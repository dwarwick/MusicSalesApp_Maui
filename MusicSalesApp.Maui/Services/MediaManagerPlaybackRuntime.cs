#if !ANDROID
using System.Collections;
using MediaManager;
using MediaManager.Library;
using MediaManager.Media;
using MediaManager.Playback;
using MediaManager.Player;
using MediaManager.Queue;
using MmRepeatMode = MediaManager.Playback.RepeatMode;
using MmShuffleMode = MediaManager.Queue.ShuffleMode;

namespace MusicSalesApp.Maui.Services;

public sealed class MediaManagerPlaybackRuntime : IPlatformPlaybackRuntime
{
    private readonly IMediaManager _mediaManager;

    public MediaManagerPlaybackRuntime(IMediaManager mediaManager)
    {
        _mediaManager = mediaManager;
        _mediaManager.StateChanged += OnStateChanged;
        _mediaManager.MediaItemChanged += OnMediaItemChanged;
        _mediaManager.PositionChanged += OnPositionChanged;
        _mediaManager.MediaItemFinished += OnMediaItemFinished;
        _mediaManager.MediaItemFailed += OnMediaItemFailed;
    }

    public event EventHandler<PlaybackRuntimeStateChangedEventArgs>? StateChanged;

    public event EventHandler<PlaybackMediaItemEventArgs>? MediaItemChanged;

    public event EventHandler<PlaybackPositionChangedEventArgs>? PositionChanged;

    public event EventHandler<PlaybackMediaItemEventArgs>? MediaItemFinished;

    public event EventHandler<PlaybackMediaItemFailedEventArgs>? MediaItemFailed;

    public PlaybackRuntimeState State => MapState(_mediaManager.State);

    public TimeSpan Position => _mediaManager.Position;

    public TimeSpan Duration => _mediaManager.Duration;

    public IPlaybackRuntimeQueue? Queue => _mediaManager.Queue == null
        ? null
        : new MediaManagerRuntimeQueue(_mediaManager.Queue);

    public PlaybackRepeatMode RepeatMode
    {
        get => _mediaManager.RepeatMode == MediaManager.Playback.RepeatMode.All
            ? PlaybackRepeatMode.All
            : PlaybackRepeatMode.Off;
        set => _mediaManager.RepeatMode = value == PlaybackRepeatMode.All
            ? MmRepeatMode.All
            : MmRepeatMode.Off;
    }

    public PlaybackShuffleMode ShuffleMode
    {
        get => _mediaManager.ShuffleMode == MmShuffleMode.All
            ? PlaybackShuffleMode.All
            : PlaybackShuffleMode.Off;
        set => _mediaManager.ShuffleMode = value == PlaybackShuffleMode.All
            ? MmShuffleMode.All
            : MmShuffleMode.Off;
    }

    public int AudioSessionId
    {
        get
        {
#if ANDROID
            return CrossMediaManager.Android?.Player?.AudioSessionId ?? 0;
#else
            return 0;
#endif
        }
    }

    public async Task<PlaybackMediaItem?> PlayAsync(PlaybackMediaItem mediaItem)
    {
        var playedItem = await _mediaManager.Play(ToMediaManagerItem(mediaItem)).ConfigureAwait(false);
        return FromMediaManagerItem(playedItem);
    }

    public async Task<PlaybackMediaItem?> PlayAsync(IEnumerable<PlaybackMediaItem> mediaItems)
    {
        var playedItem = await _mediaManager.Play(mediaItems.Select(ToMediaManagerItem)).ConfigureAwait(false);
        return FromMediaManagerItem(playedItem);
    }

    public Task PlayAsync() => _mediaManager.Play();

    public Task PauseAsync() => _mediaManager.Pause();

    public Task StopAsync() => _mediaManager.Stop();

    public Task<bool> PlayNextAsync() => _mediaManager.PlayNext();

    public Task<bool> PlayPreviousAsync() => _mediaManager.PlayPrevious();

    public Task<bool> PlayQueueItemAsync(int index) => _mediaManager.PlayQueueItem(index);

    public Task SeekToAsync(TimeSpan position) => _mediaManager.SeekTo(position);

    private void OnStateChanged(object? sender, StateChangedEventArgs e) =>
        StateChanged?.Invoke(this, new PlaybackRuntimeStateChangedEventArgs(MapState(e.State)));

    private void OnMediaItemChanged(object? sender, MediaItemEventArgs e) =>
        MediaItemChanged?.Invoke(this, new PlaybackMediaItemEventArgs(FromMediaManagerItem(e.MediaItem)));

    private void OnPositionChanged(object? sender, MediaManager.Playback.PositionChangedEventArgs e) =>
        PositionChanged?.Invoke(this, new PlaybackPositionChangedEventArgs(e.Position));

    private void OnMediaItemFinished(object? sender, MediaItemEventArgs e) =>
        MediaItemFinished?.Invoke(this, new PlaybackMediaItemEventArgs(FromMediaManagerItem(e.MediaItem)));

    private void OnMediaItemFailed(object? sender, MediaItemFailedEventArgs e) =>
        MediaItemFailed?.Invoke(
            this,
            new PlaybackMediaItemFailedEventArgs(
                FromMediaManagerItem(e.MediaItem),
                e.Exeption,
                e.Message));

    private static IMediaItem ToMediaManagerItem(PlaybackMediaItem item)
    {
        return new MediaItem(item.MediaUri)
        {
            MediaLocation = item.IsLocal ? MediaLocation.FileSystem : MediaLocation.Remote,
            Title = item.Title,
            Artist = item.Artist,
            ImageUri = item.ImageUri,
            AlbumImageUri = item.AlbumImageUri,
        };
    }

    private static PlaybackMediaItem? FromMediaManagerItem(IMediaItem? item)
    {
        if (item == null)
        {
            return null;
        }

        return new PlaybackMediaItem(item.MediaUri ?? string.Empty, 0, item.MediaUri ?? string.Empty)
        {
            Title = item.Title ?? string.Empty,
            Artist = item.Artist ?? string.Empty,
            ImageUri = item.ImageUri ?? string.Empty,
            AlbumImageUri = item.AlbumImageUri ?? string.Empty,
            IsLocal = IsLocalPlaybackUri(item.MediaUri),
            IsSleepSafe = IsLocalPlaybackUri(item.MediaUri)
        };
    }

    private static PlaybackRuntimeState MapState(MediaPlayerState state) => state switch
    {
        MediaPlayerState.Playing => PlaybackRuntimeState.Playing,
        MediaPlayerState.Paused => PlaybackRuntimeState.Paused,
        MediaPlayerState.Buffering => PlaybackRuntimeState.Buffering,
        MediaPlayerState.Failed => PlaybackRuntimeState.Failed,
        _ => PlaybackRuntimeState.Stopped
    };

    private static bool IsLocalPlaybackUri(string? mediaUri) =>
        !string.IsNullOrWhiteSpace(mediaUri) &&
        (Path.IsPathRooted(mediaUri) || mediaUri.StartsWith("file://", StringComparison.OrdinalIgnoreCase));

    private sealed class MediaManagerRuntimeQueue(IMediaQueue queue) : IPlaybackRuntimeQueue
    {
        public bool HasCurrent => queue.HasCurrent;

        public int CurrentIndex => queue.CurrentIndex;

        public PlaybackMediaItem? Current => FromMediaManagerItem(queue.Current);

        public PlaybackMediaItem? Next => FromObjectMediaItem(queue, "Next");

        public PlaybackMediaItem? Previous => FromObjectMediaItem(queue, "Previous");

        public int Count => ResolveCount(queue);

        public IEnumerator<PlaybackMediaItem> GetEnumerator()
        {
            if (queue is IEnumerable enumerable)
            {
                foreach (var entry in enumerable)
                {
                    if (entry is IMediaItem mediaItem && FromMediaManagerItem(mediaItem) is { } item)
                    {
                        yield return item;
                    }
                }
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private static PlaybackMediaItem? FromObjectMediaItem(object source, string propertyName)
        {
            try
            {
                return source.GetType().GetProperty(propertyName)?.GetValue(source) is IMediaItem mediaItem
                    ? FromMediaManagerItem(mediaItem)
                    : null;
            }
            catch
            {
                return null;
            }
        }

        private static int ResolveCount(object source)
        {
            try
            {
                return source.GetType().GetProperty("Count")?.GetValue(source) is int count
                    ? count
                    : 0;
            }
            catch
            {
                return 0;
            }
        }
    }
}
#endif
