using Microsoft.Extensions.Logging;
using Microsoft.Maui.Networking;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Services;

/// <summary>
/// Wraps <see cref="MusicService"/> so the song catalog survives losing the network.
///
/// On a successful live load it snapshots the catalog to <see cref="IOfflineSongCatalogStore"/>. When
/// the API is unreachable it restores that snapshot and narrows it to songs whose audio is actually
/// cached, so everything the user can see offline is something they can play.
///
/// This is a decorator rather than an edit to <see cref="MusicService"/> because it is the single
/// choke point for every caller of <see cref="IMusicService.GetSongsAsync()"/> - the library, home,
/// playlist player and deep-link handler all get offline behaviour with no call-site changes, and the
/// "offline means cached-only" rule lives in exactly one place.
/// </summary>
public sealed class OfflineAwareMusicService : IMusicService
{
    private readonly IMusicService _inner;
    private readonly IOfflineSongCatalogStore _catalogStore;
    private readonly ITrackCacheService _trackCacheService;
    private readonly IConnectivity _connectivity;
    private readonly IImageCacheService? _imageCacheService;
    private readonly ILogger<OfflineAwareMusicService> _logger;

    public OfflineAwareMusicService(
        IMusicService inner,
        IOfflineSongCatalogStore catalogStore,
        ITrackCacheService trackCacheService,
        IConnectivity connectivity,
        ILogger<OfflineAwareMusicService> logger,
        IImageCacheService? imageCacheService = null)
    {
        _inner = inner;
        _catalogStore = catalogStore;
        _trackCacheService = trackCacheService;
        _connectivity = connectivity;
        _imageCacheService = imageCacheService;
        _logger = logger;
    }

    public event Action<int, int>? OnStreamCountRecorded
    {
        add => _inner.OnStreamCountRecorded += value;
        remove => _inner.OnStreamCountRecorded -= value;
    }

    /// <summary>
    /// Last-load state, kept for the <see cref="IMusicService"/> contract. Callers that need to know
    /// which load an answer belongs to should use <see cref="SongCatalogOutcome.For"/> on the returned
    /// list instead - these two properties are shared by every caller and can be overwritten by a
    /// concurrent load between an await completing and the property being read.
    /// </summary>
    public string? LastSongsError { get; private set; }

    /// <inheritdoc cref="LastSongsError"/>
    public SongCatalogSource LastSongsSource { get; private set; } = SongCatalogSource.Live;

    public Task<List<SongDto>> GetSongsAsync()
        => GetSongsAsync(CancellationToken.None);

    public async Task<List<SongDto>> GetSongsAsync(CancellationToken cancellationToken)
    {
        // NetworkAccess.None, not INetworkStatusService.IsOffline: the latter is "!= Internet", which
        // also covers Unknown and ConstrainedInternet and would wrongly skip a live call that would
        // have succeeded. When the platform is certain there is no network, skipping the request also
        // avoids burning the full songs-request timeout on a DNS lookup that cannot resolve.
        var hasNoNetwork = _connectivity.NetworkAccess == NetworkAccess.None;

        if (!hasNoNetwork)
        {
            var liveSongs = await _inner.GetSongsAsync(cancellationToken).ConfigureAwait(false);

            if (liveSongs.Count > 0)
            {
                await SaveCatalogSnapshotAsync(liveSongs, cancellationToken).ConfigureAwait(false);
                PruneCachedArtwork(liveSongs);
                return Publish(liveSongs, SongCatalogSource.Live, _inner.LastSongsError);
            }

            // An empty list with no error is a genuinely empty catalog, not a failure. Fall through to
            // the offline path only when the live call actually failed.
            if (_inner.LastSongsError is null)
            {
                return Publish(liveSongs, SongCatalogSource.Live, null);
            }
        }

        return await LoadCachedSongsAsync(hasNoNetwork, cancellationToken).ConfigureAwait(false);
    }

    private async Task<List<SongDto>> LoadCachedSongsAsync(bool hasNoNetwork, CancellationToken cancellationToken)
    {
        List<SongDto> playableSongs;
        try
        {
            var cachedSongs = await _catalogStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            playableSongs = cachedSongs.Count == 0
                ? []
                : await FilterToLocallyPlayableAsync(cachedSongs, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to restore the offline song catalog");
            playableSongs = [];
        }

        if (playableSongs.Count > 0)
        {
            await ApplyPendingLikeStatesAsync(playableSongs).ConfigureAwait(false);

            _logger.LogInformation(
                "Serving {SongCount} songs from the offline catalog because the live catalog was unavailable",
                playableSongs.Count);

            // Clearing the error is the point: the caller shows a friendly offline state instead of the
            // raw "Unable to load data from https://.../api/music/songs" diagnostic.
            return Publish(playableSongs, SongCatalogSource.OfflineCache, null);
        }

        // Offline with nothing downloaded is a normal state, not an error worth showing a URL for.
        // Online-but-broken keeps the existing diagnostic verbatim.
        return Publish([], SongCatalogSource.Unavailable, hasNoNetwork ? null : _inner.LastSongsError);
    }

    /// <summary>
    /// Tags the outcome onto the list the caller gets back, and mirrors it into the shared properties
    /// for anything still reading those.
    /// </summary>
    private SongCatalogList Publish(IEnumerable<SongDto> songs, SongCatalogSource source, string? error)
    {
        LastSongsError = error;
        LastSongsSource = source;
        return new SongCatalogList(songs, source, error);
    }

    private async Task<List<SongDto>> FilterToLocallyPlayableAsync(
        IReadOnlyList<SongDto> cachedSongs,
        CancellationToken cancellationToken)
    {
        // Readiness is always re-resolved from the live cache rather than trusted from the snapshot, so
        // an OS cache purge simply shrinks the offline library instead of surfacing unplayable songs.
        var statuses = await _trackCacheService
            .GetCacheStatusesAsync(cachedSongs, cancellationToken)
            .ConfigureAwait(false);

        return cachedSongs
            .Where(song => statuses.TryGetValue(song.Id, out var status) && status.IsLocalReady)
            .ToList();
    }

    /// <summary>
    /// Replays queued thumbs-up/down intents over the restored snapshot, so an optimistic tap made
    /// offline survives an app restart instead of appearing to have been discarded.
    /// </summary>
    private async Task ApplyPendingLikeStatesAsync(IReadOnlyList<SongDto> songs)
    {
        try
        {
            var pendingLikeStates = await _inner.GetPendingLikeStatesAsync().ConfigureAwait(false);
            PendingLikeStateApplier.Apply(songs, pendingLikeStates);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to apply pending like states to the offline catalog");
        }
    }

    /// <summary>
    /// Drops cached artwork for songs no longer in the catalog. Fire-and-forget on the live-load path:
    /// a full catalog is the only moment the app knows what artwork is still reachable, and re-download
    /// is cheap if the prune is ever wrong.
    /// </summary>
    private void PruneCachedArtwork(IReadOnlyList<SongDto> songs)
    {
        if (_imageCacheService == null)
        {
            return;
        }

        // Every URL the app may have cached has to be named here. The prune deletes any file whose
        // name is not derived from this list, and each pre-resized rendition is a distinct blob path
        // and therefore a distinct cache entry. Omitting one would delete it after every catalog
        // load and re-download it moments later - a silent, permanent loop burning the user's data.
        // Each entry carries the version it was cached under, so a superseded copy left behind by an
        // earlier version of the same image is swept up rather than counting against the budget
        // forever.
        // The full-size masters are retained only until their thumbs are actually cached. They stay
        // reachable while the thumb is missing - the display chain still falls back to them - but a
        // multi-megabyte original kept permanently beside a twenty-kilobyte rendition would eat the
        // budget the renditions exist to free, and the budget has no eviction to recover from that.
        var retainedImages = songs
            .SelectMany(song => new CachedImageReference[]
            {
                new CachedImageReference(song.AlbumArtUrl ?? string.Empty, song.AlbumArtVersion)
                    .RetainedUntilCached(song.AlbumArtThumbUrl, song.AlbumArtVersion),
                new(song.AlbumArtThumbUrl ?? string.Empty, song.AlbumArtVersion),
                new(song.AlbumArtHeroUrl ?? string.Empty, song.AlbumArtVersion),
                new CachedImageReference(song.PersonaImageUrl ?? string.Empty, song.PersonaImageVersion)
                    .RetainedUntilCached(song.PersonaImageThumbUrl, song.PersonaImageVersion),
                new(song.PersonaImageThumbUrl ?? string.Empty, song.PersonaImageVersion),
                new(song.PersonaImageHeroUrl ?? string.Empty, song.PersonaImageVersion)
            })
            .Where(image => !string.IsNullOrWhiteSpace(image.Url))
            .DistinctBy(image => (image.Url, image.Version))
            .ToList();

        _ = Task.Run(async () =>
        {
            try
            {
                await _imageCacheService.PruneAsync(retainedImages).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to prune cached artwork");
            }
        });
    }

    private async Task SaveCatalogSnapshotAsync(IReadOnlyList<SongDto> songs, CancellationToken cancellationToken)
    {
        try
        {
            await _catalogStore.SaveAsync(songs, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Snapshotting is best-effort; a store failure must never break a load that succeeded.
            _logger.LogWarning(ex, "Failed to snapshot the song catalog for offline use");
        }
    }

    public Task<SongDto?> GetSongByTitleAsync(string title) => _inner.GetSongByTitleAsync(title);

    public Task<int> GetStreamQualifyingSecondsAsync() => _inner.GetStreamQualifyingSecondsAsync();

    public Task<int?> RecordStreamAsync(int songMetadataId) => _inner.RecordStreamAsync(songMetadataId);

    public Task FlushPendingStreamRecordsAsync() => _inner.FlushPendingStreamRecordsAsync();

    public Task ClearPendingStreamRecordsAsync() => _inner.ClearPendingStreamRecordsAsync();

    public Task<List<LikeCountDto>> GetBulkLikeCountsAsync(IEnumerable<int> songIds)
        => _inner.GetBulkLikeCountsAsync(songIds);

    public Task<Dictionary<int, bool?>> GetBulkUserLikeStatusAsync(IEnumerable<int> songIds)
        => _inner.GetBulkUserLikeStatusAsync(songIds);

    public Task<LikeToggleResult?> ToggleLikeAsync(int songMetadataId) => _inner.ToggleLikeAsync(songMetadataId);

    public Task<LikeToggleResult?> ToggleDislikeAsync(int songMetadataId) => _inner.ToggleDislikeAsync(songMetadataId);

    public Task<SetLikeStateOutcome> SetLikeStateAsync(int songMetadataId, bool? desiredState)
        => _inner.SetLikeStateAsync(songMetadataId, desiredState);

    public Task FlushPendingLikeStatesAsync() => _inner.FlushPendingLikeStatesAsync();

    public Task ClearPendingLikeStatesAsync() => _inner.ClearPendingLikeStatesAsync();

    public Task<IReadOnlyDictionary<int, bool?>> GetPendingLikeStatesAsync() => _inner.GetPendingLikeStatesAsync();

    public Task<(bool Success, string ErrorMessage)> VerifySubscriptionPurchaseAsync(
        BillingPurchaseVerificationRequest request) => _inner.VerifySubscriptionPurchaseAsync(request);

    public Task<(bool Success, string ErrorMessage)> VerifyGooglePlayPurchaseAsync(string purchaseToken, string? orderId)
        => _inner.VerifyGooglePlayPurchaseAsync(purchaseToken, orderId);

    public Task<SubscriptionStatusDto?> GetSubscriptionStatusAsync() => _inner.GetSubscriptionStatusAsync();

    public Task<(bool Success, DateTime? EndDate)> CancelSubscriptionAsync() => _inner.CancelSubscriptionAsync();

    public Task<bool> ReportSongAsync(int songMetadataId, string reason)
        => _inner.ReportSongAsync(songMetadataId, reason);
}
