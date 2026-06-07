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
    private readonly IExoPlayer _player;
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
    private bool _disposed;

    public AndroidMedia3PlaybackRuntime(ILogger<AndroidMedia3PlaybackRuntime> logger)
    {
        _logger = logger;
        _context = global::Android.App.Application.Context;
        _player = RunOnPlayerThread(() => AndroidMedia3PlaybackRegistry.GetOrCreatePlayer(_context));
        _queue = new AndroidMedia3RuntimeQueue(this);
        _listener = new PlayerListener(this);
        RunOnPlayerThread(() =>
        {
            _player.AddListener(_listener);
            _lastKnownIndex = _player.CurrentMediaItemIndex;
            _lastIsPlaying = MapState(_player.PlaybackState, _player.PlayWhenReady) == PlaybackRuntimeState.Playing;
        });
        _logger.LogInformation("Media3 playback runtime initialized. {Snapshot}", CreatePlayerSnapshot());
        _ = Task.Run(PublishPositionUpdatesAsync);
    }

    public event EventHandler<PlaybackRuntimeStateChangedEventArgs>? StateChanged;

    public event EventHandler<PlaybackMediaItemEventArgs>? MediaItemChanged;

    public event EventHandler<PlaybackPositionChangedEventArgs>? PositionChanged;

    public event EventHandler<PlaybackMediaItemEventArgs>? MediaItemFinished;

    public event EventHandler<PlaybackMediaItemFailedEventArgs>? MediaItemFailed;

    public PlaybackRuntimeState State => RunOnPlayerThread(() => MapState(_player.PlaybackState, _player.PlayWhenReady));

    public TimeSpan Position => RunOnPlayerThread(() => TimeSpan.FromMilliseconds(Math.Max(0, _player.CurrentPosition)));

    public TimeSpan Duration => RunOnPlayerThread(() => _player.Duration <= 0 || _player.Duration == C.TimeUnset
        ? TimeSpan.Zero
        : TimeSpan.FromMilliseconds(_player.Duration));

    public IPlaybackRuntimeQueue? Queue => _queue;

    public PlaybackRepeatMode RepeatMode
    {
        get => RunOnPlayerThread(() => _player.RepeatMode == RepeatModeAll ? PlaybackRepeatMode.All : PlaybackRepeatMode.Off);
        set => RunOnPlayerThread(() => _player.RepeatMode = value == PlaybackRepeatMode.All ? RepeatModeAll : RepeatModeOff);
    }

    public PlaybackShuffleMode ShuffleMode
    {
        get => RunOnPlayerThread(() => _player.ShuffleModeEnabled ? PlaybackShuffleMode.All : PlaybackShuffleMode.Off);
        set => RunOnPlayerThread(() => _player.ShuffleModeEnabled = value == PlaybackShuffleMode.All);
    }

    public int AudioSessionId => RunOnPlayerThread(() => _audioSessionId == 0 ? _player.AudioSessionId : _audioSessionId);

    public Task<MauiMediaItem?> PlayAsync(MauiMediaItem mediaItem) =>
        RunOnPlayerThreadAsync(() =>
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
        RunOnPlayerThreadAsync(() =>
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
        RunOnPlayerThreadAsync(() =>
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
                if (_player.CurrentMediaItemIndex != safeCurrentIndex)
                {
                    _logger.LogWarning(
                        "Media3 ReplaceQueueAsync seek correction required. RequestedIndex={RequestedIndex}; ActualIndex={ActualIndex}; RequestedPositionMs={RequestedPositionMs}; {Snapshot}",
                        safeCurrentIndex,
                        _player.CurrentMediaItemIndex,
                        safePositionMs,
                        CreatePlayerSnapshot());
                    _player.SeekTo(safeCurrentIndex, safePositionMs);
                }

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
        RunOnPlayerThreadAsync(() =>
        {
            EnsurePlaybackServiceStarted();
            _logger.LogInformation("Media3 PlayAsync(resume) requested. {Snapshot}", CreatePlayerSnapshot());
            _player.Play();
            PublishStateChanged();
        });

    public Task PauseAsync() =>
        RunOnPlayerThreadAsync(() =>
        {
            _logger.LogInformation("Media3 PauseAsync requested. {Snapshot}", CreatePlayerSnapshot());
            SuppressAppCommandUserReason();
            _player.Pause();
            PublishStateChanged();
        });

    public Task StopAsync() =>
        RunOnPlayerThreadAsync(() =>
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
        RunOnPlayerThreadAsync(() =>
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
        RunOnPlayerThreadAsync(() =>
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
        RunOnPlayerThreadAsync(() =>
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
        RunOnPlayerThreadAsync(() =>
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
        RunOnPlayerThread(() => _player.RemoveListener(_listener));
        _listener.Dispose();
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

    private static Task RunOnPlayerThreadAsync(Action action)
    {
        if (MainThread.IsMainThread)
        {
            action();
            return Task.CompletedTask;
        }

        return MainThread.InvokeOnMainThreadAsync(action);
    }

    private static Task<T> RunOnPlayerThreadAsync<T>(Func<T> action)
    {
        return MainThread.IsMainThread
            ? Task.FromResult(action())
            : MainThread.InvokeOnMainThreadAsync(action);
    }

    private void EnsurePlaybackServiceStarted()
    {
        AndroidMedia3CacheProvider.EnsureNotificationChannels(_context);
        var intent = new Intent(_context, typeof(PlaybackMediaSessionService));
        _context.StartService(intent);
        AndroidMedia3PlaybackRegistry.GetOrCreateMediaSession(_context);
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

    private string CreatePlayerSnapshot() => RunOnPlayerThread(CreatePlayerSnapshotOnPlayerThread);

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

    private void SetPlayerQueueItems(IReadOnlyList<MauiMediaItem> items, int startIndex, long startPositionMs = 0)
    {
        _player.SetMediaItem(ToMedia3Item(items[0]));
        for (var index = 1; index < items.Count; index++)
        {
            _player.AddMediaItem(ToMedia3Item(items[index]));
        }

        if (startIndex > 0 || startPositionMs > 0)
        {
            _player.SeekTo(startIndex, Math.Max(0, startPositionMs));
        }
    }

    private sealed class AndroidMedia3RuntimeQueue(AndroidMedia3PlaybackRuntime owner) : IPlaybackRuntimeQueue
    {
        private List<MauiMediaItem> _items = [];

        public bool HasCurrent => CurrentIndex >= 0 && CurrentIndex < _items.Count;

        public int CurrentIndex => RunOnPlayerThread(() => owner._player.CurrentMediaItemIndex);

        public MauiMediaItem? Current => this[CurrentIndex];

        public MauiMediaItem? Next => RunOnPlayerThread(() =>
            owner._player.HasNextMediaItem ? this[owner._player.NextMediaItemIndex] : null);

        public MauiMediaItem? Previous => RunOnPlayerThread(() =>
            owner._player.HasPreviousMediaItem ? this[owner._player.PreviousMediaItemIndex] : null);

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
