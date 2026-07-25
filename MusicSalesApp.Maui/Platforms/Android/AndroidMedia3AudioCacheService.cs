using Android.Content;
using AndroidX.Media3.Common;
using AndroidX.Media3.ExoPlayer.Offline;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;
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
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, CachePinScope> _pins = new(StringComparer.Ordinal);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _rejectedUndersizedDownloads = new(StringComparer.Ordinal);

    public AndroidMedia3AudioCacheService(
        ILogger<AndroidMedia3AudioCacheService> logger,
        IOfflineCacheSettingsService offlineCacheSettingsService)
    {
        _context = global::Android.App.Application.Context;
        _logger = logger;
        _offlineCacheSettingsService = offlineCacheSettingsService;
    }

    public string GetStableCacheKey(SongDto song) => AudioCacheKeyHelper.GetStableCacheKey(song);

    public async Task<TrackCacheStatus> GetCacheStatusAsync(
        SongDto song,
        CancellationToken cancellationToken = default)
    {
        var statuses = await GetCacheStatusesAsync([song], cancellationToken).ConfigureAwait(false);
        return statuses[song.Id];
    }

    public async Task<IReadOnlyDictionary<int, TrackCacheStatus>> GetCacheStatusesAsync(
        IReadOnlyList<SongDto> songs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(songs);

        var downloadManager = await AndroidMedia3CacheProvider
            .GetDownloadManagerAsync(_context)
            .ConfigureAwait(false);
        var downloadIndex = downloadManager.DownloadIndex;
        var cache = await AndroidMedia3CacheProvider.GetCacheAsync(_context).ConfigureAwait(false);

        return await Task.Run<IReadOnlyDictionary<int, TrackCacheStatus>>(
            () =>
            {
                var statuses = new Dictionary<int, TrackCacheStatus>(songs.Count);
                foreach (var song in songs)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var stableCacheKey = GetStableCacheKey(song);
                    Download? download = null;
                    try
                    {
                        download = downloadIndex?.GetDownload(stableCacheKey);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Unable to read Media3 download status. StableCacheKey={StableCacheKey}", stableCacheKey);
                    }

                    statuses[song.Id] = CreateCacheStatus(song, stableCacheKey, download, cache);
                }

                return statuses;
            },
            cancellationToken).ConfigureAwait(false);
    }

    private TrackCacheStatus CreateCacheStatus(
        SongDto song,
        string stableCacheKey,
        Download? download,
        AndroidX.Media3.DataSource.Cache.SimpleCache cache)
    {
        var downloadCompleted = download?.State == Download.StateCompleted;
        var bytesDownloaded = download?.BytesDownloaded ?? -1;

        // A "completed" download smaller than any real audio file is an error payload that was
        // stored as if it were the song (observed on-device: a 170-byte blob marked Completed).
        // Reporting it local-ready would make the queue treat unplayable bytes as sleep-safe.
        var completedUndersized = downloadCompleted &&
            bytesDownloaded >= 0 &&
            bytesDownloaded < AudioCacheKeyHelper.MinPlayableAudioBytes;
        if (completedUndersized && _rejectedUndersizedDownloads.TryAdd(stableCacheKey, 0))
        {
            _logger.LogWarning(
                "Media3 completed download is too small to be playable audio; removing it and treating the song as not cached. SongId={SongId}; StableCacheKey={StableCacheKey}; BytesDownloaded={BytesDownloaded}; MinPlayableAudioBytes={MinPlayableAudioBytes}",
                song.Id,
                stableCacheKey,
                bytesDownloaded,
                AudioCacheKeyHelper.MinPlayableAudioBytes);
            _ = RemovePoisonedUndersizedDownloadAsync(song.Id, stableCacheKey);
        }

        var isReady = downloadCompleted && !completedUndersized && IsCacheFullyLocal(stableCacheKey, download, cache);
        if (downloadCompleted && !completedUndersized && !isReady)
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
            return await GetCacheStatusAsync(song, cancellationToken).ConfigureAwait(false);
        }

        var status = await GetCacheStatusAsync(song, cancellationToken).ConfigureAwait(false);
        if (status.IsLocalReady)
        {
            _logger.LogDebug(
                "Media3 cache already ready. SongId={SongId}; StableCacheKey={StableCacheKey}; LocalPlaybackUri={LocalPlaybackUri}",
                song.Id,
                stableCacheKey,
                SanitizeMediaUri(status.LocalPlaybackUri));
            return status;
        }

        // A key rejected as undersized this session would just download the same junk bytes
        // again; retry at most once per app launch so a server-side fix heals on next start.
        if (_rejectedUndersizedDownloads.ContainsKey(stableCacheKey))
        {
            _logger.LogDebug(
                "Media3 cache ensure skipped because this download was rejected as undersized earlier in this session. SongId={SongId}; StableCacheKey={StableCacheKey}",
                song.Id,
                stableCacheKey);
            return status;
        }

        await RemoveStaleCompletedDownloadAsync(stableCacheKey, cancellationToken).ConfigureAwait(false);

        if (!await CanQueueDownloadAsync(song.Id, stableCacheKey, cancellationToken).ConfigureAwait(false))
        {
            return await GetCacheStatusAsync(song, cancellationToken).ConfigureAwait(false);
        }

        var request = BuildDownloadRequest(stableCacheKey, remoteUri);
        var downloadManager = await AndroidMedia3CacheProvider
            .GetDownloadManagerAsync(_context)
            .ConfigureAwait(false);
        _logger.LogInformation(
            "Media3 download queued. SongId={SongId}; StableCacheKey={StableCacheKey}; RemoteUri={RemoteUri}",
            song.Id,
            stableCacheKey,
            SanitizeMediaUri(remoteUri.ToString()));
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            downloadManager.AddDownload(request);
            downloadManager.ResumeDownloads();
        });

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

    public Task<long> GetCacheUsageBytesAsync(CancellationToken cancellationToken = default) =>
        AndroidMedia3CacheProvider.GetCacheSizeBytesAsync(_context, cancellationToken);

    public void RemoveUnpinnedPreparedContent(IEnumerable<string> stableCacheKeys)
    {
        foreach (var stableCacheKey in stableCacheKeys)
        {
            if (_pins.ContainsKey(stableCacheKey))
            {
                continue;
            }

            _ = RemoveDownloadObservingFaultsAsync(stableCacheKey);
        }
    }

    // Removes a poisoned undersized download and, only on failure, rolls back the session
    // rejection memo so a later status check retries removal instead of permanently stranding
    // the junk entry (never removed) while also refusing to re-download it.
    private async Task RemovePoisonedUndersizedDownloadAsync(int songId, string stableCacheKey)
    {
        try
        {
            await AndroidMedia3CacheProvider.RemoveDownloadAsync(_context, stableCacheKey).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _rejectedUndersizedDownloads.TryRemove(stableCacheKey, out _);
            _logger.LogWarning(
                ex,
                "Failed to remove poisoned undersized download; will retry on a later status check. SongId={SongId}; StableCacheKey={StableCacheKey}",
                songId,
                stableCacheKey);
        }
    }

    private async Task RemoveDownloadObservingFaultsAsync(string stableCacheKey)
    {
        try
        {
            await AndroidMedia3CacheProvider.RemoveDownloadAsync(_context, stableCacheKey).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove unpinned cached content. StableCacheKey={StableCacheKey}", stableCacheKey);
        }
    }

    private async Task<bool> CanQueueDownloadAsync(
        int songId,
        string stableCacheKey,
        CancellationToken cancellationToken)
    {
        var cacheSizeBytes = await AndroidMedia3CacheProvider
            .GetCacheSizeBytesAsync(_context, cancellationToken)
            .ConfigureAwait(false);
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

        var availableStorageBytes = await AndroidMedia3CacheProvider
            .GetAvailableCacheStorageBytesAsync(_context, cancellationToken)
            .ConfigureAwait(false);
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
            var download = await TryGetDownloadAsync(stableCacheKey, cancellationToken).ConfigureAwait(false);
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
                return await GetCacheStatusAsync(song, cancellationToken).ConfigureAwait(false);
            }

            if (download?.State == Download.StateFailed)
            {
                _logger.LogWarning(
                    "Media3 download failed for song {SongId}. StableCacheKey={StableCacheKey}; FailureReason={FailureReason}",
                    song.Id,
                    stableCacheKey,
                    download.FailureReason);
                return await GetCacheStatusAsync(song, cancellationToken).ConfigureAwait(false);
            }

            await Task.Delay(DownloadPollInterval, cancellationToken).ConfigureAwait(false);
        }

        return await GetCacheStatusAsync(song, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Download?> TryGetDownloadAsync(
        string stableCacheKey,
        CancellationToken cancellationToken)
    {
        try
        {
            var downloadManager = await AndroidMedia3CacheProvider
                .GetDownloadManagerAsync(_context)
                .ConfigureAwait(false);
            var downloadIndex = downloadManager.DownloadIndex;
            return await Task.Run(
                () => downloadIndex?.GetDownload(stableCacheKey),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Unable to read Media3 download status. StableCacheKey={StableCacheKey}", stableCacheKey);
            return null;
        }
    }

    private bool IsCacheFullyLocal(
        string stableCacheKey,
        Download? download,
        AndroidX.Media3.DataSource.Cache.SimpleCache cache)
    {
        try
        {
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
        var download = await TryGetDownloadAsync(stableCacheKey, cancellationToken).ConfigureAwait(false);
        var cache = await AndroidMedia3CacheProvider.GetCacheAsync(_context).ConfigureAwait(false);
        var isFullyLocal = await Task.Run(
            () => IsCacheFullyLocal(stableCacheKey, download, cache),
            cancellationToken).ConfigureAwait(false);
        if (download?.State != Download.StateCompleted || isFullyLocal)
        {
            return;
        }

        _logger.LogWarning(
            "Removing stale Media3 completed download because cache files are missing or incomplete. StableCacheKey={StableCacheKey}; BytesDownloaded={BytesDownloaded}; ContentLength={ContentLength}",
            stableCacheKey,
            download.BytesDownloaded,
            download.ContentLength);

        await AndroidMedia3CacheProvider
            .RemoveDownloadAsync(_context, stableCacheKey, cancellationToken)
            .ConfigureAwait(false);

        for (var attempt = 0; attempt < 20; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = await TryGetDownloadAsync(stableCacheKey, cancellationToken).ConfigureAwait(false);
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
