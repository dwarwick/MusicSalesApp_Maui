namespace MusicSalesApp.Maui.Services;

public enum PlaybackRuntimeState
{
    Stopped,
    Playing,
    Paused,
    Buffering,
    Failed
}

public enum PlaybackRuntimeStateChangeReason
{
    Unknown,
    UserRequest
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

public enum PlaybackQueueStartBehavior
{
    RestartAtRequestedIndex,
    PreserveCurrentSongIfPresent
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

    /// <summary>
    /// The artwork Android's Media3 session is given, preferring the 320px thumb because Media3
    /// decodes this URI on the media thread for a notification icon a couple of hundred pixels wide.
    /// </summary>
    public string ImageUri { get; init; } = string.Empty;

    /// <summary>
    /// The artwork for surfaces that render it large - Apple's lock screen and Control Center -
    /// preferring the 640px hero rendition, where <see cref="ImageUri"/> prefers the thumb. Android
    /// never reads this.
    /// </summary>
    public string AlbumImageUri { get; init; } = string.Empty;

    /// <summary>
    /// Content version of the rendition <see cref="AlbumImageUri"/> came from, so a remote hero can be
    /// written to the image cache under the right key. <c>StableRemoteAssetKey</c> hashes the blob path
    /// plus this version - caching under version 0 would both duplicate the file and keep serving
    /// pre-crop artwork after a re-crop, because a re-crop overwrites the same blob path in place.
    /// </summary>
    public int AlbumImageContentVersion { get; init; }

    public bool IsLocal { get; init; }

    public bool IsSleepSafe { get; init; }

    public PlaybackMediaLocation MediaLocation => IsLocal
        ? PlaybackMediaLocation.FileSystem
        : PlaybackMediaLocation.Remote;
}

public sealed class PlaybackRuntimeStateChangedEventArgs(
    PlaybackRuntimeState state,
    PlaybackRuntimeStateChangeReason reason = PlaybackRuntimeStateChangeReason.Unknown) : EventArgs
{
    public PlaybackRuntimeState State { get; } = state;

    public PlaybackRuntimeStateChangeReason Reason { get; } = reason;

    public bool IsUserRequest => Reason == PlaybackRuntimeStateChangeReason.UserRequest;
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
