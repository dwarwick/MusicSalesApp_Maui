namespace MusicSalesApp.Maui.Services;

public enum PlaybackRuntimeState
{
    Stopped,
    Playing,
    Paused,
    Buffering,
    Failed
}

public enum PlaybackRepeatMode
{
    Off,
    All
}

public enum PlaybackShuffleMode
{
    Off,
    All
}

public enum PlaybackPreparationState
{
    None,
    Preparing,
    Ready,
    WaitingForNetwork,
    Error
}

public enum PlaybackMediaLocation
{
    Remote,
    FileSystem
}

public sealed record PlaybackMediaItem(
    string MediaUri,
    int SongId,
    string StableCacheKey)
{
    public PlaybackMediaItem(string mediaUri)
        : this(mediaUri, 0, mediaUri)
    {
    }

    public string Title { get; init; } = string.Empty;

    public string Artist { get; init; } = string.Empty;

    public string ImageUri { get; init; } = string.Empty;

    public string AlbumImageUri { get; init; } = string.Empty;

    public bool IsLocal { get; init; }

    public bool IsSleepSafe { get; init; }

    public PlaybackMediaLocation MediaLocation => IsLocal
        ? PlaybackMediaLocation.FileSystem
        : PlaybackMediaLocation.Remote;
}

public sealed class PlaybackRuntimeStateChangedEventArgs(PlaybackRuntimeState state) : EventArgs
{
    public PlaybackRuntimeState State { get; } = state;
}

public sealed class PlaybackMediaItemEventArgs(PlaybackMediaItem? mediaItem) : EventArgs
{
    public PlaybackMediaItem? MediaItem { get; } = mediaItem;
}

public sealed class PlaybackMediaItemFailedEventArgs(
    PlaybackMediaItem? mediaItem,
    Exception exception,
    string? message = null) : EventArgs
{
    public PlaybackMediaItem? MediaItem { get; } = mediaItem;

    public Exception Exception { get; } = exception;

    public string Message { get; } = string.IsNullOrWhiteSpace(message)
        ? exception.Message
        : message;
}

public sealed class PlaybackPositionChangedEventArgs(TimeSpan position) : EventArgs
{
    public TimeSpan Position { get; } = position;
}

public interface IPlaybackRuntimeQueue : IEnumerable<PlaybackMediaItem>
{
    bool HasCurrent { get; }

    int CurrentIndex { get; }

    PlaybackMediaItem? Current { get; }

    PlaybackMediaItem? Next { get; }

    PlaybackMediaItem? Previous { get; }

    int Count { get; }
}
