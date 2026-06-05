namespace MusicSalesApp.Maui.Services;

public interface IPlatformPlaybackRuntime
{
    event EventHandler<PlaybackRuntimeStateChangedEventArgs>? StateChanged;

    event EventHandler<PlaybackMediaItemEventArgs>? MediaItemChanged;

    event EventHandler<PlaybackPositionChangedEventArgs>? PositionChanged;

    event EventHandler<PlaybackMediaItemEventArgs>? MediaItemFinished;

    event EventHandler<PlaybackMediaItemFailedEventArgs>? MediaItemFailed;

    PlaybackRuntimeState State { get; }

    TimeSpan Position { get; }

    TimeSpan Duration { get; }

    IPlaybackRuntimeQueue? Queue { get; }

    PlaybackRepeatMode RepeatMode { get; set; }

    PlaybackShuffleMode ShuffleMode { get; set; }

    int AudioSessionId { get; }

    Task<PlaybackMediaItem?> PlayAsync(PlaybackMediaItem mediaItem);

    Task<PlaybackMediaItem?> PlayAsync(IEnumerable<PlaybackMediaItem> mediaItems);

    Task PlayAsync();

    Task PauseAsync();

    Task StopAsync();

    Task<bool> PlayNextAsync();

    Task<bool> PlayPreviousAsync();

    Task<bool> PlayQueueItemAsync(int index);

    Task SeekToAsync(TimeSpan position);
}

public interface IIndexedQueuePlaybackRuntime
{
    Task<PlaybackMediaItem?> PlayAsync(IEnumerable<PlaybackMediaItem> mediaItems, int startIndex);
}
