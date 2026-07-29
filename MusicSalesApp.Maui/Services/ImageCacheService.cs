using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace MusicSalesApp.Maui.Services;

/// <summary>
/// On-disk cache for song artwork (album art and creator persona images), so covers still render when
/// the device is offline.
///
/// MAUI's built-in UriImageSource cache cannot do this job: it is keyed on the full URL, and the server
/// mints a fresh SAS query string for every image on every API call, so its entries never match across
/// sessions. Keys here come from <see cref="StableRemoteAssetKey"/>, which hashes the blob path only.
/// </summary>
public interface IImageCacheService
{
    /// <summary>
    /// Local path for an already-cached image, or null. Cheap enough (a single File.Exists) to call
    /// from the UI thread; never performs a download.
    /// </summary>
    string? TryGetCachedImagePath(string? remoteImageUrl);

    /// <summary>
    /// Returns the local path, downloading the image first if necessary. Returns null rather than
    /// throwing on any failure, and never attempts a download while offline.
    /// </summary>
    Task<string?> EnsureCachedAsync(string? remoteImageUrl, CancellationToken cancellationToken = default);

    Task<long> GetCacheUsageBytesAsync(CancellationToken cancellationToken = default);

    /// <summary>Deletes cached images that are not referenced by any of the supplied URLs.</summary>
    Task PruneAsync(IReadOnlyCollection<string> retainedRemoteImageUrls, CancellationToken cancellationToken = default);
}

public sealed class ImageCacheService : IImageCacheService
{
    /// <summary>
    /// Smallest byte count a real image can plausibly have. An Azure "AuthenticationFailed" XML body
    /// from an expired SAS token is a few hundred bytes; caching one as artwork would pin a broken
    /// image forever.
    /// </summary>
    internal const long MinPlausibleImageBytes = 512;

    private const string DefaultImageExtension = ".img";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ImageCacheService> _logger;
    private readonly IOfflineCacheSettingsService? _offlineCacheSettingsService;
    private readonly INetworkStatusService? _networkStatusService;
    private readonly string _cacheDirectory;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _downloadLocks = new();

    public ImageCacheService(
        IHttpClientFactory httpClientFactory,
        ILogger<ImageCacheService> logger,
        IOfflineCacheSettingsService offlineCacheSettingsService,
        INetworkStatusService networkStatusService)
        : this(
            httpClientFactory,
            logger,
            Path.Combine(FileSystem.Current.CacheDirectory, "image-cache"),
            offlineCacheSettingsService,
            networkStatusService)
    {
    }

    public ImageCacheService(
        IHttpClientFactory httpClientFactory,
        ILogger<ImageCacheService> logger,
        string cacheDirectory,
        IOfflineCacheSettingsService? offlineCacheSettingsService = null,
        INetworkStatusService? networkStatusService = null)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _cacheDirectory = cacheDirectory;
        _offlineCacheSettingsService = offlineCacheSettingsService;
        _networkStatusService = networkStatusService;

        // Off the constructor's thread: this service is resolved during startup on the main thread, and
        // the sweep enumerates a directory and stats every file in it. Nothing waits on the result.
        StaleTemporaryFileSweep = Task.Run(SweepStaleTemporaryFiles);
    }

    /// <summary>Completion of the background startup sweep. Exposed so tests need not poll.</summary>
    internal Task StaleTemporaryFileSweep { get; }

    public string? TryGetCachedImagePath(string? remoteImageUrl)
    {
        if (!StableRemoteAssetKey.TryGetAbsoluteUri(remoteImageUrl, out var remoteUri))
        {
            return null;
        }

        var cachePath = GetCachePath(remoteUri);
        return HasCachedFile(cachePath) ? cachePath : null;
    }

    public async Task<string?> EnsureCachedAsync(string? remoteImageUrl, CancellationToken cancellationToken = default)
    {
        if (!StableRemoteAssetKey.TryGetAbsoluteUri(remoteImageUrl, out var remoteUri))
        {
            return null;
        }

        var cachePath = GetCachePath(remoteUri);
        if (await Task.Run(() => HasCachedFile(cachePath), cancellationToken).ConfigureAwait(false))
        {
            return cachePath;
        }

        // With no network at all the request cannot succeed and would only stall for the client
        // timeout, so skip it; the caller falls back to the built-in placeholder artwork. Only
        // NetworkAccess.None qualifies - on a constrained connection the download usually works, and
        // giving up would leave covers blank for no reason.
        if (_networkStatusService?.HasNoNetworkAccess == true)
        {
            return null;
        }

        var downloadLock = _downloadLocks.GetOrAdd(cachePath, _ => new SemaphoreSlim(1, 1));
        await downloadLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        var temporaryPath = cachePath + ".tmp";
        try
        {
            if (await Task.Run(() => HasCachedFile(cachePath), cancellationToken).ConfigureAwait(false))
            {
                return cachePath;
            }

            if (!CanQueueDownload(remoteUri, null))
            {
                return null;
            }

            Directory.CreateDirectory(_cacheDirectory);

            // Reuses the audio-download client deliberately: it carries no BaseAddress, no bearer token
            // and no X-Api-Key. Azure SAS URLs are self-authenticating, and sending the app's
            // credentials to blob storage would be wrong. Do not "fix" this to use MusicSalesApi.
            var client = _httpClientFactory.CreateClient(AudioCacheService.AudioDownloadClientName);
            using var response = await client
                .GetAsync(remoteUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug(
                    "Image cache download failed. StatusCode={StatusCode}; ImagePath={ImagePath}",
                    response.StatusCode,
                    remoteUri.AbsolutePath);
                return null;
            }

            if (!CanQueueDownload(remoteUri, response.Content.Headers.ContentLength))
            {
                return null;
            }

            DeleteFileIfPresent(temporaryPath);

            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var destination = File.Create(temporaryPath))
            {
                await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
                await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            var downloadedBytes = new FileInfo(temporaryPath).Length;
            if (downloadedBytes < MinPlausibleImageBytes)
            {
                _logger.LogDebug(
                    "Image cache download rejected because the payload is too small to be an image. ImagePath={ImagePath}; DownloadedBytes={DownloadedBytes}",
                    remoteUri.AbsolutePath,
                    downloadedBytes);
                return null;
            }

            File.Move(temporaryPath, cachePath, overwrite: true);
            return cachePath;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            // Artwork is decorative - a failure must never propagate into playback or a list load.
            _logger.LogDebug(ex, "Image cache download failed. ImagePath={ImagePath}", remoteUri.AbsolutePath);
            return null;
        }
        finally
        {
            DeleteFileIfPresent(temporaryPath);
            downloadLock.Release();
        }
    }

    public Task<long> GetCacheUsageBytesAsync(CancellationToken cancellationToken = default)
        => Task.Run(GetCacheDirectorySizeBytes, cancellationToken);

    public Task PruneAsync(
        IReadOnlyCollection<string> retainedRemoteImageUrls,
        CancellationToken cancellationToken = default)
        => Task.Run(
            () =>
            {
                try
                {
                    if (!Directory.Exists(_cacheDirectory))
                    {
                        return;
                    }

                    var retainedFileNames = retainedRemoteImageUrls
                        .Where(url => StableRemoteAssetKey.TryGetAbsoluteUri(url, out _))
                        .Select(url =>
                        {
                            StableRemoteAssetKey.TryGetAbsoluteUri(url, out var uri);
                            return Path.GetFileName(GetCachePath(uri));
                        })
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    var removedCount = 0;
                    foreach (var file in Directory.EnumerateFiles(_cacheDirectory))
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        // Never touch .tmp files: a download in flight right now would be deleted
                        // mid-write, and because the backfill only attempts each image once per
                        // session the cover would then be missing until the app restarts. Orphaned
                        // ones are handled by the stale sweep, which checks their age first.
                        if (file.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        if (retainedFileNames.Contains(Path.GetFileName(file)))
                        {
                            continue;
                        }

                        DeleteFileIfPresent(file);
                        removedCount++;
                    }

                    if (removedCount > 0)
                    {
                        _logger.LogInformation(
                            "Pruned {RemovedCount} cached images no longer referenced by the catalog", removedCount);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to prune the image cache");
                }
            },
            cancellationToken);

    /// <summary>
    /// Filename is the path hash alone, with no song id: one persona image is shared by every song
    /// from that creator, and prefixing the id would store a separate copy per song.
    /// </summary>
    private string GetCachePath(Uri remoteUri) => Path.Combine(
        _cacheDirectory,
        StableRemoteAssetKey.GetPathHash(remoteUri, remoteUri.AbsoluteUri)
            + StableRemoteAssetKey.GetExtension(remoteUri, DefaultImageExtension));

    private bool HasCachedFile(string cachePath)
    {
        try
        {
            if (!File.Exists(cachePath))
            {
                return false;
            }

            if (new FileInfo(cachePath).Length >= MinPlausibleImageBytes)
            {
                return true;
            }

            // Undersized entry from an older build or an interrupted write - drop it so the next call
            // re-downloads rather than serving a broken image forever.
            DeleteFileIfPresent(cachePath);
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private bool CanQueueDownload(Uri remoteUri, long? contentLengthBytes)
    {
        if (_offlineCacheSettingsService == null)
        {
            return true;
        }

        var cacheSizeBytes = GetCacheDirectorySizeBytes();
        var cacheLimitBytes = _offlineCacheSettingsService.GetImageCacheLimitBytes();
        var projectedCacheSizeBytes = contentLengthBytes is > 0
            ? cacheSizeBytes + contentLengthBytes.Value
            : cacheSizeBytes;

        if (cacheSizeBytes >= cacheLimitBytes || projectedCacheSizeBytes > cacheLimitBytes)
        {
            // Warning, not Debug: a skip means artwork silently stops caching, and both log providers
            // filter Debug out. The equivalent Android audio warning is the only reason the cache-limit
            // jam was ever diagnosed.
            _logger.LogWarning(
                "Image cache download skipped because the image cache budget is reached. ImagePath={ImagePath}; CacheSizeBytes={CacheSizeBytes}; CacheLimitBytes={CacheLimitBytes}",
                remoteUri.AbsolutePath,
                cacheSizeBytes,
                cacheLimitBytes);
            return false;
        }

        var availableStorageBytes = GetAvailableCacheStorageBytes();
        var reserveBytes = _offlineCacheSettingsService.GetDeviceFreeSpaceReserveBytes();
        var projectedAvailableStorageBytes = contentLengthBytes is > 0
            ? availableStorageBytes - contentLengthBytes.Value
            : availableStorageBytes;

        if (projectedAvailableStorageBytes <= reserveBytes)
        {
            _logger.LogWarning(
                "Image cache download skipped because the device free-space reserve is reached. ImagePath={ImagePath}; AvailableStorageBytes={AvailableStorageBytes}; ReserveBytes={ReserveBytes}",
                remoteUri.AbsolutePath,
                availableStorageBytes,
                reserveBytes);
            return false;
        }

        return true;
    }

    private long GetCacheDirectorySizeBytes()
    {
        try
        {
            if (!Directory.Exists(_cacheDirectory))
            {
                return 0;
            }

            return new DirectoryInfo(_cacheDirectory).EnumerateFiles().Sum(file => file.Length);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Unable to measure image cache directory size");
            return 0;
        }
    }

    private long GetAvailableCacheStorageBytes()
    {
        try
        {
            Directory.CreateDirectory(_cacheDirectory);
            return CacheStorageProbe.GetAvailableFreeSpaceBytes(_cacheDirectory);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Unable to measure available image cache storage");
            return long.MaxValue;
        }
    }

    /// <summary>
    /// Removes orphaned .tmp files left by a process kill mid-download. They are invisible to the cache
    /// but still count against the budget.
    /// </summary>
    private void SweepStaleTemporaryFiles()
    {
        try
        {
            if (!Directory.Exists(_cacheDirectory))
            {
                return;
            }

            var cutoffUtc = DateTime.UtcNow.AddHours(-1);
            foreach (var file in Directory.EnumerateFiles(_cacheDirectory, "*.tmp"))
            {
                if (File.GetLastWriteTimeUtc(file) < cutoffUtc)
                {
                    DeleteFileIfPresent(file);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to sweep stale image cache temporary files");
        }
    }

    private static void DeleteFileIfPresent(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Leftover files are harmless; the next write overwrites them.
        }
    }
}
