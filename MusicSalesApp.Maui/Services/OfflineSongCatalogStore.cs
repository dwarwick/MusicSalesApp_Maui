using System.Text.Json;
using Microsoft.Extensions.Logging;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Services;

/// <summary>
/// Durable snapshot of the song catalog, written after every successful live load so the app still
/// knows what music exists when the API is unreachable.
///
/// Without this the cached MP3s are orphaned: every cache key is derived from a <see cref="SongDto"/>,
/// so with no song list there is no way to even enumerate what has been downloaded.
/// </summary>
public interface IOfflineSongCatalogStore
{
    Task<IReadOnlyList<SongDto>> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(IReadOnlyList<SongDto> songs, CancellationToken cancellationToken = default);

    Task<DateTimeOffset?> GetLastUpdatedUtcAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Strips the outgoing user's thumbs-up/down state from the snapshot, keeping the catalog itself.
    ///
    /// Called on logout. The catalog is not namespaced by account, so leaving the opinions in place
    /// would show one user's votes to whoever signs in next while offline - but deleting the whole file
    /// would also take away offline playback, including for the session-expiry logout that can happen at
    /// startup with no network. The songs are public; only the opinion is personal.
    /// </summary>
    Task ClearUserLikeStatesAsync(CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);
}

public sealed class OfflineSongCatalogDocument
{
    public int Version { get; set; } = OfflineSongCatalogStore.CurrentVersion;

    public DateTimeOffset UpdatedUtc { get; set; }

    public List<OfflineSongCatalogEntry> Songs { get; set; } = [];
}

public sealed class OfflineSongCatalogEntry
{
    /// <summary>
    /// The song exactly as the API returned it. Stored verbatim rather than mapped into a slimmer
    /// record so a restored list is indistinguishable from a live one, and cannot drift as SongDto grows.
    /// </summary>
    public SongDto Song { get; set; } = new();

    /// <summary>
    /// Audio cache key at the time of writing. Diagnostics only - readiness is always re-resolved from
    /// the live cache on read, never trusted from this file.
    /// </summary>
    public string StableCacheKey { get; set; } = string.Empty;
}

/// <summary>
/// JSON-file implementation of <see cref="IOfflineSongCatalogStore"/>.
///
/// Deliberately a file rather than <see cref="IAppPreferenceStore"/>: Preferences is Android
/// SharedPreferences XML / iOS NSUserDefaults, parsed wholesale into memory and rewritten on every
/// commit, and its API is synchronous-only. A few hundred songs carrying 400-character SAS URLs is
/// hundreds of kilobytes - far outside what a scalar key/value store should hold.
///
/// Deliberately in AppDataDirectory rather than CacheDirectory: the OS may purge the cache directory
/// under storage pressure. Losing the audio but keeping the metadata is self-healing (reads intersect
/// with live cache status, so the offline library just shrinks); losing the metadata but keeping the
/// audio is unrecoverable, because nothing else can name those files.
/// </summary>
public sealed class OfflineSongCatalogStore : IOfflineSongCatalogStore
{
    internal const int CurrentVersion = 1;
    internal const int MaxCatalogEntries = 5000;
    private const string CatalogFileName = "song-catalog-v1.json";

    private static readonly JsonSerializerOptions CatalogSerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly string _catalogFilePath;
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly ILogger<OfflineSongCatalogStore> _logger;

    public OfflineSongCatalogStore(ILogger<OfflineSongCatalogStore> logger)
        : this(Path.Combine(FileSystem.AppDataDirectory, "offline"), logger)
    {
    }

    public OfflineSongCatalogStore(string catalogDirectory, ILogger<OfflineSongCatalogStore> logger)
    {
        _catalogFilePath = Path.Combine(catalogDirectory, CatalogFileName);
        _logger = logger;
    }

    public async Task<IReadOnlyList<SongDto>> LoadAsync(CancellationToken cancellationToken = default)
    {
        var document = await ReadDocumentAsync(cancellationToken).ConfigureAwait(false);
        return document is null
            ? []
            : document.Songs.Select(entry => entry.Song).Where(song => song is not null).ToList();
    }

    public async Task SaveAsync(IReadOnlyList<SongDto> songs, CancellationToken cancellationToken = default)
    {
        // An empty list is never persisted. A transient failure that surfaced as "no songs" must not be
        // able to wipe a good snapshot; callers are responsible for only saving successful live results.
        if (songs is null || songs.Count == 0)
        {
            return;
        }

        var retained = songs;
        if (retained.Count > MaxCatalogEntries)
        {
            _logger.LogWarning(
                "Offline song catalog holds {SongCount} songs, truncating to the {MaxCatalogEntries} entry limit",
                retained.Count, MaxCatalogEntries);
            retained = retained.Take(MaxCatalogEntries).ToList();
        }

        var document = new OfflineSongCatalogDocument
        {
            Version = CurrentVersion,
            UpdatedUtc = DateTimeOffset.UtcNow,
            Songs = retained
                .Select(song => new OfflineSongCatalogEntry
                {
                    Song = song,
                    StableCacheKey = AudioCacheKeyHelper.GetStableCacheKey(song)
                })
                .ToList()
        };

        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await WriteDocumentAsync(document, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Persisting the catalog is best-effort. A failure here must never break the live load that
            // triggered it, so it is logged and swallowed rather than propagated.
            _logger.LogWarning(ex, "Failed to persist the offline song catalog");
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task<DateTimeOffset?> GetLastUpdatedUtcAsync(CancellationToken cancellationToken = default)
    {
        var document = await ReadDocumentAsync(cancellationToken).ConfigureAwait(false);
        return document?.UpdatedUtc;
    }

    public async Task ClearUserLikeStatesAsync(CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var document = await ReadDocumentAsync(cancellationToken).ConfigureAwait(false);
            if (document is null || document.Songs.Count == 0)
            {
                return;
            }

            foreach (var entry in document.Songs.Where(entry => entry.Song is not null))
            {
                entry.Song.UserLikeStatus = null;
            }

            document.UpdatedUtc = DateTimeOffset.UtcNow;
            await WriteDocumentAsync(document, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clear the user like states from the offline song catalog");
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await Task.Run(DeleteCatalogFile, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private async Task<OfflineSongCatalogDocument?> ReadDocumentAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await Task.Run(
                () =>
                {
                    if (!File.Exists(_catalogFilePath))
                    {
                        return null;
                    }

                    using var stream = File.OpenRead(_catalogFilePath);
                    return JsonSerializer.Deserialize<OfflineSongCatalogDocument>(stream, CatalogSerializerOptions);
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Offline song catalog is corrupt. Clearing it.");
            DeleteCatalogFile();
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Failed to read the offline song catalog");
            return null;
        }
    }

    private async Task WriteDocumentAsync(OfflineSongCatalogDocument document, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_catalogFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Write-then-move so a process kill mid-write cannot leave a half-written catalog behind - the
        // same pattern the audio cache uses for downloads.
        var temporaryPath = _catalogFilePath + ".tmp";
        try
        {
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, document, CatalogSerializerOptions, cancellationToken)
                    .ConfigureAwait(false);
            }

            File.Move(temporaryPath, _catalogFilePath, overwrite: true);
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private void DeleteCatalogFile()
    {
        TryDelete(_catalogFilePath);
        TryDelete(_catalogFilePath + ".tmp");
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
            // Nothing useful to do - a leftover file is harmless and will be overwritten next save.
        }
    }
}
