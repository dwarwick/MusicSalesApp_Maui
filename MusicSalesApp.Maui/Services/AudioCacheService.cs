using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Services;

public interface IAudioCacheService : ITrackCacheService
{
    string GetImmediatePlaybackUri(SongDto song);

    Task<string> ResolvePlaybackUriAsync(SongDto song, CancellationToken cancellationToken = default);
}

public class AudioCacheService : IAudioCacheService
{
    public const string AudioDownloadClientName = "AudioDownload";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AudioCacheService> _logger;
    private readonly IOfflineCacheSettingsService? _offlineCacheSettingsService;
    private readonly string _cacheDirectory;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _downloadLocks = new();
    private readonly ConcurrentDictionary<string, byte> _activeQueuePins = new();

    public AudioCacheService(
        IHttpClientFactory httpClientFactory,
        ILogger<AudioCacheService> logger,
        IOfflineCacheSettingsService offlineCacheSettingsService)
        : this(httpClientFactory, logger, Path.Combine(FileSystem.Current.CacheDirectory, "audio-playback"), offlineCacheSettingsService)
    {
    }

    public AudioCacheService(
        IHttpClientFactory httpClientFactory,
        ILogger<AudioCacheService> logger,
        string cacheDirectory)
        : this(httpClientFactory, logger, cacheDirectory, null)
    {
    }

    public AudioCacheService(
        IHttpClientFactory httpClientFactory,
        ILogger<AudioCacheService> logger,
        string cacheDirectory,
        IOfflineCacheSettingsService? offlineCacheSettingsService)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _cacheDirectory = cacheDirectory;
        _offlineCacheSettingsService = offlineCacheSettingsService;
    }

    public string GetImmediatePlaybackUri(SongDto song)
    {
        var status = GetCacheStatus(song);
        return status.IsLocalReady && !string.IsNullOrWhiteSpace(status.LocalPlaybackUri)
            ? status.LocalPlaybackUri
            : song.StreamUrl ?? string.Empty;
    }

    public string GetStableCacheKey(SongDto song)
    {
        if (!AudioCacheKeyHelper.TryGetRemoteUri(song, out var remoteUri))
        {
            return $"song-{song.Id}";
        }

        return AudioCacheKeyHelper.GetStableCacheKey(song);
    }

    public TrackCacheStatus GetCacheStatus(SongDto song)
    {
        if (!AudioCacheKeyHelper.TryGetRemoteUri(song, out var remoteUri))
        {
            return new TrackCacheStatus(song.Id, $"song-{song.Id}", null, false, false);
        }

        var cachePath = GetCachePath(song, remoteUri);
        var isLocalReady = HasCachedFile(cachePath);
        var stableCacheKey = AudioCacheKeyHelper.GetStableCacheKey(song);
        return new TrackCacheStatus(
            song.Id,
            stableCacheKey,
            isLocalReady ? cachePath : null,
            isLocalReady,
            _activeQueuePins.ContainsKey(stableCacheKey));
    }

    public async Task<TrackCacheStatus> EnsureCachedAsync(
        SongDto song,
        CachePinScope pinScope,
        CancellationToken cancellationToken = default)
    {
        if (pinScope is CachePinScope.ActiveQueue or CachePinScope.Offline)
        {
            _activeQueuePins[GetStableCacheKey(song)] = 0;
        }

        var playbackUri = await ResolvePlaybackUriAsync(song, cancellationToken).ConfigureAwait(false);
        var status = GetCacheStatus(song);
        if (!status.IsLocalReady && IsLocalPlaybackUri(playbackUri))
        {
            return status with { LocalPlaybackUri = playbackUri, IsLocalReady = true };
        }

        return status;
    }

    public void PinActiveQueue(IReadOnlyList<SongDto> songs)
    {
        _activeQueuePins.Clear();
        foreach (var song in songs)
        {
            _activeQueuePins[GetStableCacheKey(song)] = 0;
        }
    }

    public async Task<string> ResolvePlaybackUriAsync(SongDto song, CancellationToken cancellationToken = default)
    {
        if (!AudioCacheKeyHelper.TryGetRemoteUri(song, out var remoteUri))
        {
            return song.StreamUrl ?? string.Empty;
        }

        var cachePath = GetCachePath(song, remoteUri);
        if (HasCachedFile(cachePath))
        {
            return cachePath;
        }

        var downloadLock = _downloadLocks.GetOrAdd(cachePath, _ => new SemaphoreSlim(1, 1));
        await downloadLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (HasCachedFile(cachePath))
            {
                return cachePath;
            }

            var stableCacheKey = AudioCacheKeyHelper.GetStableCacheKey(song);
            if (!CanQueueDownload(song.Id, stableCacheKey, null))
            {
                return song.StreamUrl ?? string.Empty;
            }

            Directory.CreateDirectory(_cacheDirectory);

            var client = _httpClientFactory.CreateClient(AudioDownloadClientName);
            using var response = await client.GetAsync(remoteUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Audio cache download failed for song {SongId}. StatusCode={StatusCode}; StreamUrl={StreamUrl}",
                    song.Id,
                    response.StatusCode,
                    song.StreamUrl);
                return song.StreamUrl ?? string.Empty;
            }

            if (!CanQueueDownload(song.Id, stableCacheKey, response.Content.Headers.ContentLength))
            {
                return song.StreamUrl ?? string.Empty;
            }

            var tempPath = cachePath + ".tmp";
            DeleteFileIfPresent(tempPath);

            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var destination = File.Create(tempPath))
            {
                await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
                await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(tempPath, cachePath, true);

            _logger.LogInformation(
                "Audio cache download completed for song {SongId}. CachePath={CachePath}",
                song.Id,
                cachePath);

            return cachePath;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Audio cache download cancelled for song {SongId}", song.Id);
            return song.StreamUrl ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Audio cache download failed for song {SongId}", song.Id);
            return song.StreamUrl ?? string.Empty;
        }
        finally
        {
            DeleteFileIfPresent(cachePath + ".tmp");
            downloadLock.Release();
        }
    }

    private bool CanQueueDownload(int songId, string stableCacheKey, long? contentLengthBytes)
    {
        if (_offlineCacheSettingsService == null)
        {
            return true;
        }

        var cacheSizeBytes = GetCacheDirectorySizeBytes();
        var cacheLimitBytes = _offlineCacheSettingsService.GetOfflineCacheLimitBytes();
        var projectedCacheSizeBytes = contentLengthBytes is > 0
            ? cacheSizeBytes + contentLengthBytes.Value
            : cacheSizeBytes;

        if (cacheSizeBytes >= cacheLimitBytes || projectedCacheSizeBytes > cacheLimitBytes)
        {
            _logger.LogWarning(
                "Audio cache download skipped because configured offline cache limit is reached. SongId={SongId}; StableCacheKey={StableCacheKey}; CacheSizeBytes={CacheSizeBytes}; ContentLengthBytes={ContentLengthBytes}; CacheLimitBytes={CacheLimitBytes}",
                songId,
                stableCacheKey,
                cacheSizeBytes,
                contentLengthBytes,
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
                "Audio cache download skipped because device free-space reserve is reached. SongId={SongId}; StableCacheKey={StableCacheKey}; AvailableStorageBytes={AvailableStorageBytes}; ContentLengthBytes={ContentLengthBytes}; ReserveBytes={ReserveBytes}",
                songId,
                stableCacheKey,
                availableStorageBytes,
                contentLengthBytes,
                reserveBytes);
            return false;
        }

        return true;
    }

    private long GetCacheDirectorySizeBytes()
    {
        try
        {
            return Directory.Exists(_cacheDirectory)
                ? GetDirectorySizeBytes(new DirectoryInfo(_cacheDirectory))
                : 0;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Unable to measure audio cache directory size. CacheDirectory={CacheDirectory}", _cacheDirectory);
            return 0;
        }
    }

    private long GetAvailableCacheStorageBytes()
    {
        try
        {
            Directory.CreateDirectory(_cacheDirectory);
            var root = Path.GetPathRoot(_cacheDirectory);
            if (string.IsNullOrWhiteSpace(root))
            {
                return long.MaxValue;
            }

            return new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Unable to measure available cache storage. CacheDirectory={CacheDirectory}", _cacheDirectory);
            return long.MaxValue;
        }
    }

    private static long GetDirectorySizeBytes(DirectoryInfo directory)
    {
        var total = 0L;
        foreach (var file in directory.EnumerateFiles())
        {
            total += file.Length;
        }

        foreach (var childDirectory in directory.EnumerateDirectories())
        {
            total += GetDirectorySizeBytes(childDirectory);
        }

        return total;
    }

    private static void DeleteFileIfPresent(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static bool HasCachedFile(string cachePath)
    {
        return File.Exists(cachePath) && new FileInfo(cachePath).Length > 0;
    }

    private string GetCachePath(SongDto song, Uri remoteUri)
    {
        var extension = Path.GetExtension(Uri.UnescapeDataString(remoteUri.AbsolutePath));
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".mp3";
        }

        var hash = AudioCacheKeyHelper.GetStablePathHash(song, remoteUri);

        return Path.Combine(_cacheDirectory, $"{song.Id}-{hash}{extension}");
    }

    private static bool IsLocalPlaybackUri(string? mediaUri)
    {
        return !string.IsNullOrWhiteSpace(mediaUri) &&
            (Path.IsPathRooted(mediaUri) || mediaUri.StartsWith("file://", StringComparison.OrdinalIgnoreCase));
    }
}
