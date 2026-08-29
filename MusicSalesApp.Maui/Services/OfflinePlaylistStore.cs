using System.Text.Json;
using Microsoft.Extensions.Logging;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Services;

/// <summary>
/// Durable snapshot of the user's playlists, written after every successful live fetch so playlists
/// remain browsable and playable offline. The companion to <see cref="IOfflineSongCatalogStore"/>.
/// </summary>
public interface IOfflinePlaylistStore
{
    Task<HomePlaylistsDto?> LoadHomePlaylistsAsync(CancellationToken cancellationToken = default);
    Task SaveHomePlaylistsAsync(HomePlaylistsDto homePlaylists, CancellationToken cancellationToken = default);

    Task<List<PlaylistDto>> LoadMyPlaylistsAsync(CancellationToken cancellationToken = default);
    Task SaveMyPlaylistsAsync(IReadOnlyList<PlaylistDto> playlists, CancellationToken cancellationToken = default);

    Task<PlaylistSongsDto?> LoadPlaylistSongsAsync(int playlistId, CancellationToken cancellationToken = default);
    Task SavePlaylistSongsAsync(int playlistId, PlaylistSongsDto songs, CancellationToken cancellationToken = default);

    Task<PlaylistSongsDto?> LoadRecommendedSongsAsync(CancellationToken cancellationToken = default);
    Task SaveRecommendedSongsAsync(PlaylistSongsDto songs, CancellationToken cancellationToken = default);

    Task<List<PlaylistDto>> LoadTopStreamedPlaylistsAsync(CancellationToken cancellationToken = default);
    Task SaveTopStreamedPlaylistsAsync(IReadOnlyList<PlaylistDto> playlists, CancellationToken cancellationToken = default);

    Task<PlaylistSongsDto?> LoadTopStreamedSongsAsync(string window, CancellationToken cancellationToken = default);
    Task SaveTopStreamedSongsAsync(string window, PlaylistSongsDto songs, CancellationToken cancellationToken = default);

    /// <summary>
    /// Drops everything personal to the signed-in user, keeping the global "most streamed" sections.
    /// Called on sign-out.
    /// </summary>
    Task ClearAsync(CancellationToken cancellationToken = default);
}

public sealed class OfflinePlaylistDocument
{
    public int Version { get; set; } = OfflinePlaylistStore.CurrentVersion;

    public DateTimeOffset UpdatedUtc { get; set; }

    public HomePlaylistsDto? HomePlaylists { get; set; }

    public List<PlaylistDto> MyPlaylists { get; set; } = [];

    /// <summary>
    /// Per-playlist song lists, keyed by playlist id. The Recommended playlist has no stable id of its
    /// own, so it is stored separately.
    /// </summary>
    public Dictionary<int, PlaylistSongsDto> PlaylistSongs { get; set; } = [];

    public PlaylistSongsDto? RecommendedSongs { get; set; }

    /// <summary>
    /// The five global "most streamed" tiles.
    /// </summary>
    /// <remarks>
    /// Not personal, so unlike everything above it survives sign-out - see <c>ClearAsync</c>.
    /// </remarks>
    public List<PlaylistDto> TopStreamedPlaylists { get; set; } = [];

    /// <summary>
    /// Their song lists, keyed by <b>window</b> rather than by id.
    /// </summary>
    /// <remarks>
    /// These playlists have no row and all report <c>Id = 0</c>, so they cannot share
    /// <see cref="PlaylistSongs"/> - all five would collide on key 0, and with the Recommended list
    /// too.
    /// </remarks>
    public Dictionary<string, PlaylistSongsDto> TopStreamedSongs { get; set; } = [];
}

/// <summary>
/// JSON-file implementation, alongside the song catalog in AppDataDirectory for the same reason: the
/// OS may purge CacheDirectory, and metadata that outlives its audio is harmless while audio that
/// outlives its metadata is unreachable.
/// </summary>
public sealed class OfflinePlaylistStore : IOfflinePlaylistStore
{
    // 2: added the global top-streamed sections. Nothing validates this on read - an older document
    // simply deserialises with the new collections empty, which is the correct starting state.
    internal const int CurrentVersion = 2;
    internal const int MaxCachedPlaylists = 200;
    private const string PlaylistFileName = "playlists-v1.json";

    private static readonly JsonSerializerOptions PlaylistSerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly string _playlistFilePath;
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly ILogger<OfflinePlaylistStore> _logger;

    public OfflinePlaylistStore(ILogger<OfflinePlaylistStore> logger)
        : this(Path.Combine(FileSystem.AppDataDirectory, "offline"), logger)
    {
    }

    public OfflinePlaylistStore(string playlistDirectory, ILogger<OfflinePlaylistStore> logger)
    {
        _playlistFilePath = Path.Combine(playlistDirectory, PlaylistFileName);
        _logger = logger;
    }

    public async Task<HomePlaylistsDto?> LoadHomePlaylistsAsync(CancellationToken cancellationToken = default)
        => (await ReadDocumentAsync(cancellationToken).ConfigureAwait(false))?.HomePlaylists;

    public Task SaveHomePlaylistsAsync(HomePlaylistsDto homePlaylists, CancellationToken cancellationToken = default)
        => MutateAsync(document => document.HomePlaylists = homePlaylists, cancellationToken);

    public async Task<List<PlaylistDto>> LoadMyPlaylistsAsync(CancellationToken cancellationToken = default)
        => (await ReadDocumentAsync(cancellationToken).ConfigureAwait(false))?.MyPlaylists ?? [];

    public Task SaveMyPlaylistsAsync(
        IReadOnlyList<PlaylistDto> playlists,
        CancellationToken cancellationToken = default)
        => MutateAsync(document => document.MyPlaylists = playlists.Take(MaxCachedPlaylists).ToList(), cancellationToken);

    public async Task<PlaylistSongsDto?> LoadPlaylistSongsAsync(
        int playlistId,
        CancellationToken cancellationToken = default)
    {
        var document = await ReadDocumentAsync(cancellationToken).ConfigureAwait(false);
        return document is not null && document.PlaylistSongs.TryGetValue(playlistId, out var songs) ? songs : null;
    }

    public Task SavePlaylistSongsAsync(
        int playlistId,
        PlaylistSongsDto songs,
        CancellationToken cancellationToken = default)
        => MutateAsync(
            document =>
            {
                document.PlaylistSongs[playlistId] = songs;

                if (document.PlaylistSongs.Count > MaxCachedPlaylists)
                {
                    // Ungraceful but bounded: keep the entry just written plus an arbitrary remainder.
                    // Users have single-digit playlists in practice; this only guards runaway growth.
                    var excessKeys = document.PlaylistSongs.Keys
                        .Where(key => key != playlistId)
                        .Take(document.PlaylistSongs.Count - MaxCachedPlaylists)
                        .ToList();
                    foreach (var key in excessKeys)
                    {
                        document.PlaylistSongs.Remove(key);
                    }
                }
            },
            cancellationToken);

    public async Task<PlaylistSongsDto?> LoadRecommendedSongsAsync(CancellationToken cancellationToken = default)
        => (await ReadDocumentAsync(cancellationToken).ConfigureAwait(false))?.RecommendedSongs;

    public Task SaveRecommendedSongsAsync(PlaylistSongsDto songs, CancellationToken cancellationToken = default)
        => MutateAsync(document => document.RecommendedSongs = songs, cancellationToken);

    public async Task<List<PlaylistDto>> LoadTopStreamedPlaylistsAsync(CancellationToken cancellationToken = default)
        => (await ReadDocumentAsync(cancellationToken).ConfigureAwait(false))?.TopStreamedPlaylists ?? [];

    public Task SaveTopStreamedPlaylistsAsync(
        IReadOnlyList<PlaylistDto> playlists,
        CancellationToken cancellationToken = default)
        => MutateAsync(document => document.TopStreamedPlaylists = playlists.ToList(), cancellationToken);

    public async Task<PlaylistSongsDto?> LoadTopStreamedSongsAsync(
        string window,
        CancellationToken cancellationToken = default)
    {
        var document = await ReadDocumentAsync(cancellationToken).ConfigureAwait(false);
        return document is not null && document.TopStreamedSongs.TryGetValue(window, out var songs)
            ? songs
            : null;
    }

    public Task SaveTopStreamedSongsAsync(
        string window,
        PlaylistSongsDto songs,
        CancellationToken cancellationToken = default)
        // Bounded by construction: there are exactly five windows, so no eviction pass is needed here
        // the way there is for user playlists.
        => MutateAsync(document => document.TopStreamedSongs[window] = songs, cancellationToken);

    /// <summary>
    /// Clears the signed-out user's playlists while keeping the global "most streamed" ones.
    /// </summary>
    /// <remarks>
    /// This used to delete the whole file, which was right when every section of it was personal. The
    /// top-streamed playlists are not: they are the same for every visitor and are shown to signed-out
    /// ones, so wiping them on sign-out would blank the home page for exactly the user who has just
    /// lost their account context - including on the session-expiry sign-out that fires at start-up
    /// with no network to refill them.
    /// </remarks>
    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var existing = await ReadDocumentUnlockedAsync(cancellationToken).ConfigureAwait(false);

            var globalPlaylists = existing?.TopStreamedPlaylists ?? [];
            var globalSongs = existing?.TopStreamedSongs ?? [];

            if (globalPlaylists.Count == 0 && globalSongs.Count == 0)
            {
                await Task.Run(DeletePlaylistFile, cancellationToken).ConfigureAwait(false);
                return;
            }

            await WriteDocumentAsync(
                new OfflinePlaylistDocument
                {
                    Version = CurrentVersion,
                    UpdatedUtc = DateTimeOffset.UtcNow,
                    TopStreamedPlaylists = globalPlaylists,
                    TopStreamedSongs = globalSongs
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clear the offline playlist snapshot");
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <summary>
    /// Read-modify-write under a lock. Each save touches one section of a single document, so the
    /// whole document is rewritten rather than kept in memory between calls.
    /// </summary>
    private async Task MutateAsync(Action<OfflinePlaylistDocument> mutate, CancellationToken cancellationToken)
    {
        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var document = await ReadDocumentUnlockedAsync(cancellationToken).ConfigureAwait(false)
                ?? new OfflinePlaylistDocument();

            mutate(document);
            document.Version = CurrentVersion;
            document.UpdatedUtc = DateTimeOffset.UtcNow;

            await WriteDocumentAsync(document, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Snapshotting is best-effort and must never break the live fetch that triggered it.
            _logger.LogWarning(ex, "Failed to persist the offline playlist snapshot");
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private async Task<OfflinePlaylistDocument?> ReadDocumentAsync(CancellationToken cancellationToken)
    {
        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ReadDocumentUnlockedAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private async Task<OfflinePlaylistDocument?> ReadDocumentUnlockedAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await Task.Run(
                () =>
                {
                    if (!File.Exists(_playlistFilePath))
                    {
                        return null;
                    }

                    using var stream = File.OpenRead(_playlistFilePath);
                    return JsonSerializer.Deserialize<OfflinePlaylistDocument>(stream, PlaylistSerializerOptions);
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Offline playlist snapshot is corrupt. Clearing it.");
            DeletePlaylistFile();
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Failed to read the offline playlist snapshot");
            return null;
        }
    }

    private async Task WriteDocumentAsync(OfflinePlaylistDocument document, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_playlistFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = _playlistFilePath + ".tmp";
        try
        {
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, document, PlaylistSerializerOptions, cancellationToken)
                    .ConfigureAwait(false);
            }

            File.Move(temporaryPath, _playlistFilePath, overwrite: true);
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private void DeletePlaylistFile()
    {
        TryDelete(_playlistFilePath);
        TryDelete(_playlistFilePath + ".tmp");
    }

    private static void TryDelete(string path)
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
            // Harmless; the next write overwrites it.
        }
    }
}
