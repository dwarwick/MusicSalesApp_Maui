using Android.Content;
using AndroidX.Media3.Common;
using AndroidX.Media3.ExoPlayer.Offline;
using Microsoft.Extensions.Logging;
using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.ViewModels;
using AndroidUri = Android.Net.Uri;

namespace MusicSalesApp.Maui.Platforms.Android;

public sealed class AndroidMedia3AudioCacheService : IAudioCacheService
{
    private static readonly TimeSpan DownloadPollInterval = TimeSpan.FromMilliseconds(500);

    private readonly Context _context;
    private readonly ILogger<AndroidMedia3AudioCacheService> _logger;
    private readonly IOfflineCacheSettingsService _offlineCacheSettingsService;
    private readonly Dictionary<string, CachePinScope> _pins = new(StringComparer.Ordinal);

    public AndroidMedia3AudioCacheService(
        ILogger<AndroidMedia3AudioCacheService> logger,
        IOfflineCacheSettingsService offlineCacheSettingsService)
    {
        _context = global::Android.App.Application.Context;
        _logger = logger;
        _offlineCacheSettingsService = offlineCacheSettingsService;
        AndroidMedia3CacheProvider.EnsureNotificationChannels(_context);
    }

    public string GetImmediatePlaybackUri(SongDto song) => song.StreamUrl ?? string.Empty;

    public string GetStableCacheKey(SongDto song) => AudioCacheKeyHelper.GetStableCacheKey(song);

    public TrackCacheStatus GetCacheStatus(SongDto song)
    {
        var stableCacheKey = GetStableCacheKey(song);
        var download = TryGetDownload(stableCacheKey);
        var downloadCompleted = download?.State == Download.StateCompleted;
        var isReady = downloadCompleted && IsCacheFullyLocal(stableCacheKey, download);
        if (downloadCompleted && !isReady)
        {
            _logger.LogWarning(
                "Media3 download index reports completed content, but local cache spans are incomplete. SongId={SongId}; StableCacheKey={StableCacheKey}; BytesDownloaded={BytesDownloaded}; ContentLength={ContentLength}",
                song.Id,
                stableCacheKey,
                download?.BytesDownloaded,
                download?.ContentLength);
        }

        _logger.LogDebug(
            "Media3 cache status checked. SongId={SongId}; StableCacheKey={StableCacheKey}; State={State}; IsReady={IsReady}; IsPinned={IsPinned}; BytesDownloaded={BytesDownloaded}; ContentLength={ContentLength}",
            song.Id,
            stableCacheKey,
            DownloadStateName(download?.State),
            isReady,
            _pins.ContainsKey(stableCacheKey),
            download?.BytesDownloaded,
            download?.ContentLength);

        return new TrackCacheStatus(
            song.Id,
            stableCacheKey,
            isReady ? song.StreamUrl : null,
            isReady,
            _pins.ContainsKey(stableCacheKey));
    }

    public async Task<TrackCacheStatus> EnsureCachedAsync(
        SongDto song,
        CachePinScope pinScope,
        CancellationToken cancellationToken = default)
    {
        var stableCacheKey = GetStableCacheKey(song);
        _logger.LogDebug(
            "Media3 cache ensure requested. SongId={SongId}; StableCacheKey={StableCacheKey}; PinScope={PinScope}; StreamUri={StreamUri}",
            song.Id,
            stableCacheKey,
            pinScope,
            SanitizeMediaUri(song.StreamUrl));

        if (pinScope is CachePinScope.ActiveQueue or CachePinScope.Offline)
        {
            _pins[stableCacheKey] = pinScope;
        }

        if (!AudioCacheKeyHelper.TryGetRemoteUri(song, out var remoteUri))
        {
            _logger.LogWarning(
                "Media3 cache ensure skipped because song has no remote URI. SongId={SongId}; StableCacheKey={StableCacheKey}",
                song.Id,
                stableCacheKey);
            return GetCacheStatus(song);
        }

        var status = GetCacheStatus(song);
        if (status.IsLocalReady)
        {
            _logger.LogDebug(
                "Media3 cache already ready. SongId={SongId}; StableCacheKey={StableCacheKey}; LocalPlaybackUri={LocalPlaybackUri}",
                song.Id,
                stableCacheKey,
                SanitizeMediaUri(status.LocalPlaybackUri));
            return status;
        }

        await RemoveStaleCompletedDownloadAsync(stableCacheKey, cancellationToken).ConfigureAwait(false);

        if (!CanQueueDownload(song.Id, stableCacheKey))
        {
            return GetCacheStatus(song);
        }

        var request = BuildDownloadRequest(stableCacheKey, remoteUri);
        var downloadManager = AndroidMedia3CacheProvider.GetDownloadManager(_context);
        _logger.LogInformation(
            "Media3 download queued. SongId={SongId}; StableCacheKey={StableCacheKey}; RemoteUri={RemoteUri}",
            song.Id,
            stableCacheKey,
            SanitizeMediaUri(remoteUri.ToString()));
        downloadManager.AddDownload(request);
        downloadManager.ResumeDownloads();

        var finalStatus = await WaitForDownloadReadiness(song, stableCacheKey, cancellationToken).ConfigureAwait(false);
        _logger.LogDebug(
            "Media3 cache ensure completed. SongId={SongId}; StableCacheKey={StableCacheKey}; IsLocalReady={IsLocalReady}; LocalPlaybackUri={LocalPlaybackUri}",
            song.Id,
            stableCacheKey,
            finalStatus.IsLocalReady,
            SanitizeMediaUri(finalStatus.LocalPlaybackUri));
        return finalStatus;
    }

    public void PinActiveQueue(IReadOnlyList<SongDto> songs)
    {
        _logger.LogInformation("Media3 active queue pins updated. QueueCount={QueueCount}", songs.Count);
        var offlinePins = _pins
            .Where(pair => pair.Value == CachePinScope.Offline)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        _pins.Clear();
        foreach (var pin in offlinePins)
        {
            _pins[pin.Key] = pin.Value;
        }

        foreach (var song in songs)
        {
            _pins[GetStableCacheKey(song)] = CachePinScope.ActiveQueue;
        }
    }

    public async Task<string> ResolvePlaybackUriAsync(SongDto song, CancellationToken cancellationToken = default)
    {
        await EnsureCachedAsync(song, CachePinScope.TemporaryWarm, cancellationToken).ConfigureAwait(false);
        return song.StreamUrl ?? string.Empty;
    }

    public void RemoveUnpinnedPreparedContent(IEnumerable<string> stableCacheKeys)
    {
        foreach (var stableCacheKey in stableCacheKeys)
        {
            if (_pins.ContainsKey(stableCacheKey))
            {
                continue;
            }

            AndroidMedia3CacheProvider.RemoveDownload(_context, stableCacheKey);
        }
    }

    private bool CanQueueDownload(int songId, string stableCacheKey)
    {
        var cacheSizeBytes = AndroidMedia3CacheProvider.GetCacheSizeBytes(_context);
        var cacheLimitBytes = _offlineCacheSettingsService.GetOfflineCacheLimitBytes();
        if (cacheSizeBytes >= cacheLimitBytes)
        {
            _logger.LogWarning(
                "Media3 cache ensure skipped because configured offline cache limit is reached. SongId={SongId}; StableCacheKey={StableCacheKey}; CacheSizeBytes={CacheSizeBytes}; CacheLimitBytes={CacheLimitBytes}",
                songId,
                stableCacheKey,
                cacheSizeBytes,
                cacheLimitBytes);
            return false;
        }

        var availableStorageBytes = AndroidMedia3CacheProvider.GetAvailableCacheStorageBytes(_context);
        var reserveBytes = _offlineCacheSettingsService.GetDeviceFreeSpaceReserveBytes();
        if (availableStorageBytes <= reserveBytes)
        {
            _logger.LogWarning(
                "Media3 cache ensure skipped because device free-space reserve is reached. SongId={SongId}; StableCacheKey={StableCacheKey}; AvailableStorageBytes={AvailableStorageBytes}; ReserveBytes={ReserveBytes}",
                songId,
                stableCacheKey,
                availableStorageBytes,
                reserveBytes);
            return false;
        }

        return true;
    }

    private async Task<TrackCacheStatus> WaitForDownloadReadiness(
        SongDto song,
        string stableCacheKey,
        CancellationToken cancellationToken)
    {
        int? lastLoggedState = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            var download = TryGetDownload(stableCacheKey);
            if (download?.State != lastLoggedState)
            {
                lastLoggedState = download?.State;
                _logger.LogInformation(
                    "Media3 download state changed. SongId={SongId}; StableCacheKey={StableCacheKey}; State={State}; PercentDownloaded={PercentDownloaded}; BytesDownloaded={BytesDownloaded}",
                    song.Id,
                    stableCacheKey,
                    DownloadStateName(download?.State),
                    download?.PercentDownloaded,
                    download?.BytesDownloaded);
            }

            if (download?.State == Download.StateCompleted)
            {
                return GetCacheStatus(song);
            }

            if (download?.State == Download.StateFailed)
            {
                _logger.LogWarning(
                    "Media3 download failed for song {SongId}. StableCacheKey={StableCacheKey}; FailureReason={FailureReason}",
                    song.Id,
                    stableCacheKey,
                    download.FailureReason);
                return GetCacheStatus(song);
            }

            await Task.Delay(DownloadPollInterval, cancellationToken).ConfigureAwait(false);
        }

        return GetCacheStatus(song);
    }

    private Download? TryGetDownload(string stableCacheKey)
    {
        try
        {
            var downloadManager = AndroidMedia3CacheProvider.GetDownloadManager(_context);
            var downloadIndex = downloadManager.DownloadIndex;
            return downloadIndex?.GetDownload(stableCacheKey);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Unable to read Media3 download status. StableCacheKey={StableCacheKey}", stableCacheKey);
            return null;
        }
    }

    private bool IsCacheFullyLocal(string stableCacheKey, Download? download)
    {
        try
        {
            var cache = AndroidMedia3CacheProvider.GetCache(_context);
            var contentLength = download?.ContentLength ?? C.LengthUnset;

            return contentLength > 0
                && contentLength != C.LengthUnset
                && cache.IsCached(stableCacheKey, 0, contentLength);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Unable to verify Media3 cache spans. StableCacheKey={StableCacheKey}", stableCacheKey);
            return false;
        }
    }

    private async Task RemoveStaleCompletedDownloadAsync(string stableCacheKey, CancellationToken cancellationToken)
    {
        var download = TryGetDownload(stableCacheKey);
        if (download?.State != Download.StateCompleted || IsCacheFullyLocal(stableCacheKey, download))
        {
            return;
        }

        _logger.LogWarning(
            "Removing stale Media3 completed download because cache files are missing or incomplete. StableCacheKey={StableCacheKey}; BytesDownloaded={BytesDownloaded}; ContentLength={ContentLength}",
            stableCacheKey,
            download.BytesDownloaded,
            download.ContentLength);

        AndroidMedia3CacheProvider.RemoveDownload(_context, stableCacheKey);

        for (var attempt = 0; attempt < 20; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = TryGetDownload(stableCacheKey);
            if (current == null || current.State != Download.StateCompleted)
            {
                return;
            }

            await Task.Delay(DownloadPollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    private static DownloadRequest BuildDownloadRequest(string stableCacheKey, Uri remoteUri)
    {
        var androidUri = AndroidUri.Parse(remoteUri.ToString())
            ?? throw new InvalidOperationException("Android URI parsing returned null for a validated absolute media URI.");

        var builder = new DownloadRequest.Builder(stableCacheKey, androidUri);
        builder.SetCustomCacheKey(stableCacheKey);
        return builder.Build()
            ?? throw new InvalidOperationException("Media3 DownloadRequest.Builder returned null.");
    }

    private static string DownloadStateName(int? state) => state switch
    {
        null => "(missing)",
        Download.StateQueued => "Queued(0)",
        Download.StateStopped => "Stopped(1)",
        Download.StateDownloading => "Downloading(2)",
        Download.StateCompleted => "Completed(3)",
        Download.StateFailed => "Failed(4)",
        Download.StateRemoving => "Removing(5)",
        Download.StateRestarting => "Restarting(7)",
        _ => $"Unknown({state})"
    };

    private static string SanitizeMediaUri(string? mediaUri)
    {
        if (string.IsNullOrWhiteSpace(mediaUri))
        {
            return string.Empty;
        }

        if (Uri.TryCreate(mediaUri, UriKind.Absolute, out var uri))
        {
            return uri.GetLeftPart(UriPartial.Path);
        }

        return mediaUri.Length <= 180 ? mediaUri : mediaUri[..180];
    }
}
