using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Services;

public enum QueuePreparationMode
{
    Normal,
    SleepSafe
}

public enum QueuePreparationFailureReason
{
    None,
    EmptyQueue,
    InvalidStartIndex,
    CurrentTrackUnavailable,
    DownloadFailed,
    NetworkUnavailable,
    Cancelled,
    Unknown
}

public enum CachePinScope
{
    TemporaryWarm,
    ActiveQueue,
    Offline
}

public sealed record TrackCacheStatus(
    int SongId,
    string StableCacheKey,
    string? LocalPlaybackUri,
    bool IsLocalReady,
    bool IsPinned);

public sealed record QueuePreparationResult(
    bool CurrentTrackReady,
    int ReadyThroughQueueIndex,
    TimeSpan ReadyThroughDuration,
    IReadOnlyList<PlaybackMediaItem> NotReadyItems,
    QueuePreparationMode Mode,
    QueuePreparationFailureReason? FailureReason)
{
    public static QueuePreparationResult Empty(QueuePreparationMode mode, QueuePreparationFailureReason reason) =>
        new(false, -1, TimeSpan.Zero, [], mode, reason);
}

public interface ITrackCacheService
{
    string GetStableCacheKey(SongDto song);

    Task<TrackCacheStatus> GetCacheStatusAsync(
        SongDto song,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<int, TrackCacheStatus>> GetCacheStatusesAsync(
        IReadOnlyList<SongDto> songs,
        CancellationToken cancellationToken = default);

    Task<TrackCacheStatus> EnsureCachedAsync(
        SongDto song,
        CachePinScope pinScope,
        CancellationToken cancellationToken = default);

    void PinActiveQueue(IReadOnlyList<SongDto> songs);
}

public interface IQueuePreparationService
{
    Task<QueuePreparationResult> PrepareAsync(
        IReadOnlyList<SongDto> queue,
        int startIndex,
        QueuePreparationMode mode,
        TimeSpan continuityWindow,
        CancellationToken cancellationToken = default);
}

