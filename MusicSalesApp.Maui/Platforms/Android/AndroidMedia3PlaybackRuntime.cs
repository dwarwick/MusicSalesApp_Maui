using System.Collections;
using Android.Content;
using AndroidX.Media3.Common;
using AndroidX.Media3.ExoPlayer;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;
using MusicSalesApp.Maui.Services;
using CommonMediaItem = AndroidX.Media3.Common.MediaItem;
using MauiMediaItem = MusicSalesApp.Maui.Services.PlaybackMediaItem;

namespace MusicSalesApp.Maui.Platforms.Android;

public sealed class AndroidMedia3PlaybackRuntime : IPlatformPlaybackRuntime, IIndexedQueuePlaybackRuntime, IQueueReplacementPlaybackRuntime, IDisposable
{
    private const int PlayerStateIdle = 1;
    private const int PlayerStateBuffering = 2;
    private const int PlayerStateReady = 3;
    private const int PlayerStateEnded = 4;
    private const int RepeatModeOff = 0;
    private const int RepeatModeAll = 2;
    private const int PlayWhenReadyChangeReasonUserRequest = 1;
    private const int PlayWhenReadyChangeReasonRemote = 4;
    private static readonly TimeSpan UserTerminalStateReasonWindow = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan AppCommandUserReasonSuppressionWindow = TimeSpan.FromSeconds(1);

    private readonly Context _context;
    private readonly object _initializationSync = new();
    private IExoPlayer _player = null!;
    private readonly PlayerListener _listener;
    private readonly AndroidMedia3RuntimeQueue _queue;
    private readonly ILogger<AndroidMedia3PlaybackRuntime> _logger;
    private readonly CancellationTokenSource _positionUpdatesCancellation = new();
    private int _audioSessionId;
    private int _lastKnownIndex = -1;
    private int _lastFinishedIndex = -1;
    private long _lastFinishedUtcTicks;
    private long _lastUserTerminalStateRequestUtcTicks;
    private long _ignoreUserTerminalStateReasonUntilUtcTicks;
    private string? _lastFinishedStableCacheKey;
    private bool _currentItemBecamePlayable;
    private bool _lastIsPlaying;
    private bool _suppressMediaItemTransitionPublishing;
    private Task? _initializationTask;
    private PlaybackRepeatMode _repeatMode;
    private PlaybackShuffleMode _shuffleMode;
    private int _positionUpdatesStarted;
    private bool _disposed;

    public AndroidMedia3PlaybackRuntime(ILogger<AndroidMedia3PlaybackRuntime> logger)
    {
        _logger = logger;
        _context = global::Android.App.Application.Context;
        _queue = new AndroidMedia3RuntimeQueue(this);
        _listener = new PlayerListener(this);
        _logger.LogInformation("Media3 playback runtime created; native player initialization is deferred until playback is requested.");
    }

    public event EventHandler<PlaybackRuntimeStateChangedEventArgs>? StateChanged;

    public event EventHandler<PlaybackMediaItemEventArgs>? MediaItemChanged;

    public event EventHandler<PlaybackPositionChangedEventArgs>? PositionChanged;

    public event EventHandler<PlaybackMediaItemEventArgs>? MediaItemFinished;

    public event EventHandler<PlaybackMediaItemFailedEventArgs>? MediaItemFailed;

    public PlaybackRuntimeState State => ReadPlayer(
        player => MapState(player.PlaybackState, player.PlayWhenReady),
        PlaybackRuntimeState.Stopped);

    public TimeSpan Position => ReadPlayer(
        player => TimeSpan.FromMilliseconds(Math.Max(0, player.CurrentPosition)),
        TimeSpan.Zero);

    public TimeSpan Duration => ReadPlayer(player => player.Duration <= 0 || player.Duration == C.TimeUnset
        ? TimeSpan.Zero
        : TimeSpan.FromMilliseconds(player.Duration), TimeSpan.Zero);

    public IPlaybackRuntimeQueue? Queue => _queue;

    public PlaybackRepeatMode RepeatMode
    {
        // Read from the native player once initialized so changes made directly on it (media-session
        // controllers, Android Auto/Bluetooth) are reflected; the field is only the pre-init buffer.
        get => ReadPlayer(
            player => player.RepeatMode == RepeatModeAll ? PlaybackRepeatMode.All : PlaybackRepeatMode.Off,
            _repeatMode);
        set
        {
            _repeatMode = value;
            ApplyToInitializedPlayer(player =>
                player.RepeatMode = value == PlaybackRepeatMode.All ? RepeatModeAll : RepeatModeOff);
        }
    }

    public PlaybackShuffleMode ShuffleMode
    {
        get => ReadPlayer(
            player => player.ShuffleModeEnabled ? PlaybackShuffleMode.All : PlaybackShuffleMode.Off,
            _shuffleMode);
        set
        {
            _shuffleMode = value;
            ApplyToInitializedPlayer(player => player.ShuffleModeEnabled = value == PlaybackShuffleMode.All);
        }
    }

    public int AudioSessionId => ReadPlayer(
        player => _audioSessionId == 0 ? player.AudioSessionId : _audioSessionId,
        _audioSessionId);

    public Task<MauiMediaItem?> PlayAsync(MauiMediaItem mediaItem) =>
        RunOnInitializedPlayerThreadAsync(() =>
        {
            EnsurePlaybackServiceStarted();
            ResetCurrentItemPlaybackObservation("single item play");
            _logger.LogInformation(
                "Media3 PlayAsync(single) requested. Item={MediaItem}; {Snapshot}",
                DescribeMediaItem(mediaItem),
                CreatePlayerSnapshot());
            SuppressAppCommandUserReason();
            _player.Stop();
            _player.ClearMediaItems();
            _logger.LogInformation("Media3 PlayAsync(single) reset previous player queue. {Snapshot}", CreatePlayerSnapshot());
            _queue.SetItems([mediaItem]);
            _player.SetMediaItem(ToMedia3Item(mediaItem));
            _player.Prepare();
            _player.Play();
            _logger.LogInformation("Media3 PlayAsync(single) submitted to player. {Snapshot}", CreatePlayerSnapshot());
            PublishMediaItemChanged();
            return (MauiMediaItem?)mediaItem;
        });

    public Task<MauiMediaItem?> PlayAsync(IEnumerable<MauiMediaItem> mediaItems) => PlayAsync(mediaItems, 0);

    public Task<MauiMediaItem?> PlayAsync(IEnumerable<MauiMediaItem> mediaItems, int startIndex) =>
        RunOnInitializedPlayerThreadAsync(() =>
        {
            EnsurePlaybackServiceStarted();
            var items = mediaItems.ToList();
            if (items.Count == 0)
            {
                _logger.LogWarning("Media3 PlayAsync(queue) ignored because the queue is empty. {Snapshot}", CreatePlayerSnapshot());
                return null;
            }

            var safeStartIndex = Math.Clamp(startIndex, 0, items.Count - 1);
            ResetCurrentItemPlaybackObservation("queue play");
            _logger.LogInformation(
                "Media3 PlayAsync(queue) requested. QueueCount={QueueCount}; StartIndex={StartIndex}; SafeStartIndex={SafeStartIndex}; StartItem={StartItem}; FirstItem={FirstItem}; {Snapshot}",
                items.Count,
                startIndex,
                safeStartIndex,
                DescribeMediaItem(items[safeStartIndex]),
                DescribeMediaItem(items.FirstOrDefault()),
                CreatePlayerSnapshot());

            SuppressAppCommandUserReason();
            _player.Stop();
            _player.ClearMediaItems();
            _logger.LogInformation("Media3 PlayAsync(queue) reset previous player queue. {Snapshot}", CreatePlayerSnapshot());

            _queue.SetItems(items);
            SetPlayerQueueItems(items, safeStartIndex);
            _player.Prepare();
            _player.Play();
            _logger.LogInformation("Media3 PlayAsync(queue) submitted to player. {Snapshot}", CreatePlayerSnapshot());
            PublishMediaItemChanged();
            return _queue.Current;
        });

    public Task<MauiMediaItem?> ReplaceQueueAsync(
        IEnumerable<MauiMediaItem> mediaItems,
        int currentIndex,
        TimeSpan currentPosition,
        bool playWhenReady) =>
        RunOnInitializedPlayerThreadAsync(() =>
        {
            EnsurePlaybackServiceStarted();
            var items = mediaItems.ToList();
            if (items.Count == 0)
            {
                _logger.LogWarning("Media3 ReplaceQueueAsync ignored because the queue is empty. {Snapshot}", CreatePlayerSnapshot());
                return null;
            }

            var safeCurrentIndex = Math.Clamp(currentIndex, 0, items.Count - 1);
            var safePositionMs = currentPosition <= TimeSpan.Zero
                ? 0L
                : (long)currentPosition.TotalMilliseconds;

            ResetCurrentItemPlaybackObservation("queue replacement");
            _logger.LogInformation(
                "Media3 ReplaceQueueAsync requested. QueueCount={QueueCount}; CurrentIndex={CurrentIndex}; SafeCurrentIndex={SafeCurrentIndex}; CurrentPosition={CurrentPosition}; PlayWhenReady={PlayWhenReady}; CurrentItem={CurrentItem}; {Snapshot}",
                items.Count,
                currentIndex,
                safeCurrentIndex,
                currentPosition,
                playWhenReady,
                DescribeMediaItem(items[safeCurrentIndex]),
                CreatePlayerSnapshot());

            SuppressAppCommandUserReason();
            var previousTransitionSuppression = _suppressMediaItemTransitionPublishing;
            _suppressMediaItemTransitionPublishing = true;
            try
            {
                _queue.SetItems(items);
                SetPlayerQueueItems(items, safeCurrentIndex, safePositionMs);
                _player.Prepare();
                EnsurePlayerOnRequestedItem(items, safeCurrentIndex, safePositionMs);

                if (playWhenReady)
                {
                    _player.Play();
                }
                else
                {
                    _player.Pause();
                }
            }
            finally
            {
                _suppressMediaItemTransitionPublishing = previousTransitionSuppression;
            }

            _logger.LogInformation("Media3 ReplaceQueueAsync submitted to player. {Snapshot}", CreatePlayerSnapshot());
            PublishMediaItemChanged();
            return _queue.Current;
        });

    public Task PlayAsync() =>
        RunOnInitializedPlayerThreadAsync(() =>
        {
            EnsurePlaybackServiceStarted();
            _logger.LogInformation("Media3 PlayAsync(resume) requested. {Snapshot}", CreatePlayerSnapshot());
            _player.Play();
            PublishStateChanged();
        });

    public Task PauseAsync() =>
        RunOnInitializedPlayerThreadAsync(() =>
        {
            _logger.LogInformation("Media3 PauseAsync requested. {Snapshot}", CreatePlayerSnapshot());
            SuppressAppCommandUserReason();
            _player.Pause();
            PublishStateChanged();
        });

    public Task StopAsync() =>
        RunOnInitializedPlayerThreadAsync(() =>
        {
            _logger.LogInformation("Media3 StopAsync requested. {Snapshot}", CreatePlayerSnapshot());
            SuppressAppCommandUserReason();
            _player.Stop();
            _player.ClearMediaItems();
            _queue.SetItems([]);
            _lastKnownIndex = -1;
            ResetCurrentItemPlaybackObservation("stop");
            PublishStateChanged();
            StopPlaybackService("StopAsync");
        });

    public Task<bool> PlayNextAsync() =>
        RunOnInitializedPlayerThreadAsync(() =>
        {
            if (!_player.HasNextMediaItem)
            {
                return false;
            }

            _logger.LogInformation("Media3 PlayNextAsync requested. {Snapshot}", CreatePlayerSnapshot());
            ResetCurrentItemPlaybackObservation("play next");
            _player.SeekToNextMediaItem();
            _player.Play();
            PublishMediaItemChanged();
            return true;
        });

    public Task<bool> PlayPreviousAsync() =>
        RunOnInitializedPlayerThreadAsync(() =>
        {
            if (!_player.HasPreviousMediaItem)
            {
                return false;
            }

            _logger.LogInformation("Media3 PlayPreviousAsync requested. {Snapshot}", CreatePlayerSnapshot());
            ResetCurrentItemPlaybackObservation("play previous");
            _player.SeekToPreviousMediaItem();
            _player.Play();
            PublishMediaItemChanged();
            return true;
        });

    public Task<bool> PlayQueueItemAsync(int index) =>
        RunOnInitializedPlayerThreadAsync(() =>
        {
            if (index < 0 || index >= _queue.Count)
            {
                return false;
            }

            _logger.LogInformation("Media3 PlayQueueItemAsync requested. RequestedIndex={RequestedIndex}; {Snapshot}", index, CreatePlayerSnapshot());
            ResetCurrentItemPlaybackObservation("play queue item");
            _player.SeekTo(index, 0);
            _player.Play();
            PublishMediaItemChanged();
            return true;
        });

    public Task SeekToAsync(TimeSpan position) =>
        RunOnInitializedPlayerThreadAsync(() =>
        {
            _logger.LogInformation("Media3 SeekToAsync requested. Position={Position}; {Snapshot}", position, CreatePlayerSnapshot());
            _player.SeekTo((long)position.TotalMilliseconds);
        });

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _positionUpdatesCancellation.Cancel();
        var player = Volatile.Read(ref _player);
        if (player != null)
        {
            RunOnPlayerThread(() => player.RemoveListener(_listener));
        }

        _listener.Dispose();
    }

    private Task EnsureInitializedAsync()
    {
        lock (_initializationSync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            // A faulted first initialization (e.g. a transient failure) must not brick playback
            // for the rest of the process lifetime — retry on the next playback request.
            if (_initializationTask is null or { IsFaulted: true } or { IsCanceled: true })
            {
                _initializationTask = InitializeAsync();
            }

            return _initializationTask;
        }
    }

    private async Task InitializeAsync()
    {
        var player = await AndroidMedia3PlaybackRegistry
            .GetOrCreatePlayerAsync(_context)
            .ConfigureAwait(false);
        await AndroidMedia3PlaybackRegistry
            .GetOrCreateMediaSessionAsync(_context)
            .ConfigureAwait(false);

        await RunOnPlayerThreadCoreAsync(() =>
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _player = player;
            _player.AddListener(_listener);
            _player.RepeatMode = _repeatMode == PlaybackRepeatMode.All ? RepeatModeAll : RepeatModeOff;
            _player.ShuffleModeEnabled = _shuffleMode == PlaybackShuffleMode.All;
            _lastKnownIndex = _player.CurrentMediaItemIndex;
            _lastIsPlaying = MapState(_player.PlaybackState, _player.PlayWhenReady) == PlaybackRuntimeState.Playing;
        }).ConfigureAwait(false);

        if (Interlocked.Exchange(ref _positionUpdatesStarted, 1) == 0)
        {
            _ = Task.Run(PublishPositionUpdatesAsync);
        }

        _logger.LogInformation("Media3 playback runtime initialized on first playback request. {Snapshot}", CreatePlayerSnapshot());
    }

    private T ReadPlayer<T>(Func<IExoPlayer, T> read, T fallback)
    {
        var player = Volatile.Read(ref _player);
        return player == null
            ? fallback
            : RunOnPlayerThread(() => read(player));
    }

    private void ApplyToInitializedPlayer(Action<IExoPlayer> action)
    {
        var player = Volatile.Read(ref _player);
        if (player != null)
        {
            RunOnPlayerThread(() => action(player));
        }
    }

    private static void RunOnPlayerThread(Action action)
    {
        if (MainThread.IsMainThread)
        {
            action();
            return;
        }

        MainThread.InvokeOnMainThreadAsync(action).GetAwaiter().GetResult();
    }

    private static T RunOnPlayerThread<T>(Func<T> action)
    {
        return MainThread.IsMainThread
            ? action()
            : MainThread.InvokeOnMainThreadAsync(action).GetAwaiter().GetResult();
    }

    private static Task RunOnPlayerThreadCoreAsync(Action action)
    {
        if (MainThread.IsMainThread)
        {
            action();
            return Task.CompletedTask;
        }

        return MainThread.InvokeOnMainThreadAsync(action);
    }

    private static Task<T> RunOnPlayerThreadCoreAsync<T>(Func<T> action)
    {
        return MainThread.IsMainThread
            ? Task.FromResult(action())
            : MainThread.InvokeOnMainThreadAsync(action);
    }

    private async Task RunOnInitializedPlayerThreadAsync(Action action)
    {
        await EnsureInitializedAsync().ConfigureAwait(false);
        await RunOnPlayerThreadCoreAsync(action).ConfigureAwait(false);
    }

    private async Task<T> RunOnInitializedPlayerThreadAsync<T>(Func<T> action)
    {
        await EnsureInitializedAsync().ConfigureAwait(false);
        return await RunOnPlayerThreadCoreAsync(action).ConfigureAwait(false);
    }

    private void EnsurePlaybackServiceStarted()
    {
        AndroidMedia3CacheProvider.EnsureNotificationChannels(_context);
        var intent = new Intent(_context, typeof(PlaybackMediaSessionService));
        _context.StartService(intent);
        _logger.LogInformation("Media3 playback service/session ensured. {Snapshot}", CreatePlayerSnapshot());
    }

    private void StopPlaybackService(string reason)
    {
        try
        {
            PlaybackMediaSessionService.RequestStop(_context);
            _logger.LogInformation("Media3 playback service stop requested. Reason={Reason}; {Snapshot}", reason, CreatePlayerSnapshot());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Media3 playback service stop request failed. Reason={Reason}; {Snapshot}", reason, CreatePlayerSnapshot());
        }
    }

    private async Task PublishPositionUpdatesAsync()
    {
        while (!_positionUpdatesCancellation.IsCancellationRequested)
        {
            try
            {
                PositionChanged?.Invoke(this, new PlaybackPositionChangedEventArgs(Position));
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Media3 position update failed.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1), _positionUpdatesCancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private void OnPlaybackStateChanged(int playbackState)
    {
        if (playbackState == PlayerStateReady)
        {
            _currentItemBecamePlayable = true;
        }

        var mappedState = MapState(playbackState, _player.PlayWhenReady);
        _logger.LogInformation(
            "Media3 playback state changed. RawState={RawState}; MappedState={MappedState}; {Snapshot}",
            PlaybackStateName(playbackState),
            mappedState,
            CreatePlayerSnapshot());

        PublishStateChanged(mappedState);
        if (playbackState == PlayerStateEnded)
        {
            PublishMediaItemFinishedIfAllowed(_queue.Current, _player.CurrentMediaItemIndex, "playback state ended", suppressStartupEnd: true);
        }
    }

    private void OnMediaItemTransition(CommonMediaItem? mediaItem, int reason)
    {
        var previousIndex = _lastKnownIndex;
        var currentIndex = _player.CurrentMediaItemIndex;
        _logger.LogInformation(
            "Media3 media item transition. Reason={Reason}; PreviousIndex={PreviousIndex}; CurrentIndex={CurrentIndex}; MediaId={MediaId}; {Snapshot}",
            MediaItemTransitionReasonName(reason),
            previousIndex,
            currentIndex,
            mediaItem?.MediaId,
            CreatePlayerSnapshot());

        if (_suppressMediaItemTransitionPublishing)
        {
            _lastKnownIndex = currentIndex;
            ResetCurrentItemPlaybackObservation("suppressed media item transition");
            _logger.LogInformation(
                "Media3 media item transition publication suppressed during queue replacement. Reason={Reason}; CurrentIndex={CurrentIndex}; MediaId={MediaId}; {Snapshot}",
                MediaItemTransitionReasonName(reason),
                currentIndex,
                mediaItem?.MediaId,
                CreatePlayerSnapshot());
            return;
        }

        if (previousIndex >= 0 && previousIndex != currentIndex && previousIndex < _queue.Count)
        {
            PublishMediaItemFinishedIfAllowed(_queue[previousIndex], previousIndex, "media item transition", suppressStartupEnd: false);
        }

        _lastKnownIndex = currentIndex;
        ResetCurrentItemPlaybackObservation("media item transition");
        PublishMediaItemChanged();
    }

    private void OnPlayerError(PlaybackException error)
    {
        _logger.LogError(
            error,
            "Media3 player error. Message={Message}; Cause={Cause}; {Snapshot}",
            error.Message,
            error.Cause?.ToString(),
            CreatePlayerSnapshot());
        PublishStateChanged(PlaybackRuntimeState.Failed);
        MediaItemFailed?.Invoke(
            this,
            new PlaybackMediaItemFailedEventArgs(
                _queue.Current,
                new InvalidOperationException(error.Message),
                error.Message));
    }

    private void PublishStateChanged() => PublishStateChanged(State);

    private void PublishStateChanged(PlaybackRuntimeState state) =>
        StateChanged?.Invoke(this, new PlaybackRuntimeStateChangedEventArgs(state, ResolveStateChangeReason(state)));

    private PlaybackRuntimeStateChangeReason ResolveStateChangeReason(PlaybackRuntimeState state)
    {
        if (state != PlaybackRuntimeState.Paused && state != PlaybackRuntimeState.Stopped)
        {
            return PlaybackRuntimeStateChangeReason.Unknown;
        }

        if (ShouldSuppressUserTerminalReason())
        {
            return PlaybackRuntimeStateChangeReason.Unknown;
        }

        var lastUserRequestTicks = Volatile.Read(ref _lastUserTerminalStateRequestUtcTicks);
        if (lastUserRequestTicks <= 0)
        {
            return PlaybackRuntimeStateChangeReason.Unknown;
        }

        var elapsedTicks = DateTime.UtcNow.Ticks - lastUserRequestTicks;
        return elapsedTicks >= 0 && elapsedTicks <= UserTerminalStateReasonWindow.Ticks
            ? PlaybackRuntimeStateChangeReason.UserRequest
            : PlaybackRuntimeStateChangeReason.Unknown;
    }

    private void PublishMediaItemChanged()
    {
        _lastKnownIndex = _player.CurrentMediaItemIndex;
        _logger.LogInformation(
            "Media3 media item changed published. CurrentItem={CurrentItem}; {Snapshot}",
            DescribeMediaItem(_queue.Current),
            CreatePlayerSnapshot());
        MediaItemChanged?.Invoke(this, new PlaybackMediaItemEventArgs(_queue.Current));
        PublishStateChanged();
    }

    private void OnIsPlayingChanged(bool isPlaying)
    {
        _lastIsPlaying = isPlaying;
        if (isPlaying)
        {
            _currentItemBecamePlayable = true;
        }

        _logger.LogInformation("Media3 is-playing changed. IsPlaying={IsPlaying}; {Snapshot}", isPlaying, CreatePlayerSnapshot());
        PublishStateChanged();
    }

    private void OnPlayWhenReadyChanged(bool playWhenReady, int reason)
    {
        var userTerminalReason = !playWhenReady && IsUserControlledPlayWhenReadyReason(reason);
        if (userTerminalReason)
        {
            if (ShouldSuppressUserTerminalReason())
            {
                _logger.LogInformation(
                    "Media3 play-when-ready user terminal reason suppressed for app command. PlayWhenReady={PlayWhenReady}; Reason={Reason}; {Snapshot}",
                    playWhenReady,
                    reason,
                    CreatePlayerSnapshot());
            }
            else
            {
                Interlocked.Exchange(ref _lastUserTerminalStateRequestUtcTicks, DateTime.UtcNow.Ticks);
            }
        }

        _logger.LogInformation(
            "Media3 play-when-ready changed. PlayWhenReady={PlayWhenReady}; Reason={Reason}; UserTerminalReason={UserTerminalReason}; {Snapshot}",
            playWhenReady,
            reason,
            userTerminalReason,
            CreatePlayerSnapshot());
        PublishStateChanged();
    }

    private static bool IsUserControlledPlayWhenReadyReason(int reason) =>
        reason == PlayWhenReadyChangeReasonUserRequest || reason == PlayWhenReadyChangeReasonRemote;

    private void SuppressAppCommandUserReason() =>
        Interlocked.Exchange(
            ref _ignoreUserTerminalStateReasonUntilUtcTicks,
            DateTime.UtcNow.Ticks + AppCommandUserReasonSuppressionWindow.Ticks);

    private bool ShouldSuppressUserTerminalReason() =>
        DateTime.UtcNow.Ticks <= Volatile.Read(ref _ignoreUserTerminalStateReasonUntilUtcTicks);

    private void OnPlaybackSuppressionReasonChanged(int playbackSuppressionReason)
    {
        _logger.LogWarning(
            "Media3 playback suppression reason changed. SuppressionReason={SuppressionReason}; {Snapshot}",
            playbackSuppressionReason,
            CreatePlayerSnapshot());
        PublishStateChanged();
    }

    private void PublishMediaItemFinishedIfAllowed(MauiMediaItem? mediaItem, int queueIndex, string trigger, bool suppressStartupEnd)
    {
        if (suppressStartupEnd && ShouldSuppressStartupEnd(trigger))
        {
            return;
        }

        var stableCacheKey = mediaItem?.StableCacheKey ?? string.Empty;
        var nowTicks = DateTime.UtcNow.Ticks;
        if (_lastFinishedIndex == queueIndex &&
            string.Equals(_lastFinishedStableCacheKey, stableCacheKey, StringComparison.Ordinal) &&
            nowTicks - _lastFinishedUtcTicks < TimeSpan.FromMilliseconds(250).Ticks)
        {
            _logger.LogDebug(
                "Media3 duplicate MediaItemFinished suppressed. Trigger={Trigger}; QueueIndex={QueueIndex}; StableCacheKey={StableCacheKey}; {Snapshot}",
                trigger,
                queueIndex,
                stableCacheKey,
                CreatePlayerSnapshot());
            return;
        }

        _lastFinishedIndex = queueIndex;
        _lastFinishedStableCacheKey = stableCacheKey;
        _lastFinishedUtcTicks = nowTicks;
        _logger.LogInformation(
            "Media3 MediaItemFinished published. Trigger={Trigger}; QueueIndex={QueueIndex}; Item={MediaItem}; {Snapshot}",
            trigger,
            queueIndex,
            DescribeMediaItem(mediaItem),
            CreatePlayerSnapshot());
        MediaItemFinished?.Invoke(this, new PlaybackMediaItemEventArgs(mediaItem));
    }

    private bool ShouldSuppressStartupEnd(string trigger)
    {
        var positionMs = Math.Max(0, _player.CurrentPosition);
        var durationMs = _player.Duration;
        var hasUnknownOrZeroDuration = durationMs <= 0 || durationMs == C.TimeUnset;
        if (!_currentItemBecamePlayable && positionMs == 0 && hasUnknownOrZeroDuration)
        {
            _logger.LogWarning(
                "Media3 ended before current item became playable; suppressing finish event. Trigger={Trigger}; {Snapshot}",
                trigger,
                CreatePlayerSnapshot());
            return true;
        }

        return false;
    }

    private void ResetCurrentItemPlaybackObservation(string reason)
    {
        _currentItemBecamePlayable = false;
        _lastFinishedIndex = -1;
        _lastFinishedStableCacheKey = null;
        _lastFinishedUtcTicks = 0;
        _logger.LogDebug("Media3 current item playback observation reset. Reason={Reason}; {Snapshot}", reason, CreatePlayerSnapshot());
    }

    private static PlaybackRuntimeState MapState(int playbackState, bool playWhenReady) => playbackState switch
    {
        PlayerStateBuffering => PlaybackRuntimeState.Buffering,
        PlayerStateReady when playWhenReady => PlaybackRuntimeState.Playing,
        PlayerStateReady => PlaybackRuntimeState.Paused,
        _ => PlaybackRuntimeState.Stopped
    };

    private string CreatePlayerSnapshot()
    {
        if (Volatile.Read(ref _player) == null)
        {
            return _initializationTask == null
                ? "InitializationState=NotStarted"
                : "InitializationState=InProgress";
        }

        return RunOnPlayerThread(CreatePlayerSnapshotOnPlayerThread);
    }

    private string CreatePlayerSnapshotOnPlayerThread()
    {
        var durationMs = _player.Duration == C.TimeUnset ? "TimeUnset" : _player.Duration.ToString();
        return $"RawState={PlaybackStateName(_player.PlaybackState)}; PlayWhenReady={_player.PlayWhenReady}; IsPlaying={_lastIsPlaying}; CurrentIndex={_player.CurrentMediaItemIndex}; QueueCount={_queue.Count}; PlayerMediaItemCount={_player.MediaItemCount}; PositionMs={Math.Max(0, _player.CurrentPosition)}; DurationMs={durationMs}; AudioSessionId={AudioSessionId}; BecamePlayable={_currentItemBecamePlayable}; CurrentItem={DescribeMediaItem(_queue.Current)}";
    }

    private static string DescribeMediaItem(MauiMediaItem? item)
    {
        if (item == null)
        {
            return "(null)";
        }

        return $"SongId={item.SongId}; StableCacheKey={item.StableCacheKey}; Title={item.Title}; Artist={item.Artist}; IsLocal={item.IsLocal}; IsSleepSafe={item.IsSleepSafe}; Uri={SanitizeMediaUri(item.MediaUri)}";
    }

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

    private static string PlaybackStateName(int playbackState) => playbackState switch
    {
        PlayerStateIdle => "Idle(1)",
        PlayerStateBuffering => "Buffering(2)",
        PlayerStateReady => "Ready(3)",
        PlayerStateEnded => "Ended(4)",
        _ => $"Unknown({playbackState})"
    };

    private static string MediaItemTransitionReasonName(int reason) => reason switch
    {
        0 => "Repeat(0)",
        1 => "Auto(1)",
        2 => "Seek(2)",
        3 => "PlaylistChanged(3)",
        _ => $"Unknown({reason})"
    };

    private static CommonMediaItem ToMedia3Item(MauiMediaItem item)
    {
        var metadataBuilder = new MediaMetadata.Builder();
        metadataBuilder.SetTitle(item.Title);
        metadataBuilder.SetArtist(item.Artist);

        if (!string.IsNullOrWhiteSpace(item.ImageUri))
        {
            metadataBuilder.SetArtworkUri(global::Android.Net.Uri.Parse(item.ImageUri));
        }

        var metadata = metadataBuilder.Build()
            ?? throw new InvalidOperationException("Media3 MediaMetadata.Builder returned null.");

        var builder = new CommonMediaItem.Builder();
        builder.SetMediaId(item.StableCacheKey);
        builder.SetCustomCacheKey(item.StableCacheKey);
        builder.SetMediaMetadata(metadata);

        if (item.IsLocal && Path.IsPathRooted(item.MediaUri))
        {
            builder.SetUri(global::Android.Net.Uri.FromFile(new Java.IO.File(item.MediaUri)));
        }
        else
        {
            builder.SetUri(item.MediaUri);
        }

        return builder.Build()
            ?? throw new InvalidOperationException("Media3 MediaItem.Builder returned null.");
    }

    /// <summary>
    /// Make certain the player is on the SONG the queue rebuild asked for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// By media id, never by index. The bug this replaces was the correction itself: it compared
    /// <c>CurrentMediaItemIndex</c> against the index this runtime wanted and, on a mismatch,
    /// seeked to that index. When the player was still holding the PREVIOUS queue, that index
    /// belonged to the previous ordering - so a player correctly playing the right song at the old
    /// index was dragged onto a different song at the new one. Leaving the featured queue, where
    /// a song sits first, for the artist queue, where it sits second, moved playback from that song
    /// onto its neighbour while every label kept naming the first. The correction caused the fault
    /// it appeared to be repairing.
    /// </para>
    /// <para>
    /// So: if the player is already on the right song, its index is irrelevant and nothing happens.
    /// Only when it is on the WRONG song does this seek, and then to wherever that song actually
    /// sits in the player's own list rather than to where this runtime assumes it sits.
    /// </para>
    /// </remarks>
    private void EnsurePlayerOnRequestedItem(IReadOnlyList<MauiMediaItem> items, int requestedIndex, long positionMs)
    {
        var expectedId = items[requestedIndex].StableCacheKey;
        var playerIds = ReadPlayerMediaIds();
        var correction = PlaybackQueueAlignment.ResolveCorrection(
            expectedId,
            _player.CurrentMediaItem?.MediaId,
            playerIds);

        switch (correction.Kind)
        {
            case QueueCorrectionKind.AlreadyCorrect:
                return;

            case QueueCorrectionKind.NotPresent:
                _logger.LogError(
                    "Media3 ReplaceQueueAsync cannot place the player on the requested song because the player's playlist does not contain it; audio will not match the reported song. ExpectedMediaId={ExpectedMediaId}; RequestedIndex={RequestedIndex}; {Snapshot}",
                    expectedId,
                    requestedIndex,
                    CreatePlayerSnapshot());
                return;
        }

        _logger.LogWarning(
            "Media3 ReplaceQueueAsync seek correction required. ExpectedMediaId={ExpectedMediaId}; RequestedIndex={RequestedIndex}; ActualIndex={ActualIndex}; RequestedPositionMs={RequestedPositionMs}; {Snapshot}",
            expectedId,
            requestedIndex,
            correction.SeekIndex,
            positionMs,
            CreatePlayerSnapshot());

        _player.SeekTo(correction.SeekIndex, positionMs);

        if (_player.CurrentMediaItem?.MediaId != expectedId)
        {
            _logger.LogError(
                "Media3 ReplaceQueueAsync could not move the player onto the requested song; audio will not match the reported song. ExpectedMediaId={ExpectedMediaId}; ActualMediaId={ActualMediaId}; {Snapshot}",
                expectedId,
                _player.CurrentMediaItem?.MediaId,
                CreatePlayerSnapshot());
        }
    }

    /// <summary>
    /// Whether the player is really holding the playlist just submitted, item for item.
    /// </summary>
    /// <remarks>
    /// Compares media ids in order rather than comparing counts. Two different queues frequently
    /// have the same length - swapping a two-song featured queue for a two-song artist queue is the
    /// exact case that produced a silent mismatch in production - and a count check cannot tell
    /// those apart. Media ids are the per-song stable cache key, set in <see cref="ToMedia3Item"/>.
    /// </remarks>
    private bool PlayerQueueMatches(IReadOnlyList<CommonMediaItem> expected) =>
        PlaybackQueueAlignment.QueueMatches(
            expected.Select(item => item.MediaId).ToList(),
            ReadPlayerMediaIds());

    /// <summary>The media ids the player is actually holding, in its own order.</summary>
    private List<string?> ReadPlayerMediaIds()
    {
        var ids = new List<string?>(_player.MediaItemCount);
        for (var index = 0; index < _player.MediaItemCount; index++)
        {
            ids.Add(_player.GetMediaItemAt(index)?.MediaId);
        }

        return ids;
    }

    private void SetPlayerQueueItems(IReadOnlyList<MauiMediaItem> items, int startIndex, long startPositionMs = 0)
    {
        var media3Items = items.Select(ToMedia3Item).ToList();
        var safeStartPositionMs = Math.Max(0, startPositionMs);
        _player.SetMediaItems(media3Items, startIndex, safeStartPositionMs);

        // Observed on-device (Galaxy S25, Media3 bulk-submit): SetMediaItems can return with the
        // native playlist still empty, so the follow-up Prepare() immediately ends playback.
        // Fall back to the per-item submission path production used before this call was batched.
        //
        // Checked by IDENTITY, not by count. Counting caught the empty case and nothing else: when
        // one queue is swapped for another of the SAME LENGTH - the two-song featured queue for the
        // two-song artist queue - a submission that left the OLD ORDER in place still counted 2 of
        // 2 and passed. The player then held one order while this class held another, and every
        // index either side exchanged after that referred to a different song.
        if (PlayerQueueMatches(media3Items))
        {
            return;
        }

        _logger.LogWarning(
            "Media3 bulk SetMediaItems did not apply the requested queue (count {AppliedCount} of {RequestedCount}); falling back to per-item queue submission. StartIndex={StartIndex}; StartPositionMs={StartPositionMs}",
            _player.MediaItemCount,
            media3Items.Count,
            startIndex,
            safeStartPositionMs);

        _player.ClearMediaItems();
        _player.SetMediaItem(media3Items[0]);
        for (var index = 1; index < media3Items.Count; index++)
        {
            _player.AddMediaItem(media3Items[index]);
        }

        if (startIndex > 0 || safeStartPositionMs > 0)
        {
            _player.SeekTo(startIndex, safeStartPositionMs);
        }

        if (!PlayerQueueMatches(media3Items))
        {
            _logger.LogError(
                "Media3 per-item queue submission ALSO failed to apply the requested queue; the player is holding a different playlist from the one this runtime believes it has. RequestedCount={RequestedCount}; PlayerCount={PlayerCount}; {Snapshot}",
                media3Items.Count,
                _player.MediaItemCount,
                CreatePlayerSnapshot());
        }

        _logger.LogInformation(
            "Media3 per-item queue submission completed. PlayerMediaItemCount={PlayerMediaItemCount}; RequestedCount={RequestedCount}",
            _player.MediaItemCount,
            media3Items.Count);
    }

    private sealed class AndroidMedia3RuntimeQueue(AndroidMedia3PlaybackRuntime owner) : IPlaybackRuntimeQueue
    {
        private List<MauiMediaItem> _items = [];

        public bool HasCurrent => CurrentIndex >= 0 && CurrentIndex < _items.Count;

        public int CurrentIndex => owner.ReadPlayer(player => player.CurrentMediaItemIndex, -1);

        public MauiMediaItem? Current => this[CurrentIndex];

        public MauiMediaItem? Next => owner.ReadPlayer(
            player => player.HasNextMediaItem ? this[player.NextMediaItemIndex] : null,
            null);

        public MauiMediaItem? Previous => owner.ReadPlayer(
            player => player.HasPreviousMediaItem ? this[player.PreviousMediaItemIndex] : null,
            null);

        public int Count => _items.Count;

        public MauiMediaItem? this[int index] =>
            index >= 0 && index < _items.Count
                ? _items[index]
                : null;

        public void SetItems(IReadOnlyList<MauiMediaItem> items) => _items = items.ToList();

        public IEnumerator<MauiMediaItem> GetEnumerator() => _items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class PlayerListener(AndroidMedia3PlaybackRuntime owner) : Java.Lang.Object, IPlayerListener
    {
        public void OnAudioAttributesChanged(AudioAttributes? audioAttributes) { }

        public void OnAudioSessionIdChanged(int audioSessionId) => owner._audioSessionId = audioSessionId;

        public void OnAvailableCommandsChanged(PlayerCommands? availableCommands) { }

        public void OnCues(AndroidX.Media3.Common.Text.CueGroup? cueGroup) { }

        public void OnCuesDeprecated(IList<AndroidX.Media3.Common.Text.Cue>? cues) { }

        public void OnDeviceInfoChanged(AndroidX.Media3.Common.DeviceInfo? deviceInfo) { }

        public void OnDeviceVolumeChanged(int volume, bool muted) { }

        public void OnEvents(IPlayer? player, PlayerEvents? events) { }

        public void OnIsLoadingChanged(bool isLoading) { }

        public void OnIsPlayingChanged(bool isPlaying) => owner.OnIsPlayingChanged(isPlaying);

        public void OnLoadingChanged(bool isLoading) { }

        public void OnMaxSeekToPreviousPositionChanged(long maxSeekToPreviousPositionMs) { }

        public void OnMediaItemTransition(CommonMediaItem? mediaItem, int reason) => owner.OnMediaItemTransition(mediaItem, reason);

        public void OnMediaMetadataChanged(MediaMetadata? mediaMetadata) { }

        public void OnMetadata(Metadata? metadata) { }

        public void OnPlayWhenReadyChanged(bool playWhenReady, int reason) => owner.OnPlayWhenReadyChanged(playWhenReady, reason);

        public void OnPlaybackParametersChanged(PlaybackParameters? playbackParameters) { }

        public void OnPlaybackStateChanged(int playbackState) => owner.OnPlaybackStateChanged(playbackState);

        public void OnPlaybackSuppressionReasonChanged(int playbackSuppressionReason) => owner.OnPlaybackSuppressionReasonChanged(playbackSuppressionReason);

        public void OnPlayerError(PlaybackException? error)
        {
            if (error != null)
            {
                owner.OnPlayerError(error);
            }
        }

        public void OnPlayerErrorChanged(PlaybackException? error) { }

        public void OnPlayerStateChanged(bool playWhenReady, int playbackState) => owner.OnPlaybackStateChanged(playbackState);

        public void OnPlaylistMetadataChanged(MediaMetadata? mediaMetadata) { }

        public void OnPositionDiscontinuity(PlayerPositionInfo? oldPosition, PlayerPositionInfo? newPosition, int reason) { }

        public void OnRenderedFirstFrame() { }

        public void OnRepeatModeChanged(int repeatMode) { }

        public void OnSeekBackIncrementChanged(long seekBackIncrementMs) { }

        public void OnSeekForwardIncrementChanged(long seekForwardIncrementMs) { }

        public void OnShuffleModeEnabledChanged(bool shuffleModeEnabled) { }

        public void OnSkipSilenceEnabledChanged(bool skipSilenceEnabled) { }

        public void OnSurfaceSizeChanged(int width, int height) { }

        public void OnTimelineChanged(Timeline? timeline, int reason) { }

        public void OnTrackSelectionParametersChanged(TrackSelectionParameters? parameters) { }

        public void OnTracksChanged(Tracks? tracks) { }

        public void OnVideoSizeChanged(VideoSize? videoSize) { }

        public void OnVolumeChanged(float volume) { }
    }
}
