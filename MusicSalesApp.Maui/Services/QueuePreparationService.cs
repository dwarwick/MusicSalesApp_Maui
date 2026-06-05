using Microsoft.Extensions.Logging;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Services;

public sealed class QueuePreparationService : IQueuePreparationService
{
    // Non-positive sleep-safe windows mean the active queue must be prepared through the end.
    public static readonly TimeSpan FullQueueSleepSafeContinuityWindow = TimeSpan.Zero;
    public static readonly TimeSpan DefaultSleepSafeContinuityWindow = TimeSpan.FromMinutes(90);

    private readonly ITrackCacheService _trackCacheService;
    private readonly ILogger<QueuePreparationService> _logger;

    public QueuePreparationService(
        ITrackCacheService trackCacheService,
        ILogger<QueuePreparationService> logger)
    {
        _trackCacheService = trackCacheService;
        _logger = logger;
    }

    public async Task<QueuePreparationResult> PrepareAsync(
        IReadOnlyList<SongDto> queue,
        int startIndex,
        QueuePreparationMode mode,
        TimeSpan continuityWindow,
        CancellationToken cancellationToken = default)
    {
        if (queue.Count == 0)
        {
            return QueuePreparationResult.Empty(mode, QueuePreparationFailureReason.EmptyQueue);
        }

        if (startIndex < 0 || startIndex >= queue.Count)
        {
            return QueuePreparationResult.Empty(mode, QueuePreparationFailureReason.InvalidStartIndex);
        }

        _trackCacheService.PinActiveQueue(queue);

        var readyThroughIndex = startIndex - 1;
        var readyThroughDuration = TimeSpan.Zero;
        var notReadyItems = new List<PlaybackMediaItem>();
        var failureReason = QueuePreparationFailureReason.None;
        var requiredDuration = continuityWindow <= TimeSpan.Zero
            ? TimeSpan.MaxValue
            : continuityWindow;

        for (var index = startIndex; index < queue.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var song = queue[index];
            var status = _trackCacheService.GetCacheStatus(song);

            if (!status.IsLocalReady)
            {
                status = await TryEnsureCachedAsync(song, mode, cancellationToken).ConfigureAwait(false);
            }

            if (!status.IsLocalReady)
            {
                notReadyItems.Add(CreatePreparationMediaItem(song, status));
                failureReason = index == startIndex
                    ? QueuePreparationFailureReason.CurrentTrackUnavailable
                    : QueuePreparationFailureReason.DownloadFailed;

                if (mode == QueuePreparationMode.SleepSafe)
                {
                    break;
                }
            }
            else if (notReadyItems.Count == 0)
            {
                readyThroughIndex = index;
                readyThroughDuration += GetTrackDuration(song);
            }

            if (mode == QueuePreparationMode.Normal && index == startIndex)
            {
                break;
            }

            if (mode == QueuePreparationMode.SleepSafe &&
                readyThroughDuration >= requiredDuration &&
                index > startIndex)
            {
                break;
            }
        }

        var currentReady = readyThroughIndex >= startIndex;
        QueuePreparationFailureReason? normalizedFailureReason = failureReason == QueuePreparationFailureReason.None
            ? null
            : failureReason;

        _logger.LogInformation(
            "Queue preparation completed. Mode={Mode}; StartIndex={StartIndex}; QueueCount={QueueCount}; CurrentTrackReady={CurrentTrackReady}; ReadyThroughQueueIndex={ReadyThroughQueueIndex}; ReadyThroughDuration={ReadyThroughDuration}; NotReadyCount={NotReadyCount}; FailureReason={FailureReason}",
            mode,
            startIndex,
            queue.Count,
            currentReady,
            readyThroughIndex,
            readyThroughDuration,
            notReadyItems.Count,
            normalizedFailureReason);

        return new QueuePreparationResult(
            currentReady,
            readyThroughIndex,
            readyThroughDuration,
            notReadyItems,
            mode,
            normalizedFailureReason);
    }

    private async Task<TrackCacheStatus> TryEnsureCachedAsync(
        SongDto song,
        QueuePreparationMode mode,
        CancellationToken cancellationToken)
    {
        try
        {
            var pinScope = mode == QueuePreparationMode.SleepSafe
                ? CachePinScope.ActiveQueue
                : CachePinScope.TemporaryWarm;
            return await _trackCacheService.EnsureCachedAsync(song, pinScope, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Queue preparation failed to cache song {SongId}", song.Id);
            return _trackCacheService.GetCacheStatus(song);
        }
    }

    private PlaybackMediaItem CreatePreparationMediaItem(SongDto song, TrackCacheStatus status)
    {
        var mediaUri = status.LocalPlaybackUri;
        if (string.IsNullOrWhiteSpace(mediaUri))
        {
            mediaUri = song.StreamUrl ?? string.Empty;
        }

        return new PlaybackMediaItem(mediaUri, song.Id, status.StableCacheKey)
        {
            Title = song.SongTitle ?? string.Empty,
            Artist = song.ArtistName ?? string.Empty,
            IsLocal = status.IsLocalReady,
            IsSleepSafe = status.IsLocalReady
        };
    }

    private static TimeSpan GetTrackDuration(SongDto song)
    {
        if (song.TrackLengthSeconds is not { } seconds ||
            double.IsNaN(seconds) ||
            double.IsInfinity(seconds) ||
            seconds <= 0)
        {
            return TimeSpan.Zero;
        }

        return TimeSpan.FromSeconds(seconds);
    }
}
