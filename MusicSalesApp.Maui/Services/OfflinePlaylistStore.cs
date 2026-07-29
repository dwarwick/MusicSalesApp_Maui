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
}

/// <summary>
/// JSON-file implementation, alongside the song catalog in AppDataDirectory for the same reason: the
/// OS may purge CacheDirectory, and metadata that outlives its audio is harmless while audio that
/// outlives its metadata is unreachable.
/// </summary>
public sealed class OfflinePlaylistStore : IOfflinePlaylistStore
{
    internal const int CurrentVersion = 1;
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

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await Task.Run(DeletePlaylistFile, cancellationToken).ConfigureAwait(false);
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
