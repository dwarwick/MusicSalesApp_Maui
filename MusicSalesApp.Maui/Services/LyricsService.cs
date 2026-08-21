#nullable enable
using System.Text;
using Microsoft.Extensions.Logging;
using MusicSalesApp.Common.Contracts;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Services;

/// <summary>Where a song's timings came from, for callers that ration their retries.</summary>
public enum LyricsCacheResult
{
    /// <summary>Read from disk. No network was touched.</summary>
    Cached,

    /// <summary>Downloaded and written to disk.</summary>
    Downloaded,

    /// <summary>This song has no lyrics a listener may see. Not an error, and not worth retrying.</summary>
    None,

    /// <summary>Nothing was reachable. Worth trying again later.</summary>
    Offline,

    /// <summary>The fetch or the parse failed. Not worth retrying on a loop.</summary>
    Failed
}

/// <summary>The outcome of a caching attempt, and the document if there is one.</summary>
public sealed record LyricsOutcome(LyricsCacheResult Result, LyricsTimingsDocument? Document)
{
    /// <summary>Whether a later attempt could plausibly do better.</summary>
    public bool IsWorthRetrying => Result == LyricsCacheResult.Offline;
}

/// <summary>
/// Fetches and caches the word-level lyric timings for a song.
/// </summary>
public interface ILyricsService
{
    /// <summary>
    /// The timings for a song, from disk if they are there and from the network if not.
    /// Returns null whenever there is nothing to show, which includes every failure.
    /// </summary>
    Task<LyricsTimingsDocument?> GetTimingsAsync(SongDto? song, CancellationToken cancellationToken = default);

    /// <summary>
    /// Put a song's timings on disk if they are not already there, reporting why if not.
    /// </summary>
    Task<LyricsOutcome> EnsureCachedAsync(SongDto? song, CancellationToken cancellationToken = default);

    /// <summary>Delete cached timings not named by <paramref name="retained"/>.</summary>
    Task PruneAsync(IReadOnlyCollection<SongDto> retained, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
/// <remarks>
/// <para>
/// <b>The presence of a path is the permission.</b> The server resolves it and sends null for
/// anything unpublished, so there is no status to re-check here - and deliberately so: withheld
/// timings sit at exactly the blob path a published song would use, so a client that received a
/// path plus a status could not be trusted to gate on it correctly.
/// </para>
/// <para>
/// Nothing here throws. A song without lyrics, an unreachable server and a corrupt blob all
/// arrive at the same place - no lyrics - because none of them is a reason to break a player.
/// </para>
/// </remarks>
public class LyricsService : ILyricsService
{
    /// <summary>Named to match the folder the audio and image caches use.</summary>
    private const string CacheFolderName = "lyrics-cache";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConnectivity _connectivity;
    private readonly ILogger<LyricsService> _logger;
    private readonly string _cacheDirectory;

    public LyricsService(
        IHttpClientFactory httpClientFactory,
        IConnectivity connectivity,
        ILogger<LyricsService> logger,
        string? cacheDirectory = null)
    {
        _httpClientFactory = httpClientFactory;
        _connectivity = connectivity;
        _logger = logger;
        _cacheDirectory = cacheDirectory ?? Path.Combine(FileSystem.Current.CacheDirectory, CacheFolderName);
    }

    /// <inheritdoc />
    public async Task<LyricsTimingsDocument?> GetTimingsAsync(
        SongDto? song, CancellationToken cancellationToken = default)
    {
        var outcome = await EnsureCachedAsync(song, cancellationToken).ConfigureAwait(false);
        return outcome.Document;
    }

    /// <inheritdoc />
    public async Task<LyricsOutcome> EnsureCachedAsync(
        SongDto? song, CancellationToken cancellationToken = default)
    {
        if (song is null || string.IsNullOrWhiteSpace(song.LyricsTimingsPath))
        {
            return new LyricsOutcome(LyricsCacheResult.None, null);
        }

        var cachePath = GetCachePath(song);

        try
        {
            if (File.Exists(cachePath))
            {
                var cached = await ParseAsync(
                    await File.ReadAllTextAsync(cachePath, cancellationToken).ConfigureAwait(false))
                    .ConfigureAwait(false);

                if (cached is not null)
                {
                    song.CachedLyricsPath = cachePath;
                    return new LyricsOutcome(LyricsCacheResult.Cached, cached);
                }

                // On disk but unreadable. Drop it so the next attempt re-downloads rather than
                // failing forever against a corrupt file.
                TryDelete(cachePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read cached lyrics for song {SongId}.", song.Id);
        }

        if (_connectivity.NetworkAccess != NetworkAccess.Internet)
        {
            return new LyricsOutcome(LyricsCacheResult.Offline, null);
        }

        try
        {
            var client = _httpClientFactory.CreateClient("MusicSalesApi");

            // The version is not decoration. The blob path never changes between re-publishes and
            // the response carries a year-long immutable cache header, so without it a creator's
            // corrected timings would never reach a client that had already fetched the old ones.
            var requestUri = $"api/music/{song.LyricsTimingsPath}?v={song.LyricsVersion}";

            using var response = await client.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                // A 404 is the ordinary answer for timings that were withdrawn between the song
                // list being cached and now. Nothing is wrong; there are simply no lyrics.
                _logger.LogInformation(
                    "No lyrics served for song {SongId} ({Status}).", song.Id, (int)response.StatusCode);
                return new LyricsOutcome(LyricsCacheResult.None, null);
            }

            // Read as text rather than through GetFromJsonAsync: this route serves blobs as
            // application/octet-stream, and the JSON helpers refuse that media type outright.
            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            var document = await ParseAsync(json).ConfigureAwait(false);
            if (document is null)
            {
                return new LyricsOutcome(LyricsCacheResult.Failed, null);
            }

            await WriteCacheAsync(cachePath, json, cancellationToken).ConfigureAwait(false);
            song.CachedLyricsPath = cachePath;

            return new LyricsOutcome(LyricsCacheResult.Downloaded, document);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not fetch lyrics for song {SongId}.", song.Id);
            return new LyricsOutcome(LyricsCacheResult.Failed, null);
        }
    }

    /// <inheritdoc />
    public Task PruneAsync(IReadOnlyCollection<SongDto> retained, CancellationToken cancellationToken = default)
        => Task.Run(() =>
        {
            try
            {
                if (!Directory.Exists(_cacheDirectory))
                {
                    return;
                }

                // Keyed on path AND version, so the superseded file left by an earlier publish is
                // swept rather than accumulating beside the current one forever.
                var keep = retained
                    .Where(s => !string.IsNullOrWhiteSpace(s.LyricsTimingsPath))
                    .Select(GetCachePath)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var file in Directory.EnumerateFiles(_cacheDirectory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!keep.Contains(file))
                    {
                        TryDelete(file);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not prune the lyrics cache.");
            }
        }, cancellationToken);

    /// <summary>
    /// Parse off the calling thread.
    /// </summary>
    /// <remarks>
    /// A four-minute song can carry a few thousand words, and this runs while a player page is
    /// being built. <c>Deserialize</c> answers null rather than throwing on a corrupt blob, which
    /// is the right answer to "can this song show lyrics".
    /// </remarks>
    private static Task<LyricsTimingsDocument?> ParseAsync(string json) =>
        Task.Run(() => LyricsTimingsSerializer.Deserialize(json));

    /// <summary>
    /// The cache file for a song, named from the blob path and version rather than the URL.
    /// </summary>
    /// <remarks>
    /// The server regenerates the SAS query string on every call, so a key derived from a whole
    /// URL would miss every time. This mirrors what <c>StableRemoteAssetKey</c> does for audio
    /// and artwork, using the blob path directly since that is already stable.
    /// </remarks>
    private string GetCachePath(SongDto song)
    {
        var seed = $"{song.LyricsTimingsPath!.ToLowerInvariant()}|v{song.LyricsVersion}";
        var hash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(seed)))[..32];

        return Path.Combine(_cacheDirectory, $"lyrics-{song.Id}-{hash}.json");
    }

    private async Task WriteCacheAsync(string path, string json, CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(_cacheDirectory);
            await File.WriteAllTextAsync(path, json, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Failing to cache costs offline availability, not this playback. The document was
            // already parsed and is on its way to the caller.
            _logger.LogWarning(ex, "Could not write lyrics to the cache.");
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not delete cached lyrics {Path}.", path);
        }
    }
}
