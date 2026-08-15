#if !ANDROID
using System.Collections;
using System.Runtime.CompilerServices;
using MediaManager;
using Microsoft.Extensions.Logging;
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
    private readonly NowPlayingArtworkCoordinator? _artworkCoordinator;
    // Reached from the position-timer thread, MediaItemChanged, and the PlayAsync continuation.
    // Without this, two threads could wrap the same queue item in two different targets and leave the
    // coordinator convinced the track kept changing.
    private readonly object _artworkSync = new();
    private readonly ConditionalWeakTable<IMediaItem, ArtworkMetadata> _artworkContentVersions = new();
    private MediaManagerArtworkTarget? _artworkTarget;

    /// <summary>
    /// How long after a transport-control press a Paused/Stopped state still counts as that press.
    /// Matches <c>AndroidMedia3PlaybackRuntime</c>'s window of the same name.
    /// </summary>
    internal static readonly TimeSpan UserTerminalStateReasonWindow = TimeSpan.FromSeconds(2);

    private readonly IPlaybackRemoteCommandBridge? _remoteCommandBridge;
    private readonly ILogger<MediaManagerPlaybackRuntime>? _logger;
    private readonly TimeProvider _timeProvider;
    private long _lastUserTerminalStateRequestUtcTicks;

    public MediaManagerPlaybackRuntime(
        IMediaManager mediaManager,
        NowPlayingArtworkCoordinator? artworkCoordinator = null,
        IPlaybackRemoteCommandBridge? remoteCommandBridge = null,
        ILogger<MediaManagerPlaybackRuntime>? logger = null)
        : this(mediaManager, artworkCoordinator, remoteCommandBridge, TimeProvider.System, logger)
    {
    }

    internal MediaManagerPlaybackRuntime(
        IMediaManager mediaManager,
        NowPlayingArtworkCoordinator? artworkCoordinator,
        IPlaybackRemoteCommandBridge? remoteCommandBridge,
        TimeProvider timeProvider,
        ILogger<MediaManagerPlaybackRuntime>? logger = null)
    {
        _mediaManager = mediaManager;
        _artworkCoordinator = artworkCoordinator;
        _remoteCommandBridge = remoteCommandBridge;
        _logger = logger;
        _timeProvider = timeProvider;

        if (_remoteCommandBridge is not null)
        {
            _remoteCommandBridge.UserTerminalCommandRequested += OnUserTerminalCommandRequested;
            _remoteCommandBridge.Start();
        }

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
        ObserveNowPlayingArtwork();
        return FromMediaManagerItem(playedItem);
    }

    public async Task<PlaybackMediaItem?> PlayAsync(IEnumerable<PlaybackMediaItem> mediaItems)
    {
        var playedItem = await _mediaManager.Play(mediaItems.Select(ToMediaManagerItem)).ConfigureAwait(false);
        ObserveNowPlayingArtwork();
        return FromMediaManagerItem(playedItem);
    }

    public Task PlayAsync() => _mediaManager.Play();

    public Task PauseAsync() => _mediaManager.Pause();

    public Task StopAsync() => _mediaManager.Stop();

    public Task<bool> PlayNextAsync() => _mediaManager.PlayNext();

    public Task<bool> PlayPreviousAsync() => _mediaManager.PlayPrevious();

    public Task<bool> PlayQueueItemAsync(int index) => _mediaManager.PlayQueueItem(index);

    public Task SeekToAsync(TimeSpan position) => _mediaManager.SeekTo(position);

    private void OnStateChanged(object? sender, StateChangedEventArgs e)
    {
        // The media library builds its notification manager lazily and resets the transport command
        // set when it does, so the policy has to be re-asserted rather than applied once at startup.
        try
        {
            _remoteCommandBridge?.RefreshTransportControls();
        }
        catch
        {
            // Transport-control cosmetics must never take playback down.
        }

        var state = MapState(e.State);
        StateChanged?.Invoke(this, new PlaybackRuntimeStateChangedEventArgs(state, ResolveStateChangeReason(state)));
    }

    private void OnUserTerminalCommandRequested(object? sender, EventArgs e) =>
        Interlocked.Exchange(ref _lastUserTerminalStateRequestUtcTicks, _timeProvider.GetUtcNow().UtcTicks);

    /// <summary>
    /// Classifies a terminal state as user-requested when the OS transport controls asked for it
    /// moments ago.
    ///
    /// <para>
    /// Without this, a lock-screen pause reaches <c>PlaybackService</c> as
    /// <c>Reason.Unknown</c> while <c>IsPlaying</c> is still true - because Plugin.MediaManager's
    /// remote-command handler calls <c>MediaManager.Pause()</c> directly and never tells
    /// <c>PlaybackService</c> - so it is read as an unexplained stall and "recovered" by restarting
    /// the queue a couple of seconds later. The in-app pause button never hit this, because
    /// <c>PlaybackService.Pause()</c> sets <c>IsPlaying = false</c> before the state change arrives.
    /// </para>
    ///
    /// <para>
    /// Only the transport controls stamp the timestamp - deliberately not this runtime's own
    /// <c>PauseAsync</c>/<c>StopAsync</c>, which the app also calls during failure recovery. Marking
    /// those as user requests would suppress the very recovery they are part of, which is why the
    /// Android runtime carries a matching <c>SuppressAppCommandUserReason</c> escape hatch.
    /// </para>
    ///
    /// <para>
    /// A window rather than a one-shot flag, because the pause arrives as several state changes:
    /// Plugin.MediaManager maps <c>AVPlayerStatus.ReadyToPlay</c> to Paused as well as
    /// <c>AVPlayerTimeControlStatus.Paused</c>, so Paused alone cannot be taken to mean a deliberate
    /// pause.
    /// </para>
    /// </summary>
    internal PlaybackRuntimeStateChangeReason ResolveStateChangeReason(PlaybackRuntimeState state)
    {
        if (state != PlaybackRuntimeState.Paused && state != PlaybackRuntimeState.Stopped)
        {
            return PlaybackRuntimeStateChangeReason.Unknown;
        }

        var requestedTicks = Volatile.Read(ref _lastUserTerminalStateRequestUtcTicks);
        if (requestedTicks <= 0)
        {
            return PlaybackRuntimeStateChangeReason.Unknown;
        }

        var elapsedTicks = _timeProvider.GetUtcNow().UtcTicks - requestedTicks;
        return elapsedTicks >= 0 && elapsedTicks <= UserTerminalStateReasonWindow.Ticks
            ? PlaybackRuntimeStateChangeReason.UserRequest
            : PlaybackRuntimeStateChangeReason.Unknown;
    }

    private void OnMediaItemChanged(object? sender, MediaItemEventArgs e)
    {
        ObserveNowPlayingArtwork();
        MediaItemChanged?.Invoke(this, new PlaybackMediaItemEventArgs(FromMediaManagerItem(e.MediaItem)));
    }

    private void OnPositionChanged(object? sender, MediaManager.Playback.PositionChangedEventArgs e)
    {
        // MediaManager's own ~1s notification heartbeat, already off the main thread. This is what
        // drives artwork retries, and the backstop if MediaItemChanged ever misses a transition.
        ObserveNowPlayingArtwork();
        PositionChanged?.Invoke(this, new PlaybackPositionChangedEventArgs(e.Position));
    }

    /// <summary>
    /// Points <see cref="NowPlayingArtworkCoordinator"/> at the item the OS is currently showing.
    ///
    /// <para>
    /// Plugin.MediaManager's Apple notification manager reads artwork from
    /// <c>IMediaItem.DisplayImage</c> - a decoded UIImage - and never from <c>ImageUri</c>. Nothing
    /// populates that property for the <c>Play(IMediaItem)</c> overloads this runtime uses: only the
    /// <c>Play(string)</c> and <c>Play(FileInfo)</c> overloads run the metadata extractor. This is the
    /// hook that fills it in, off the calling thread.
    /// </para>
    ///
    /// <para>
    /// Always <c>Queue.Current</c>, never the event's own MediaItem: <c>UpdateNotification()</c> reads
    /// <c>Queue.Current</c>, so an image set on any other instance would be invisible.
    /// </para>
    /// </summary>
    private void ObserveNowPlayingArtwork()
    {
        if (_artworkCoordinator is null)
        {
            return;
        }

        try
        {
            lock (_artworkSync)
            {
                var current = _mediaManager.Queue?.Current;
                if (current is null)
                {
                    // Queue.Current is ElementAtOrDefault(CurrentIndex), so it is momentarily null
                    // during a queue rebuild or a stop. Clearing the target here would make the very
                    // next tick wrap the same item in a fresh wrapper, which the coordinator reads as
                    // a track change - releasing the decoded image and re-fetching it for a track that
                    // was already showing artwork. Holding the target costs one image and self-corrects
                    // as soon as a genuinely different item appears.
                    return;
                }

                if (_artworkTarget is null || !ReferenceEquals(_artworkTarget.Item, current))
                {
                    _artworkTarget = new MediaManagerArtworkTarget(current, ResolveArtworkContentVersion(current));
                }

                _artworkCoordinator.Observe(_artworkTarget);
            }
        }
        catch (Exception ex)
        {
            // Artwork is decorative; it must never take playback down with it. Logged rather than
            // swallowed silently, because "no artwork and no log line" is precisely the diagnosis
            // dead-end the rolling-log allow-list exists to prevent.
            _logger?.LogWarning(ex, "Could not update now playing artwork for the current queue item.");
        }
    }

    private sealed class MediaManagerArtworkTarget(IMediaItem item, int artworkContentVersion)
        : INowPlayingArtworkTarget
    {
        public IMediaItem Item { get; } = item;

        public int ArtworkContentVersion { get; } = artworkContentVersion;

        public string ArtworkUri =>
            NowPlayingArtworkCoordinator.SelectArtworkUri(Item.AlbumImageUri, Item.ImageUri);

        /// <summary>
        /// DisplayImage, not Image. The notification manager reads <c>DisplayImage</c>, whose getter
        /// falls back <c>_displayImage ?? Image ?? AlbumImage</c> - so writing <c>Image</c> works only
        /// while nothing has ever assigned <c>DisplayImage</c> directly. Anything that does (the
        /// metadata extractor, run by the <c>Play(string)</c> overloads) would silently shadow every
        /// write, with no exception and no log. Writing the property that is actually read removes
        /// that trapdoor.
        /// </summary>
        public object? Image
        {
            get => Item.DisplayImage;
            set => Item.DisplayImage = value;
        }
    }

    private void OnMediaItemFinished(object? sender, MediaItemEventArgs e) =>
        MediaItemFinished?.Invoke(this, new PlaybackMediaItemEventArgs(FromMediaManagerItem(e.MediaItem)));

    private void OnMediaItemFailed(object? sender, MediaItemFailedEventArgs e) =>
        MediaItemFailed?.Invoke(
            this,
            new PlaybackMediaItemFailedEventArgs(
                FromMediaManagerItem(e.MediaItem),
                e.Exeption,
                e.Message));

    private IMediaItem ToMediaManagerItem(PlaybackMediaItem item)
    {
        var mediaItem = new MediaItem(item.MediaUri)
        {
            MediaLocation = item.IsLocal ? MediaLocation.FileSystem : MediaLocation.Remote,
            Title = item.Title,
            Artist = item.Artist,
            ImageUri = item.ImageUri,
            AlbumImageUri = item.AlbumImageUri,
        };

        // MediaItem has nowhere to put the artwork content version, and the loader cannot cache a
        // downloaded hero under the right key without it. A weak table keeps the association without
        // extending the media library's type or pinning items that leave the queue.
        if (item.AlbumImageContentVersion != 0)
        {
            _artworkContentVersions.Add(mediaItem, new ArtworkMetadata(item.AlbumImageContentVersion));
        }

        return mediaItem;
    }

    private int ResolveArtworkContentVersion(IMediaItem mediaItem) =>
        _artworkContentVersions.TryGetValue(mediaItem, out var metadata) ? metadata.ContentVersion : 0;

    private sealed record ArtworkMetadata(int ContentVersion);

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
