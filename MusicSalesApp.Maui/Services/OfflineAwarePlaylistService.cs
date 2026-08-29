using Microsoft.Extensions.Logging;
using Microsoft.Maui.Networking;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Services;

/// <summary>
/// Where the last playlist read came from. Lets callers tell "offline, showing what is downloaded"
/// apart from "you have no playlists", which previously rendered identically.
/// </summary>
public enum PlaylistDataSource
{
    Live = 0,
    OfflineCache = 1,
    Unavailable = 2
}

/// <summary>
/// Adds a source signal to <see cref="IPlaylistService"/>. Split out from the base interface so the
/// existing twelve methods keep their shapes and every existing mock keeps compiling.
/// </summary>
public interface IPlaylistDataSourceReporter
{
    PlaylistDataSource LastPlaylistSource { get; }
}

/// <summary>
/// Wraps <see cref="PlaylistService"/> so playlists survive losing the network, mirroring what
/// <see cref="OfflineAwareMusicService"/> does for the song catalog.
///
/// Reads snapshot on success and restore on failure, narrowed to songs whose audio is cached, so a
/// playlist opened offline contains exactly the tracks that will actually play. Writes are not
/// queued: editing a playlist offline is surfaced as an explicit "unavailable offline" result, and the
/// UI hides the controls entirely.
/// </summary>
public sealed class OfflineAwarePlaylistService : IPlaylistService, IPlaylistDataSourceReporter
{
    internal const string OfflineEditMessage = "Playlist editing isn't available offline. Reconnect to make changes.";

    private readonly IPlaylistService _inner;
    private readonly IOfflinePlaylistStore _playlistStore;
    private readonly ITrackCacheService _trackCacheService;
    private readonly IConnectivity _connectivity;
    private readonly ILogger<OfflineAwarePlaylistService> _logger;

    public OfflineAwarePlaylistService(
        IPlaylistService inner,
        IOfflinePlaylistStore playlistStore,
        ITrackCacheService trackCacheService,
        IConnectivity connectivity,
        ILogger<OfflineAwarePlaylistService> logger)
    {
        _inner = inner;
        _playlistStore = playlistStore;
        _trackCacheService = trackCacheService;
        _connectivity = connectivity;
        _logger = logger;
    }

    public PlaylistDataSource LastPlaylistSource { get; private set; } = PlaylistDataSource.Live;

    /// <summary>
    /// Gated on NetworkAccess.None rather than "not Internet", so an Unknown or constrained connection
    /// still gets a real attempt. Matches OfflineAwareMusicService.
    /// </summary>
    private bool HasNoNetwork => _connectivity.NetworkAccess == NetworkAccess.None;

    // --- Reads ---

    public async Task<HomePlaylistsDto?> GetHomePlaylistsAsync()
    {
        if (!HasNoNetwork)
        {
            var live = await _inner.GetHomePlaylistsAsync().ConfigureAwait(false);
            if (live != null)
            {
                await SafelyAsync(() => _playlistStore.SaveHomePlaylistsAsync(live)).ConfigureAwait(false);
                LastPlaylistSource = PlaylistDataSource.Live;
                return live;
            }
        }

        var cached = await SafelyAsync(() => _playlistStore.LoadHomePlaylistsAsync()).ConfigureAwait(false);
        LastPlaylistSource = cached is null ? PlaylistDataSource.Unavailable : PlaylistDataSource.OfflineCache;
        return cached;
    }

    public async Task<List<PlaylistDto>> GetMyPlaylistsAsync()
    {
        if (!HasNoNetwork)
        {
            var live = await _inner.GetMyPlaylistsAsync().ConfigureAwait(false);
            if (live.Count > 0)
            {
                await SafelyAsync(() => _playlistStore.SaveMyPlaylistsAsync(live)).ConfigureAwait(false);
                LastPlaylistSource = PlaylistDataSource.Live;
                return live;
            }

            // PlaylistService returns [] for both "no playlists" and "request failed", so an empty live
            // result is only trusted when the device is definitely online.
            if (_connectivity.NetworkAccess == NetworkAccess.Internet)
            {
                LastPlaylistSource = PlaylistDataSource.Live;
                return live;
            }
        }

        var cached = await SafelyAsync(() => _playlistStore.LoadMyPlaylistsAsync()).ConfigureAwait(false) ?? [];
        LastPlaylistSource = cached.Count == 0 ? PlaylistDataSource.Unavailable : PlaylistDataSource.OfflineCache;
        return cached;
    }

    public async Task<PlaylistSongsDto?> GetPlaylistSongsAsync(int playlistId)
    {
        if (!HasNoNetwork)
        {
            var live = await _inner.GetPlaylistSongsAsync(playlistId).ConfigureAwait(false);
            if (live != null)
            {
                await SafelyAsync(() => _playlistStore.SavePlaylistSongsAsync(playlistId, live)).ConfigureAwait(false);
                LastPlaylistSource = PlaylistDataSource.Live;
                return live;
            }
        }

        return await RestorePlaylistSongsAsync(() => _playlistStore.LoadPlaylistSongsAsync(playlistId))
            .ConfigureAwait(false);
    }

    public async Task<PlaylistSongsDto?> GetRecommendedSongsAsync()
    {
        if (!HasNoNetwork)
        {
            var live = await _inner.GetRecommendedSongsAsync().ConfigureAwait(false);
            if (live != null)
            {
                await SafelyAsync(() => _playlistStore.SaveRecommendedSongsAsync(live)).ConfigureAwait(false);
                LastPlaylistSource = PlaylistDataSource.Live;
                return live;
            }
        }

        return await RestorePlaylistSongsAsync(() => _playlistStore.LoadRecommendedSongsAsync())
            .ConfigureAwait(false);
    }

    public async Task<List<PlaylistDto>> GetTopStreamedPlaylistsAsync()
    {
        if (!HasNoNetwork)
        {
            var live = await _inner.GetTopStreamedPlaylistsAsync().ConfigureAwait(false);
            if (live.Count > 0)
            {
                await SafelyAsync(() => _playlistStore.SaveTopStreamedPlaylistsAsync(live)).ConfigureAwait(false);
                LastPlaylistSource = PlaylistDataSource.Live;
                return live;
            }

            // An empty live result is only trusted when the device is definitely online - the same
            // reasoning as GetMyPlaylistsAsync, since the inner call also returns [] on failure.
            if (_connectivity.NetworkAccess == NetworkAccess.Internet)
            {
                LastPlaylistSource = PlaylistDataSource.Live;
                return live;
            }
        }

        var cached = await SafelyAsync(() => _playlistStore.LoadTopStreamedPlaylistsAsync()).ConfigureAwait(false) ?? [];
        LastPlaylistSource = cached.Count == 0 ? PlaylistDataSource.Unavailable : PlaylistDataSource.OfflineCache;
        return cached;
    }

    public async Task<PlaylistSongsDto?> GetTopStreamedSongsAsync(string window)
    {
        if (!HasNoNetwork)
        {
            var live = await _inner.GetTopStreamedSongsAsync(window).ConfigureAwait(false);
            if (live != null)
            {
                await SafelyAsync(() => _playlistStore.SaveTopStreamedSongsAsync(window, live)).ConfigureAwait(false);
                LastPlaylistSource = PlaylistDataSource.Live;
                return live;
            }
        }

        return await RestorePlaylistSongsAsync(() => _playlistStore.LoadTopStreamedSongsAsync(window))
            .ConfigureAwait(false);
    }

    private async Task<PlaylistSongsDto?> RestorePlaylistSongsAsync(Func<Task<PlaylistSongsDto?>> load)
    {
        var cached = await SafelyAsync(load).ConfigureAwait(false);
        if (cached is null)
        {
            LastPlaylistSource = PlaylistDataSource.Unavailable;
            return null;
        }

        var playableSongs = await FilterToLocallyPlayableAsync(cached.Songs).ConfigureAwait(false);
        if (playableSongs.Count == 0)
        {
            LastPlaylistSource = PlaylistDataSource.Unavailable;
            return null;
        }

        _logger.LogInformation(
            "Serving {SongCount} of {TotalCount} playlist songs from the offline snapshot",
            playableSongs.Count,
            cached.Songs.Count);

        LastPlaylistSource = PlaylistDataSource.OfflineCache;
        return new PlaylistSongsDto
        {
            PlaylistId = cached.PlaylistId,
            PlaylistName = cached.PlaylistName,
            IsSystemGenerated = cached.IsSystemGenerated,
            PeriodLabel = cached.PeriodLabel,
            Songs = playableSongs
        };
    }

    /// <summary>
    /// Narrows a snapshot to songs whose audio is genuinely on disk. Readiness is re-resolved from the
    /// live cache every time rather than trusted from the snapshot, so an OS cache purge just shrinks
    /// the playlist instead of offering tracks that cannot play.
    /// </summary>
    private async Task<List<PlaylistSongDto>> FilterToLocallyPlayableAsync(List<PlaylistSongDto> songs)
    {
        if (songs.Count == 0)
        {
            return [];
        }

        // GetCacheStatusesAsync keys on SongDto.Id, which PlaylistPlayerViewModel maps from
        // SongMetadataId - so the probe has to use the same id or nothing will ever match.
        var probes = songs
            .Select(song => new SongDto
            {
                Id = song.SongMetadataId != 0 ? song.SongMetadataId : song.Id,
                StreamUrl = song.StreamUrl
            })
            .ToList();

        var statuses = await _trackCacheService.GetCacheStatusesAsync(probes).ConfigureAwait(false);

        return songs
            .Where(song =>
            {
                var probeId = song.SongMetadataId != 0 ? song.SongMetadataId : song.Id;
                return statuses.TryGetValue(probeId, out var status) && status.IsLocalReady;
            })
            .ToList();
    }

    private async Task<T?> SafelyAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return await operation().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Offline playlist snapshot operation failed");
            return default;
        }
    }

    private async Task SafelyAsync(Func<Task> operation)
    {
        try
        {
            await operation().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Offline playlist snapshot operation failed");
        }
    }

    // --- Writes ---
    //
    // Playlist edits are not queued. Nobody expects to reorganise their library in airplane mode, and
    // replaying edits would need conflict resolution against whatever the server did meanwhile. The UI
    // hides these controls offline; this is the backstop for anything that slips through.

    public Task<PlaylistOperationResult<PlaylistDto>> CreatePlaylistAsync(string name)
        => HasNoNetwork
            ? Task.FromResult(PlaylistOperationResult<PlaylistDto>.Fail(OfflineEditMessage))
            : _inner.CreatePlaylistAsync(name);

    public Task<PlaylistOperationResult> RenamePlaylistAsync(int playlistId, string name)
        => HasNoNetwork
            ? Task.FromResult(PlaylistOperationResult.Fail(OfflineEditMessage))
            : _inner.RenamePlaylistAsync(playlistId, name);

    public Task<PlaylistOperationResult> DeletePlaylistAsync(int playlistId)
        => HasNoNetwork
            ? Task.FromResult(PlaylistOperationResult.Fail(OfflineEditMessage))
            : _inner.DeletePlaylistAsync(playlistId);

    public Task<AvailableSongsResponse> GetAvailableSongsAsync(int playlistId)
        => _inner.GetAvailableSongsAsync(playlistId);

    public Task<PlaylistOperationResult> AddSongAsync(int playlistId, int songMetadataId)
        => HasNoNetwork
            ? Task.FromResult(PlaylistOperationResult.Fail(OfflineEditMessage))
            : _inner.AddSongAsync(playlistId, songMetadataId);

    public Task<PlaylistOperationResult> RemoveSongAsync(int playlistId, int userPlaylistId)
        => HasNoNetwork
            ? Task.FromResult(PlaylistOperationResult.Fail(OfflineEditMessage))
            : _inner.RemoveSongAsync(playlistId, userPlaylistId);

    public Task<PlaylistOperationResult> ReorderAsync(int playlistId, IReadOnlyList<int> userPlaylistIdsInOrder)
        => HasNoNetwork
            ? Task.FromResult(PlaylistOperationResult.Fail(OfflineEditMessage))
            : _inner.ReorderAsync(playlistId, userPlaylistIdsInOrder);
}
