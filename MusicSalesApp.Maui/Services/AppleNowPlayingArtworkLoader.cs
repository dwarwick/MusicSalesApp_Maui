#if IOS || MACCATALYST
using Foundation;
using ImageIO;
using Microsoft.Extensions.Logging;
using UIKit;

namespace MusicSalesApp.Maui.Services;

/// <summary>
/// Decodes lock-screen / Control Center artwork into a <see cref="UIImage"/> for
/// <see cref="NowPlayingArtworkCoordinator"/>.
///
/// <para>
/// Everything here runs off the main thread. Plugin.MediaManager's own image provider is deliberately
/// not used: it reaches the network through <c>NSData.FromUrl</c>, which blocks the calling thread for
/// the duration of the request.
/// </para>
/// </summary>
public sealed class AppleNowPlayingArtworkLoader : INowPlayingArtworkLoader
{
    /// <summary>
    /// The artwork is never drawn larger than the Control Center hero view, so decoding beyond this is
    /// wasted. It is also a safety limit rather than a preference: the artwork ladder can fall all the
    /// way through to the full-size master when a server generated no renditions, and a 4000px
    /// original decodes to roughly 64 MB - a jetsam risk in a backgrounded audio app.
    /// </summary>
    internal const int MaxArtworkPixelSize = 1024;

    /// <summary>
    /// Hard ceiling on a downloaded artwork body.
    ///
    /// <para>
    /// The artwork ladder can fall all the way through to the full-size master when a server generated
    /// no renditions - which is exactly what <c>ArtworkCachingAudioCacheService</c> refuses to do for
    /// the hero tier - so without a cap a single track could buffer tens of megabytes into a byte[] in
    /// an app that is usually backgrounded and jetsam-sensitive. Anything larger is treated as
    /// unavailable rather than retried; re-fetching it would only buffer it again.
    /// </para>
    /// </summary>
    internal const int MaxArtworkDownloadBytes = 8 * 1024 * 1024;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IImageCacheService _imageCacheService;
    private readonly ILogger<AppleNowPlayingArtworkLoader> _logger;

    public AppleNowPlayingArtworkLoader(
        IHttpClientFactory httpClientFactory,
        IImageCacheService imageCacheService,
        ILogger<AppleNowPlayingArtworkLoader> logger)
    {
        _httpClientFactory = httpClientFactory;
        _imageCacheService = imageCacheService;
        _logger = logger;
    }

    public async Task<NowPlayingArtworkLoadResult> LoadAsync(
        string artworkUri,
        int contentVersion = 0,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(artworkUri, UriKind.Absolute, out var uri))
        {
            return NowPlayingArtworkLoadResult.Unavailable;
        }

        try
        {
            if (uri.IsFile)
            {
                return await LoadFromFileAsync(uri, cancellationToken).ConfigureAwait(false);
            }

            if (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            {
                return await LoadFromRemoteAsync(uri, contentVersion, cancellationToken).ConfigureAwait(false);
            }

            return NowPlayingArtworkLoadResult.Unavailable;
        }
        catch (OperationCanceledException)
        {
            return NowPlayingArtworkLoadResult.Retryable;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            // Artwork URLs are SAS-signed, so the query string is a credential and never gets logged.
            _logger.LogWarning(
                ex,
                "Could not fetch now playing artwork. Scheme={ArtworkScheme}; ArtworkPath={ArtworkPath}",
                uri.Scheme,
                uri.AbsolutePath);
            return NowPlayingArtworkLoadResult.Retryable;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Unexpected failure decoding now playing artwork. Scheme={ArtworkScheme}; ArtworkPath={ArtworkPath}",
                uri.Scheme,
                uri.AbsolutePath);
            return NowPlayingArtworkLoadResult.Unavailable;
        }
    }

    private Task<NowPlayingArtworkLoadResult> LoadFromFileAsync(Uri uri, CancellationToken cancellationToken)
    {
        var localPath = uri.LocalPath;

        return Task.Run(
            () =>
            {
                if (!File.Exists(localPath))
                {
                    // The cache was pruned, or the backfill has not landed yet. Both resolve on their
                    // own, so this is worth another look on a later heartbeat.
                    return NowPlayingArtworkLoadResult.Retryable;
                }

                using var source = CGImageSource.FromUrl(NSUrl.FromFilename(localPath));
                return Downsample(source);
            },
            cancellationToken);
    }

    /// <summary>
    /// Caches the image first, then decodes the file that lands on disk.
    ///
    /// <para>
    /// Going through <see cref="IImageCacheService"/> rather than fetching straight into memory is what
    /// makes lock-screen artwork survive airplane mode: the next play resolves a <c>file://</c> URI
    /// from the cache and needs no network at all. It also inherits the budget accounting, the
    /// magic-byte validation, and the versioned <c>StableRemoteAssetKey</c> naming - which is precisely
    /// why the content version has to be plumbed this far.
    /// </para>
    /// </summary>
    private async Task<NowPlayingArtworkLoadResult> LoadFromRemoteAsync(
        Uri uri,
        int contentVersion,
        CancellationToken cancellationToken)
    {
        var outcome = await _imageCacheService
            .TryEnsureCachedAsync(uri.ToString(), ImageCachePriority.Hero, contentVersion, cancellationToken)
            .ConfigureAwait(false);

        switch (outcome.Result)
        {
            case ImageCacheResult.Cached when !string.IsNullOrWhiteSpace(outcome.Path):
                return await LoadFromFileAsync(new Uri(outcome.Path), cancellationToken).ConfigureAwait(false);

            case ImageCacheResult.Declined:
                // The hero budget is full. The cache is right to refuse - heroes must never crowd out
                // the thumbs every list row needs - but the lock screen should still show something,
                // so fetch this one into memory without persisting it.
                _logger.LogInformation(
                    "Image cache declined the now playing artwork; fetching it without persisting. ArtworkPath={ArtworkPath}",
                    uri.AbsolutePath);
                return await LoadFromNetworkAsync(uri, cancellationToken).ConfigureAwait(false);

            case ImageCacheResult.Offline:
                return NowPlayingArtworkLoadResult.Retryable;

            case ImageCacheResult.NoImage:
                return NowPlayingArtworkLoadResult.Unavailable;

            default:
                return outcome.IsWorthRetrying
                    ? NowPlayingArtworkLoadResult.Retryable
                    : NowPlayingArtworkLoadResult.Unavailable;
        }
    }

    private async Task<NowPlayingArtworkLoadResult> LoadFromNetworkAsync(Uri uri, CancellationToken cancellationToken)
    {
        // The audio-download client specifically: it carries no BaseAddress, no bearer token and no
        // API key, which is the same reason ImageCacheService uses it - app credentials have no
        // business being sent to Azure blob storage.
        var client = _httpClientFactory.CreateClient(AudioCacheService.AudioDownloadClientName);

        // ResponseHeadersRead so Content-Length can be checked before a byte of body is buffered -
        // the same guard ImageCacheService applies for the same reason.
        using var response = await client
            .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Now playing artwork request failed. Status={StatusCode}; ArtworkPath={ArtworkPath}",
                (int)response.StatusCode,
                uri.AbsolutePath);
            return NowPlayingArtworkLoadResult.Retryable;
        }

        if (response.Content.Headers.ContentLength > MaxArtworkDownloadBytes)
        {
            _logger.LogWarning(
                "Refusing oversized now playing artwork. ContentLength={ContentLength}; Limit={Limit}; ArtworkPath={ArtworkPath}",
                response.Content.Headers.ContentLength,
                MaxArtworkDownloadBytes,
                uri.AbsolutePath);
            return NowPlayingArtworkLoadResult.Unavailable;
        }

        var payload = await ReadCappedAsync(response, uri, cancellationToken).ConfigureAwait(false);
        if (payload is null)
        {
            return NowPlayingArtworkLoadResult.Unavailable;
        }

        if (payload.Length == 0)
        {
            return NowPlayingArtworkLoadResult.Retryable;
        }

        return await Task.Run(
            () =>
            {
                using var data = NSData.FromArray(payload);
                using var source = CGImageSource.FromData(data);
                return Downsample(source);
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Buffers the body, abandoning it if it runs past <see cref="MaxArtworkDownloadBytes"/> - a
    /// server that omits Content-Length must not be able to bypass the ceiling.
    /// </summary>
    private async Task<byte[]?> ReadCappedAsync(
        HttpResponseMessage response,
        Uri uri,
        CancellationToken cancellationToken)
    {
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];

        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (buffer.Length + read > MaxArtworkDownloadBytes)
            {
                _logger.LogWarning(
                    "Abandoned oversized now playing artwork mid-download. Limit={Limit}; ArtworkPath={ArtworkPath}",
                    MaxArtworkDownloadBytes,
                    uri.AbsolutePath);
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// Reads and downsamples in one pass, so the full-resolution bitmap is never materialised.
    /// </summary>
    private static NowPlayingArtworkLoadResult Downsample(CGImageSource? source)
    {
        if (source is null)
        {
            return NowPlayingArtworkLoadResult.Unavailable;
        }

        var options = new CGImageThumbnailOptions
        {
            CreateThumbnailFromImageAlways = true,
            CreateThumbnailWithTransform = true,
            // Decode here, on this background thread, rather than lazily on the first draw.
            ShouldCacheImmediately = true,
            MaxPixelSize = MaxArtworkPixelSize
        };

        using var cgImage = source.CreateThumbnail(0, options);
        if (cgImage is null)
        {
            // Downloaded cleanly and still is not a decodable image - an expired-SAS error document,
            // say. No later attempt will change that, so do not ask for a retry.
            return NowPlayingArtworkLoadResult.Unavailable;
        }

        return NowPlayingArtworkLoadResult.Loaded(UIImage.FromImage(cgImage));
    }
}
#endif
