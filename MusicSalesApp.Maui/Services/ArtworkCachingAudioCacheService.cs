using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Services;

/// <summary>
/// Wraps the platform audio cache so a song's artwork is downloaded alongside its audio.
///
/// Hooking the audio cache rather than the UI is what keeps the two in step: artwork is cached exactly
/// when - and only when - the audio it belongs to is cached, so everything playable offline also has a
/// cover, and nothing else is downloaded.
/// </summary>
public sealed class ArtworkCachingAudioCacheService : IAudioCacheService
{
    /// <summary>
    /// Keeps opportunistic backfill from saturating the connection while audio is still downloading.
    /// </summary>
    private const int BackfillConcurrency = 2;

    /// <summary>
    /// How many times one image may fail before it stops being retried for the rest of the session.
    /// </summary>
    private const int MaxBackfillAttemptsPerImage = 3;

    private readonly IAudioCacheService _inner;
    private readonly IImageCacheService _imageCacheService;
    private readonly INetworkStatusService? _networkStatusService;
    private readonly ILogger<ArtworkCachingAudioCacheService> _logger;
    private readonly SemaphoreSlim _backfillGate = new(BackfillConcurrency, BackfillConcurrency);

    /// <summary>
    /// Images that are cached, or have an attempt in flight, or have failed too often to keep trying.
    /// Membership is a reservation, not a record of success - see <see cref="ReleaseAttempt"/>.
    /// </summary>
    private readonly ConcurrentDictionary<string, byte> _reservedImageUrls = new();

    private readonly ConcurrentDictionary<string, int> _backfillFailureCounts = new();

    public ArtworkCachingAudioCacheService(
        IAudioCacheService inner,
        IImageCacheService imageCacheService,
        ILogger<ArtworkCachingAudioCacheService> logger,
        INetworkStatusService? networkStatusService = null)
    {
        _inner = inner;
        _imageCacheService = imageCacheService;
        _logger = logger;
        _networkStatusService = networkStatusService;
    }

    public string GetStableCacheKey(SongDto song) => _inner.GetStableCacheKey(song);

    public Task<TrackCacheStatus> GetCacheStatusAsync(SongDto song, CancellationToken cancellationToken = default)
        => _inner.GetCacheStatusAsync(song, cancellationToken);

    public async Task<IReadOnlyDictionary<int, TrackCacheStatus>> GetCacheStatusesAsync(
        IReadOnlyList<SongDto> songs,
        CancellationToken cancellationToken = default)
    {
        var statuses = await _inner.GetCacheStatusesAsync(songs, cancellationToken).ConfigureAwait(false);

        // Backfill artwork for songs whose audio was cached in an earlier session, before this decorator
        // existed or before the artwork download had a chance to run. Every readiness check in the app
        // funnels through here, so this is the natural catch-up point.
        var readySongs = songs
            .Where(song => statuses.TryGetValue(song.Id, out var status) && status.IsLocalReady)
            .ToList();

        QueueArtworkBackfill(readySongs);

        return statuses;
    }

    public async Task<TrackCacheStatus> EnsureCachedAsync(
        SongDto song,
        CachePinScope pinScope,
        CancellationToken cancellationToken = default)
    {
        var status = await _inner.EnsureCachedAsync(song, pinScope, cancellationToken).ConfigureAwait(false);

        if (status.IsLocalReady)
        {
            QueueArtworkBackfill([song]);
        }

        return status;
    }

    public void PinActiveQueue(IReadOnlyList<SongDto> songs) => _inner.PinActiveQueue(songs);

    public Task<string> ResolvePlaybackUriAsync(SongDto song, CancellationToken cancellationToken = default)
        => _inner.ResolvePlaybackUriAsync(song, cancellationToken);

    /// <summary>
    /// Reports audio plus artwork, so the settings screen's cache-usage figure reflects the real
    /// on-disk footprint without any change to the ViewModel.
    /// </summary>
    public async Task<long> GetCacheUsageBytesAsync(CancellationToken cancellationToken = default)
    {
        var audioBytes = await _inner.GetCacheUsageBytesAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return audioBytes + await _imageCacheService.GetCacheUsageBytesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Unable to include image cache usage in the reported total");
            return audioBytes;
        }
    }

    /// <summary>
    /// Fire-and-forget: artwork must never delay or fail an audio cache operation.
    /// </summary>
    private void QueueArtworkBackfill(IReadOnlyList<SongDto> songs)
    {
        if (songs.Count == 0 || _networkStatusService?.HasNoNetworkAccess == true)
        {
            return;
        }

        // The small renditions, which every list row, the mini player and the lock screen fall back
        // to. Where the server has generated one, the thumb replaces the full-size URL rather than
        // adding to it - caching both would store the same artwork twice.
        var thumbs = SelectPending(songs.SelectMany(song => new CachedImageReference[]
        {
            new(song.AlbumArtThumbUrl ?? song.AlbumArtUrl ?? string.Empty, song.AlbumArtVersion),
            new(song.PersonaImageThumbUrl ?? song.PersonaImageUrl ?? string.Empty, song.PersonaImageVersion)
        }));

        // Only offered when the server has actually generated one; there is no full-size fallback
        // here, because downloading a multi-megabyte original for the hero is precisely what this
        // feature exists to stop.
        var heroes = SelectPending(songs.SelectMany(song => new CachedImageReference[]
        {
            new(song.AlbumArtHeroUrl ?? string.Empty, song.AlbumArtVersion),
            new(song.PersonaImageHeroUrl ?? string.Empty, song.PersonaImageVersion)
        }));

        if (thumbs.Count == 0 && heroes.Count == 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                // Thumbs first, and completely, before any hero starts. With a backfill gate of two
                // a single interleaved WhenAll would let heroes consume the budget while thumbs were
                // still queued, which is the one outcome worth avoiding: a missing hero degrades to
                // an upscaled thumb, while a missing thumb leaves a blank placeholder everywhere.
                await Task.WhenAll(
                    thumbs.Select(image => CacheImageAsync(image, ImageCachePriority.Thumb)))
                    .ConfigureAwait(false);

                await Task.WhenAll(
                    heroes.Select(image => CacheImageAsync(image, ImageCachePriority.Hero)))
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Artwork backfill failed");
            }
        });
    }

    /// <summary>
    /// Filters candidates down to those worth attempting now: present, not duplicated, and not
    /// already reserved by an in-flight or completed attempt.
    /// </summary>
    private List<CachedImageReference> SelectPending(IEnumerable<CachedImageReference> candidates)
        => candidates
            .Where(image => !string.IsNullOrWhiteSpace(image.Url))
            .DistinctBy(image => (image.Url, image.Version))
            // One attempt at a time per image, and no repeat once it is cached. A failed attempt gives
            // its reservation back so a later refresh can retry - up to MaxBackfillAttemptsPerImage,
            // after which a persistently broken image stops being retried on every list refresh.
            // Renditions reserve independently of their master, since the blob paths differ.
            .Where(image => _reservedImageUrls.TryAdd(NormalizeAttemptKey(image), 0))
            .ToList();

    private async Task CacheImageAsync(CachedImageReference image, ImageCachePriority priority)
    {
        var outcome = new ImageCacheOutcome(null, ImageCacheResult.Failed);
        await _backfillGate.WaitAsync().ConfigureAwait(false);
        try
        {
            outcome = await _imageCacheService
                .TryEnsureCachedAsync(image.Url, priority, image.Version)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to cache artwork");
        }
        finally
        {
            _backfillGate.Release();

            if (outcome.Result != ImageCacheResult.Cached)
            {
                // A hero the budget turned away, or anything skipped because the device was offline,
                // is not a broken image. Counting those against the retry limit would blacklist every
                // hero for the rest of the session after three declines - permanently, since a later
                // prune frees exactly the space that would have let them through.
                ReleaseAttempt(NormalizeAttemptKey(image), countsAsFailure: !outcome.IsWorthRetrying);
            }
        }
    }

    /// <summary>
    /// Hands a failed image's reservation back so the next list refresh retries it. Without this a
    /// single dropped request - a momentary connectivity blip, a transient 503 - would leave that cover
    /// blank for the whole session even though every later refresh had a working connection. After
    /// <see cref="MaxBackfillAttemptsPerImage"/> failures the reservation is kept, so an image that is
    /// genuinely gone stops costing a request on every refresh.
    /// </summary>
    private void ReleaseAttempt(string attemptKey, bool countsAsFailure)
    {
        if (!countsAsFailure)
        {
            // Nothing was wrong with the image, so the reservation goes back with no strike against
            // it and the next refresh may try again.
            _reservedImageUrls.TryRemove(attemptKey, out _);
            return;
        }

        var failureCount = _backfillFailureCounts.AddOrUpdate(attemptKey, 1, (_, existing) => existing + 1);
        if (failureCount < MaxBackfillAttemptsPerImage)
        {
            _reservedImageUrls.TryRemove(attemptKey, out _);
        }
    }

    /// <summary>
    /// De-duplicates on the blob path, so the same image arriving with a freshly minted SAS token is
    /// recognised as already attempted. The version is part of the key as well: a re-crop leaves the
    /// path unchanged, and without it a session that had already attempted the old image would never
    /// fetch the new one.
    /// </summary>
    private static string NormalizeAttemptKey(CachedImageReference image)
    {
        var path = StableRemoteAssetKey.TryGetAbsoluteUri(image.Url, out var uri)
            ? uri.AbsolutePath.ToLowerInvariant()
            : image.Url;

        return image.Version > 0 ? $"{path}|v{image.Version}" : path;
    }
}
