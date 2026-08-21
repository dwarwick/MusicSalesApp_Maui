using System.Collections;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;
using MusicSalesApp.Common.Helpers;
using MusicSalesApp.Maui.ViewModels;
#if IOS
using Foundation;
using UIKit;
#endif

namespace MusicSalesApp.Maui.Services;

/// <summary>
/// Singleton playback service shared between MusicLibraryPage and SongPlayerPage.
/// Manages all playback state, stream tracking, and preview limits.
/// Uses platform playback runtime for actual audio output, foreground service, and
/// notification controls (Next/Previous buttons appear automatically from the queue).
/// </summary>
public class PlaybackService : IPlaybackService
{
    private static readonly TimeSpan DefaultPlaylistAdvanceFallbackDelay = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan MediaItemFinishedNearEndTolerance = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan DefaultTransientStopConfirmationDelay = TimeSpan.FromSeconds(2);
#if ANDROID
    private static readonly TimeSpan DefaultPositionSamplerInterval = TimeSpan.FromSeconds(1);
#else
    private static readonly TimeSpan DefaultPositionSamplerInterval = TimeSpan.Zero;
#endif
    private static readonly TimeSpan DefaultPositionEventStaleThreshold = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan DefaultSubscriptionStatusRefreshInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan FailureInducedRewindSuppressionWindow = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan QueueSelectionRewindSuppressionGrace = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan DefaultBufferingStallRecoveryDelay = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan PositionSamplerDelayedTickLogThreshold = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan TerminalZeroPositionRecoveryWindow = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan StaleHighPositionAfterTrackResetSuppression = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan UserRequestedStopCleanupSuppressionWindow = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan CachedFailureRecoveryWindow = TimeSpan.FromSeconds(30);
    private const int MaxTerminalZeroPositionRecoveryAttempts = 1;
    private const int MaxCachedFailureRecoveryAttemptsPerSong = 2;
    private const int MaxConsecutiveUnplayableTrackSkips = 3;
    private const int QueueCacheResolutionConcurrency = 3;
    private const int BackgroundWarmAheadTrackCount = 12;
    private const int MaxLoggedPlaylistItems = 10;
    private const int MaxLoggedNativeQueueItems = 5;
    private const string UnspecifiedQueueSourceDescription = "Unspecified queue source";

    private readonly IAuthService _authService;
    private readonly IMusicService _musicService;
    private readonly IPlatformPlaybackRuntime _playbackRuntime;
    private readonly IAudioCacheService _audioCacheService;
    private readonly IQueuePreparationService _queuePreparationService;
    private readonly IPlaybackKeepAliveService _playbackKeepAliveService;
    private readonly IAnonymousFeaturedStreamStore? _anonymousFeaturedStreamStore;
    private readonly INetworkStatusService? _networkStatusService;
    private readonly IImageCacheService? _imageCacheService;
    private readonly ILogger<PlaybackService> _logger;
    private readonly TimeSpan _playlistAdvanceFallbackDelay;
    private readonly TimeSpan _positionSamplerInterval;
    private readonly TimeSpan _positionEventStaleThreshold;
    private readonly TimeSpan _transientStopConfirmationDelay;
    private readonly TimeSpan _subscriptionStatusRefreshInterval;
    private readonly TimeSpan _bufferingStallRecoveryDelay;
    private readonly object _playbackRequestSync = new();
    private readonly object _positionSync = new();
    private CancellationTokenSource? _positionSamplerCancellation;
    private CancellationTokenSource? _terminalStateConfirmationCancellation;
    private CancellationTokenSource? _queuePreparationCancellation;
    private CancellationTokenSource? _queueBuildCancellation;
    private long _lastPositionChangedUtcTicks;
    private long _lastPositionSamplerTickUtcTicks;
    private long _lastSubscriptionStatusRefreshUtcTicks;
    private long _staleHighPositionSuppressionExpiresUtcTicks;
    private long _terminalZeroPositionRecoveryWindowExpiresUtcTicks;
    private int _subscriptionStatusRefreshInProgress;
    private int _subscribeCtaRequestInProgress;
    private int _terminalZeroPositionRecoveryAttemptCount;
    private int _terminalZeroPositionRecoverySongId;
    private long _cachedFailureRecoveryWindowExpiresUtcTicks;
    private int _cachedFailureRecoveryAttemptCount;
    private int _cachedFailureRecoverySongId;
    private int _consecutiveUnplayableTrackSkipCount;
    private int _userRequestedStopCleanupInProgress;
    private long _userRequestedStopCleanupSuppressUntilUtcTicks;

    // Stream tracking state
    private int _streamQualifyingSeconds = 30;
    private int _streamTrackingSongId;
    private double _continuousPlaybackSeconds;
    private bool _streamRecordedForCurrentSong;
    private bool _skipNextStreamPositionSample;

    // Playback position state
    private TimeSpan _playbackPosition;
    private TimeSpan _playbackDuration;

    // Preview limit state
    private const double PreviewLimitSeconds = PreviewAccessPolicy.PreviewLimitSeconds;
    private const int MinPreviewInterval = 2;
    private const int MaxPreviewIntervalExclusive = 5;
    private int _previewEndCount;
    private int _nextCtaThreshold;
    private readonly Random _random = new();

    // Playlist state
    private List<SongDto>? _playlist;
    private List<SongDto>? _playlistSourceOrder;
    private int _currentTrackIndex;
    private bool _isShuffleEnabled;
    private int _playlistAdvanceGeneration;
    private int _playbackRequestGeneration;
    private long _playbackDiagnosticSequence;
    private long _lastMediaFailureUtcTicks;
    private int _lastMediaFailureSongId;
    private int _lastMediaFailureTrackIndex;
    private int _queueSelectionSuppressionRequestGeneration;
    private int _queueSelectionSuppressionStartIndex = -1;
    private long _queueSelectionSuppressionExpiresUtcTicks;
    private int _bufferingStallRecoveryGeneration;
    private PlaybackRuntimeState? _lastObservedPlaybackRuntimeState;

    // Map MediaItem URL -> SongDto for auto-advance detection via MediaItemChanged.
    // Queue builds mutate this on thread-pool continuations (ConfigureAwait(false)) while
    // MediaItemChanged reads it on the main thread, so all access is guarded by _urlToSongSync.
    private readonly Dictionary<string, SongDto> _urlToSong = new();
    private readonly object _urlToSongSync = new();
    private readonly ConcurrentDictionary<int, TrackCacheStatus> _cacheStatusSnapshot = new();

    public PlaybackService(
        IAuthService authService,
        IMusicService musicService,
        IPlatformPlaybackRuntime playbackRuntime,
        IAudioCacheService audioCacheService,
        IQueuePreparationService queuePreparationService,
        IPlaybackKeepAliveService playbackKeepAliveService,
        ILogger<PlaybackService> logger,
        TimeSpan? playlistAdvanceFallbackDelay = null,
        TimeSpan? positionSamplerInterval = null,
        TimeSpan? positionEventStaleThreshold = null,
        TimeSpan? transientStopConfirmationDelay = null,
        TimeSpan? subscriptionStatusRefreshInterval = null,
        TimeSpan? bufferingStallRecoveryDelay = null,
        IAnonymousFeaturedStreamStore? anonymousFeaturedStreamStore = null,
        INetworkStatusService? networkStatusService = null,
        IImageCacheService? imageCacheService = null)
    {
        _authService = authService;
        _musicService = musicService;
        _playbackRuntime = playbackRuntime;
        _audioCacheService = audioCacheService;
        _queuePreparationService = queuePreparationService;
        _playbackKeepAliveService = playbackKeepAliveService;
        _anonymousFeaturedStreamStore = anonymousFeaturedStreamStore;
        _networkStatusService = networkStatusService;
        _imageCacheService = imageCacheService;
        _logger = logger;
        _playlistAdvanceFallbackDelay = playlistAdvanceFallbackDelay ?? DefaultPlaylistAdvanceFallbackDelay;
        _positionSamplerInterval = positionSamplerInterval ?? DefaultPositionSamplerInterval;
        _positionEventStaleThreshold = positionEventStaleThreshold ?? DefaultPositionEventStaleThreshold;
        _transientStopConfirmationDelay = transientStopConfirmationDelay ?? DefaultTransientStopConfirmationDelay;
        _subscriptionStatusRefreshInterval = subscriptionStatusRefreshInterval ?? DefaultSubscriptionStatusRefreshInterval;
        _bufferingStallRecoveryDelay = bufferingStallRecoveryDelay ?? DefaultBufferingStallRecoveryDelay;
        _nextCtaThreshold = 0;

        _musicService.OnStreamCountRecorded += ApplyRecordedStreamCount;
        _playbackRuntime.StateChanged += OnPlaybackRuntimeStateChanged;
        _playbackRuntime.MediaItemChanged += OnMediaItemChanged;
        _playbackRuntime.PositionChanged += OnPositionChanged;
        _playbackRuntime.MediaItemFinished += OnMediaItemFinished;
        _playbackRuntime.MediaItemFailed += OnMediaItemFailed;
    }

    // --- Observable state ---

    private SongDto? _currentSong;
    public SongDto? CurrentSong
    {
        get => _currentSong;
        private set { _currentSong = value; RaiseStateChanged(nameof(CurrentSong)); }
    }

    private bool _isPlaying;
    public bool IsPlaying
    {
        get => _isPlaying;
        private set
        {
            if (_isPlaying == value)
            {
                return;
            }

            CancelPendingTerminalPlaybackStateConfirmation();
            var previousValue = _isPlaying;
            _isPlaying = value;
            _playbackKeepAliveService.SetPlaybackActive(value);
            UpdatePositionSampling(value);
            _logger.LogInformation(
                "Playback active state changed. PreviousIsPlaying={PreviousIsPlaying}; CurrentIsPlaying={CurrentIsPlaying}; PlaybackRuntimeState={PlaybackRuntimeState}; LastObservedState={LastObservedState}; {Snapshot}",
                previousValue,
                value,
                _playbackRuntime.State,
                _lastObservedPlaybackRuntimeState,
                CreatePlaybackSnapshot(CurrentSong, null));
            RaiseStateChanged(nameof(IsPlaying));
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Reads the raw field rather than keeping a second copy, so it cannot drift from the value
    /// the preview clamp is applied to below.
    /// </remarks>
    public TimeSpan Position
    {
        get { lock (_positionSync) { return _playbackPosition; } }
    }

    private double _playbackProgress;
    public double PlaybackProgress
    {
        get => _playbackProgress;
        private set { _playbackProgress = value; RaiseStateChanged(nameof(PlaybackProgress)); }
    }

    private string _formattedPosition = "0:00";
    public string FormattedPosition
    {
        get => _formattedPosition;
        private set { _formattedPosition = value; RaiseStateChanged(nameof(FormattedPosition)); }
    }

    private string _formattedDuration = "0:00";
    public string FormattedDuration
    {
        get => _formattedDuration;
        private set { _formattedDuration = value; RaiseStateChanged(nameof(FormattedDuration)); }
    }

    private bool _isRepeatEnabled;
    public bool IsRepeatEnabled
    {
        get => _isRepeatEnabled;
        private set { _isRepeatEnabled = value; RaiseStateChanged(nameof(IsRepeatEnabled)); }
    }

    private bool _previewLimitReached;
    public bool PreviewLimitReached
    {
        get => _previewLimitReached;
        private set { _previewLimitReached = value; RaiseStateChanged(nameof(PreviewLimitReached)); }
    }

    private PlaybackPreparationState _preparationState;
    public PlaybackPreparationState PreparationState
    {
        get => _preparationState;
        private set
        {
            if (_preparationState == value)
            {
                return;
            }

            _preparationState = value;
            RaiseStateChanged(nameof(PreparationState));
        }
    }

    private QueuePreparationResult? _lastQueuePreparationResult;
    public QueuePreparationResult? LastQueuePreparationResult
    {
        get => _lastQueuePreparationResult;
        private set
        {
            _lastQueuePreparationResult = value;
            RaiseStateChanged(nameof(LastQueuePreparationResult));
        }
    }

    public List<SongDto>? Playlist => _playlist;

    public int CurrentTrackIndex => _currentTrackIndex;

    public bool HasPlaylist => _playlist != null && _playlist.Count > 0;

    public bool IsShuffleEnabled
    {
        get => _isShuffleEnabled;
        private set { _isShuffleEnabled = value; RaiseStateChanged(nameof(IsShuffleEnabled)); }
    }

    // --- Events ---

    public event Func<Task>? ShowSubscribeCtaRequested;
    public event Action<string>? StateChanged;
    public event EventHandler<PlaybackRequestFailedEventArgs>? PlaybackRequestFailed;

    // --- Actions ---

    public void PlaySong(SongDto song)
    {
        StartPlaybackRequest(() => PlaySongAsync(song), song, "PlaySong");
    }

    // Launches a user-initiated playback request without blocking the caller, while ensuring the
    // discarded task's failures are observed: a non-cancellation exception is logged and surfaced
    // as PlaybackRequestFailed(UnexpectedError) instead of vanishing as an unobserved task fault.
    private void StartPlaybackRequest(Func<Task> operation, SongDto? song, string description)
    {
        _ = RunGuardedPlaybackRequestAsync(operation, song, description);
    }

    private async Task RunGuardedPlaybackRequestAsync(Func<Task> operation, SongDto? song, string description)
    {
        try
        {
            await operation().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Request was superseded/cancelled — expected, not a failure.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Description} failed unexpectedly. SongId={SongId}", description, song?.Id);
            PlaybackRequestFailed?.Invoke(
                this,
                new PlaybackRequestFailedEventArgs(song?.Id ?? 0, PlaybackRequestFailureReason.UnexpectedError));
        }
    }

    private async Task PlaySongAsync(SongDto song)
    {
        LogPlaybackSnapshot("PlaySong requested", song, null);

        if (CurrentSong?.Id == song.Id && IsPlaying)
        {
            // Tapping the same song that's playing — pause it
            CancelPendingPlaylistAdvance();
            CancelPendingPlaybackRequest();
            IsPlaying = false;
            ObserveMediaCommand("Playback runtime.Pause from PlaySong current-song toggle", _playbackRuntime.PauseAsync(), song, null);
            LogPlaybackSnapshot("PlaySong paused current song", song, null);
            return;
        }

        // Cancel any pending playlist-advance BEFORE starting the request and awaiting the
        // cache lookup. If an advance timer fires during the await it would bump the request
        // generation and cause this tap to be silently dropped by IsPlaybackRequestCurrent.
        CancelPendingPlaylistAdvance();

        var isSameSong = CurrentSong?.Id == song.Id;
        var requestGeneration = BeginPlaybackRequest();
        if (!TryBeginQueueBuild(requestGeneration, out var queueBuildCancellation))
        {
            return;
        }
        TrackCacheStatus cacheStatus;
        try
        {
            cacheStatus = await _audioCacheService
                .GetCacheStatusAsync(song, queueBuildCancellation.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (queueBuildCancellation.IsCancellationRequested)
        {
            return;
        }
        finally
        {
            CompleteQueueBuild(queueBuildCancellation);
        }

        if (!IsPlaybackRequestCurrent(requestGeneration))
        {
            return;
        }

        _cacheStatusSnapshot[song.Id] = cacheStatus;
        if (!CanStartRequestedSong(song, cacheStatus))
        {
            PublishUnavailableOffline(song);
            return;
        }

        // Reset stream tracking for the new song
        ResetStreamTracking(song.Id);
        ResetPlaybackState();

        CurrentSong = song;
        IsPlaying = true;
        QueueImmediateSubscriptionStatusRefreshForPlayback(TimeSpan.Zero);

        if (isSameSong)
        {
            // Same song replay (e.g., after preview limit) — seek to start and resume
            ObserveMediaCommand("Playback runtime.SeekTo start for PlaySong same-song replay", _playbackRuntime.SeekToAsync(TimeSpan.Zero), song, null);
            ObserveMediaCommand("Playback runtime.Play for PlaySong same-song replay", _playbackRuntime.PlayAsync(), song, null);
        }
        else
        {
            StartSingleSongPlayback(song, requestGeneration, cacheStatus);
        }

        LogPlaybackSnapshot("PlaySong started", song, null);
    }

    public void TogglePlayPause()
    {
        if (CurrentSong == null) return;

        CancelPendingPlaylistAdvance();
        LogPlaybackSnapshot("TogglePlayPause requested", CurrentSong, null);

        var mediaStateAtRequest = _playbackRuntime.State;
        var wasPlaying = IsPlaying && !(HasPlaylist && mediaStateAtRequest == PlaybackRuntimeState.Failed);
        if (!wasPlaying && PreviewLimitReached)
        {
            ResetPreviewPositionToStart("TogglePlayPause preview replay");
            PreviewLimitReached = false;
        }

        IsPlaying = !wasPlaying;
        if (IsPlaying)
        {
            if (HasPlaylist && mediaStateAtRequest == PlaybackRuntimeState.Failed)
            {
                _logger.LogWarning(
                    "TogglePlayPause detected failed playlist state; replaying current queue index instead of issuing raw Play. CurrentTrackIndex={CurrentTrackIndex}; {Snapshot}",
                    _currentTrackIndex,
                    CreatePlaybackSnapshot(CurrentSong, null));
                PlayTrackAtIndex(_currentTrackIndex);
                LogPlaybackSnapshot("TogglePlayPause recovered failed playlist state", CurrentSong, null);
                return;
            }

            QueueImmediateSubscriptionStatusRefreshForPlayback(_playbackPosition);
            ObserveMediaCommand("Playback runtime.Play from TogglePlayPause", _playbackRuntime.PlayAsync(), CurrentSong, null);
        }
        else
        {
            ObserveMediaCommand("Playback runtime.Pause from TogglePlayPause", _playbackRuntime.PauseAsync(), CurrentSong, null);
        }

        LogPlaybackSnapshot("TogglePlayPause completed", CurrentSong, null);
    }

    public void Stop()
    {
        CancelPendingPlaylistAdvance();
        CancelPendingPlaybackRequest();
        CancelQueuePreparation();
        LogPlaybackSnapshot("Stop requested", CurrentSong, null);
        IsPlaying = false;
        ResetPlaybackState();
        ObserveMediaCommand("Playback runtime.Pause from Stop", _playbackRuntime.PauseAsync(), CurrentSong, null);
        ObserveMediaCommand("Playback runtime.SeekTo start from Stop", _playbackRuntime.SeekToAsync(TimeSpan.Zero), CurrentSong, null);
        LogPlaybackSnapshot("Stop completed", CurrentSong, null);
    }

    public void ToggleRepeat()
    {
        if (CurrentSong == null)
        {
            return;
        }

        IsRepeatEnabled = !IsRepeatEnabled;
        _playbackRuntime.RepeatMode = HasPlaylist
            ? PlaybackRepeatMode.All
            : IsRepeatEnabled ? PlaybackRepeatMode.All : PlaybackRepeatMode.Off;
    }

    internal void UpdatePosition(TimeSpan position, TimeSpan duration)
    {
        if (ShouldIgnoreStaleHighPositionAfterTrackReset(position))
        {
            return;
        }

        if (position > TimeSpan.Zero && Volatile.Read(ref _consecutiveUnplayableTrackSkipCount) != 0)
        {
            Interlocked.Exchange(ref _consecutiveUnplayableTrackSkipCount, 0);
        }

        // Some platforms emit a final stale position event at/after the preview boundary
        // after we already paused and reset to start. Ignore it to keep UI at 0:00.
        if (PreviewLimitReached && !IsPlaying && position.TotalSeconds >= PreviewLimitSeconds)
        {
            return;
        }

        var shouldRefreshSubscriptionStatus = false;
        var shouldEnforcePreviewLimit = false;

        lock (_positionSync)
        {
            var previousPosition = _playbackPosition;
            _playbackPosition = position;
            _playbackDuration = duration;

            // Clamp position at preview limit for non-subscribers.
            // Check PreviewLimitReached too — after CheckPreviewLimit sets IsPlaying=false,
            // ShouldEnforcePreviewLimit() returns false but we still need to clamp.
            var effectivePosition = position;
            if ((ShouldEnforcePreviewLimit() || PreviewLimitReached) && position.TotalSeconds >= PreviewLimitSeconds)
            {
                effectivePosition = TimeSpan.FromSeconds(PreviewLimitSeconds);
            }

            PlaybackProgress = duration.TotalSeconds > 0
                ? effectivePosition.TotalSeconds / duration.TotalSeconds
                : 0;

            FormattedPosition = FormatDuration(effectivePosition.TotalSeconds);
            FormattedDuration = FormatDuration(duration.TotalSeconds);

            // Raised explicitly because Position has no setter to raise it - it reads the field
            // assigned above. Subscribers treat each of these as a resync anchor.
            RaiseStateChanged(nameof(Position));

            TrackStreamPlayback(position, previousPosition);
            shouldEnforcePreviewLimit = ShouldEnforcePreviewLimit() && position.TotalSeconds >= PreviewLimitSeconds;
            shouldRefreshSubscriptionStatus = ShouldRefreshSubscriptionStatusDuringPlayback();
        }

        if (shouldEnforcePreviewLimit)
        {
            EnforcePreviewLimit("UpdatePosition");
        }

        if (shouldRefreshSubscriptionStatus)
        {
            QueueSubscriptionStatusRefreshForPlayback(position);
        }
    }

    public TimeSpan GetSeekPosition(double progress)
    {
        return TimeSpan.FromSeconds(progress * _playbackDuration.TotalSeconds);
    }

    public void Seek(double progress)
    {
        var position = GetSeekPosition(progress);
        MarkExplicitSeek();
        _ = _playbackRuntime.SeekToAsync(position);
    }

    internal void OnMediaEnded()
    {
        LogPlaybackSnapshot("OnMediaEnded entered", CurrentSong, null);

        if (!HasPlaylist && IsRepeatEnabled && CurrentSong != null)
        {
            // Single-song repeat: restart
            ResetStreamTracking(CurrentSong.Id);
            PreviewLimitReached = false;
            ObserveMediaCommand("Playback runtime.SeekTo start for single-song repeat", _playbackRuntime.SeekToAsync(TimeSpan.Zero), CurrentSong, null);
            ObserveMediaCommand("Playback runtime.Play for single-song repeat", _playbackRuntime.PlayAsync(), CurrentSong, null);
            LogPlaybackSnapshot("OnMediaEnded restarted single-song repeat", CurrentSong, null);
            return;
        }

        if (!HasPlaylist)
        {
            IsPlaying = false;
            LogPlaybackSnapshot("OnMediaEnded stopped non-playlist playback", CurrentSong, null);
            return;
        }

        var finishedSongId = CurrentSong?.Id;
        if (finishedSongId == null)
        {
            _logger.LogWarning("OnMediaEnded ignored because CurrentSong is null. {Snapshot}", CreatePlaybackSnapshot(null, null));
            return;
        }

        var finishedTrackIndex = _currentTrackIndex;
        var generation = _playlistAdvanceGeneration;

        // Android background playback can occasionally stop at the end of a track
        // without raising the follow-up MediaItemChanged event. Give the native queue
        // a short grace period, then explicitly continue only if nothing advanced.
        _logger.LogInformation(
            "Scheduling playlist continuation fallback. FinishedSongId={FinishedSongId}; FinishedTrackIndex={FinishedTrackIndex}; Generation={Generation}; {Snapshot}",
            finishedSongId.Value,
            finishedTrackIndex,
            generation,
            CreatePlaybackSnapshot(CurrentSong, null));
        _ = EnsurePlaylistContinuesAsync(finishedSongId.Value, finishedTrackIndex, generation);
    }

    // --- Playlist methods ---

    public void SetPlaylist(List<SongDto> songs, int startIndex)
    {
        SetPlaylist(songs, startIndex, PlaybackQueueStartBehavior.RestartAtRequestedIndex, UnspecifiedQueueSourceDescription);
    }

    public void SetPlaylist(List<SongDto> songs, int startIndex, string queueSourceDescription)
    {
        SetPlaylist(songs, startIndex, PlaybackQueueStartBehavior.RestartAtRequestedIndex, queueSourceDescription);
    }

    public void SetPlaylist(List<SongDto> songs, int startIndex, PlaybackQueueStartBehavior startBehavior)
    {
        SetPlaylist(songs, startIndex, startBehavior, UnspecifiedQueueSourceDescription);
    }

    public void SetPlaylist(List<SongDto> songs, int startIndex, PlaybackQueueStartBehavior startBehavior, string queueSourceDescription)
    {
        var selectedSong = songs.Count > 0
            ? songs[Math.Clamp(startIndex, 0, songs.Count - 1)]
            : null;
        StartPlaybackRequest(
            () => SetPlaylistAsync(songs, startIndex, startBehavior, queueSourceDescription),
            selectedSong,
            "SetPlaylist");
    }

    private async Task SetPlaylistAsync(
        List<SongDto> songs,
        int startIndex,
        PlaybackQueueStartBehavior startBehavior,
        string queueSourceDescription)
    {
        var normalizedQueueSource = NormalizeQueueSourceDescription(queueSourceDescription);
        if (songs.Count == 0)
        {
            _logger.LogInformation(
                "SetPlaylist ignored because requested queue is empty. QueueSource={QueueSource}; SongCount=0; StartIndex={StartIndex}; StartBehavior={StartBehavior}",
                normalizedQueueSource,
                startIndex,
                startBehavior);
            return;
        }

        CancelPendingPlaylistAdvance();
        _logger.LogInformation(
            "SetPlaylist requested. QueueSource={QueueSource}; SongCount={SongCount}; StartIndex={StartIndex}; StartBehavior={StartBehavior}; QueueItems={QueueItems}; SongIds={SongIds}",
            normalizedQueueSource,
            songs.Count,
            startIndex,
            startBehavior,
            DescribeQueueItems(songs),
            DescribeSongIds(songs));

        var requestedSourceOrder = new List<SongDto>(songs);
        var currentSongIndex = CurrentSong == null
            ? -1
            : requestedSourceOrder.FindIndex(song => song.Id == CurrentSong.Id);
        var shouldPreserveCurrentSong = startBehavior == PlaybackQueueStartBehavior.PreserveCurrentSongIfPresent &&
                                        currentSongIndex >= 0;
        var selectedIndex = shouldPreserveCurrentSong
            ? currentSongIndex
            : Math.Clamp(startIndex, 0, songs.Count - 1);
        var selectedSong = requestedSourceOrder[selectedIndex];
        var requestGeneration = BeginPlaybackRequest();
        if (!TryBeginQueueBuild(requestGeneration, out var queueBuildCancellation))
        {
            return;
        }
        TrackCacheStatus selectedCacheStatus;
        try
        {
            selectedCacheStatus = await _audioCacheService
                .GetCacheStatusAsync(selectedSong, queueBuildCancellation.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (queueBuildCancellation.IsCancellationRequested)
        {
            return;
        }
        finally
        {
            CompleteQueueBuild(queueBuildCancellation);
        }

        if (!IsPlaybackRequestCurrent(requestGeneration))
        {
            return;
        }

        _cacheStatusSnapshot[selectedSong.Id] = selectedCacheStatus;
        // Only gate on offline availability when the caller actually intends to START this song.
        // Preserve-current-song calls (e.g. filter-driven SynchronizeVisibleQueue) merely reorder
        // the queue around the already-playing track, so they must not abort or emit a
        // "not downloaded" toast when that track happens to be a remote stream while offline.
        if (!shouldPreserveCurrentSong && !CanStartRequestedSong(selectedSong, selectedCacheStatus))
        {
            PublishUnavailableOffline(selectedSong);
            return;
        }

        _playlistSourceOrder = requestedSourceOrder;
        _playlist = BuildPlaybackPlaylist(_playlistSourceOrder, selectedSong.Id);
        _currentTrackIndex = ResolveRequiredPlaylistIndex(_playlist, selectedSong.Id);

        RaiseStateChanged(nameof(HasPlaylist));
        RaiseStateChanged(nameof(Playlist));
        RaiseStateChanged(nameof(CurrentTrackIndex));

        var song = _playlist[_currentTrackIndex];
        if (shouldPreserveCurrentSong)
        {
            var wasPlaying = IsPlaying;
            var currentPosition = ResolveCurrentPlaybackPosition();
            CurrentSong = song;
            if (!ShouldLimitPreviewForSong(song))
            {
                PreviewLimitReached = false;
            }

            _playbackRuntime.RepeatMode = PlaybackRepeatMode.All;
            _playbackRuntime.ShuffleMode = PlaybackShuffleMode.Off;
            ReplaceNativeQueuePreservingCurrentPlayback(
                _playlist,
                _currentTrackIndex,
                requestGeneration,
                currentPosition,
                wasPlaying);
            StartQueuePreparation(_playlist, _currentTrackIndex, QueuePreparationMode.SleepSafe);
            WarmPlaybackCacheInBackground(_playlist, _currentTrackIndex);
            LogPlaybackSnapshot("SetPlaylist preserved current playback", song, null);
            return;
        }

        ResetStreamTracking(song.Id);
        ResetPlaybackState();
        CurrentSong = song;
        IsPlaying = true;
        QueueImmediateSubscriptionStatusRefreshForPlayback(TimeSpan.Zero);

        _playbackRuntime.RepeatMode = PlaybackRepeatMode.All;
        _playbackRuntime.ShuffleMode = PlaybackShuffleMode.Off;

        BuildAndStartQueue(_currentTrackIndex, requestGeneration);
        StartQueuePreparation(_playlist, _currentTrackIndex, QueuePreparationMode.SleepSafe);
        LogPlaybackSnapshot("SetPlaylist completed", song, null);
    }

    public void ClearPlaylist()
    {
        CancelPendingPlaylistAdvance();
        CancelPendingPlaybackRequest();
        CancelQueuePreparation();
        _playlist = null;
        _playlistSourceOrder = null;
        _currentTrackIndex = 0;

        RaiseStateChanged(nameof(HasPlaylist));
        RaiseStateChanged(nameof(Playlist));
        RaiseStateChanged(nameof(CurrentTrackIndex));
    }

    public void PlayNext()
    {
        if (!HasPlaylist || _playlist == null) return;

        var nextIndex = ResolveSequentialNextTrackIndex(_currentTrackIndex);
        if (!nextIndex.HasValue)
        {
            LogPlaybackSnapshot("PlayNext ignored because there is no next track", CurrentSong, null);
            return;
        }

        ClearQueueSelectionSuppression();
        LogPlaybackSnapshot("PlayNext requested", CurrentSong, null);
        PlayTrackAtIndex(nextIndex.Value);
    }

    public void PlayPrevious()
    {
        if (!HasPlaylist || _playlist == null) return;

        var previousIndex = ResolveSequentialPreviousTrackIndex(_currentTrackIndex);
        if (!previousIndex.HasValue)
        {
            LogPlaybackSnapshot("PlayPrevious ignored because there is no previous track", CurrentSong, null);
            return;
        }

        ClearQueueSelectionSuppression();
        LogPlaybackSnapshot("PlayPrevious requested", CurrentSong, null);
        PlayTrackAtIndex(previousIndex.Value);
    }

    public void PlayTrackAtIndex(int index)
    {
        if (_playlist == null || index < 0 || index >= _playlist.Count)
            return;

        CancelPendingPlaylistAdvance();
        var requestGeneration = BeginPlaybackRequest();
        var mediaStateAtRequest = _playbackRuntime.State;
        var nativeQueueMismatch = ShouldForceQueueReloadForNativeQueueMismatch(index);
        var shouldForceQueueReload = mediaStateAtRequest == PlaybackRuntimeState.Failed ||
                                     nativeQueueMismatch ||
                                     ShouldForceQueueReloadForRequestedTrack(index);
        if (mediaStateAtRequest == PlaybackRuntimeState.Failed)
        {
            _logger.LogWarning(
                "PlayTrackAtIndex detected failed native playback state while switching tracks; forcing queue rebuild for recovery. Index={Index}; RequestGeneration={RequestGeneration}; {Snapshot}",
                index,
                requestGeneration,
                CreatePlaybackSnapshot(CurrentSong, null));
        }

        _logger.LogInformation(
            "PlayTrackAtIndex requested. Index={Index}; ForceQueueReload={ForceQueueReload}; MediaStateAtRequest={MediaStateAtRequest}; RequestGeneration={RequestGeneration}; {Snapshot}",
            index,
            shouldForceQueueReload,
            mediaStateAtRequest,
            requestGeneration,
            CreatePlaybackSnapshot(CurrentSong, null));

        _currentTrackIndex = index;
        RaiseStateChanged(nameof(CurrentTrackIndex));

        var song = _playlist[index];
        ResetStreamTracking(song.Id);
        ResetPlaybackState();
        CurrentSong = song;
        IsPlaying = true;
        QueueImmediateSubscriptionStatusRefreshForPlayback(TimeSpan.Zero);

        if (shouldForceQueueReload)
        {
            _logger.LogWarning(
                "PlayTrackAtIndex forcing queue rebuild before selecting requested track. Index={Index}; NativeQueueMismatch={NativeQueueMismatch}; {Snapshot}",
                index,
                nativeQueueMismatch,
                CreatePlaybackSnapshot(song, null));
            BuildAndStartQueue(index, requestGeneration);
        }
        else
        {
            ObserveMediaCommand($"Playback runtime.PlayQueueItemAsync({index})", _playbackRuntime.PlayQueueItemAsync(index), DescribeBooleanResult, song, null);
            StartQueuePreparation(_playlist, index, QueuePreparationMode.SleepSafe);
            WarmPlaybackCacheInBackground(_playlist, index);
        }

        if (_playbackRuntime.State == PlaybackRuntimeState.Failed)
        {
            _logger.LogWarning(
                "PlayTrackAtIndex completed but native state is still failed; awaiting subsequent recovery events. Index={Index}; RequestGeneration={RequestGeneration}; {Snapshot}",
                index,
                requestGeneration,
                CreatePlaybackSnapshot(song, null));
        }

        LogPlaybackSnapshot("PlayTrackAtIndex completed", song, null);
    }

    public void ToggleShuffle()
    {
        if (!HasPlaylist || CurrentSong == null)
        {
            return;
        }

        var currentSongId = CurrentSong?.Id;
        var shouldRebuildPlaylist = _playlistSourceOrder != null && currentSongId.HasValue;
        var wasPlaying = IsPlaying;

        IsShuffleEnabled = !_isShuffleEnabled;

        if (!shouldRebuildPlaylist)
        {
            _playbackRuntime.ShuffleMode = _isShuffleEnabled ? PlaybackShuffleMode.All : PlaybackShuffleMode.Off;
            return;
        }

        _playlist = BuildPlaybackPlaylist(_playlistSourceOrder!, currentSongId!.Value);
        _currentTrackIndex = ResolveRequiredPlaylistIndex(_playlist, currentSongId.Value);

        RaiseStateChanged(nameof(Playlist));
        RaiseStateChanged(nameof(CurrentTrackIndex));

        if (CurrentSong?.Id != currentSongId.Value)
        {
            CurrentSong = _playlist[_currentTrackIndex];
        }

        _playbackRuntime.ShuffleMode = PlaybackShuffleMode.Off;

        _logger.LogInformation(
            "ToggleShuffle rebuilt active playlist. WasPlaying={WasPlaying}; CurrentSongId={CurrentSongId}; {Snapshot}",
            wasPlaying,
            currentSongId.Value,
            CreatePlaybackSnapshot(CurrentSong, null));

        CancelPendingPlaylistAdvance();
        var requestGeneration = BeginPlaybackRequest();
        BuildAndStartQueue(_currentTrackIndex, requestGeneration, pauseAfterQueueSelection: !wasPlaying);
    }

    public void SetStreamQualifyingSeconds(int seconds)
    {
        _streamQualifyingSeconds = seconds;
    }

    private int GetCurrentStreamQualifyingSeconds()
    {
        if (CurrentSong?.StreamQualifyingSeconds > 0)
        {
            return CurrentSong.StreamQualifyingSeconds;
        }

        return _streamQualifyingSeconds;
    }

    public void HandleSubscriptionActivated()
    {
        if (!_authService.HasActiveSubscription)
            return;

        var shouldResumePlayback = PreviewLimitReached && CurrentSong != null && !IsPlaying;
        PreviewLimitReached = false;

        if (shouldResumePlayback)
        {
            IsPlaying = true;
            _ = _playbackRuntime.PlayAsync();
        }
    }

    public string FormatDuration(double? seconds)
    {
        if (seconds == null || double.IsNaN(seconds.Value) || double.IsInfinity(seconds.Value))
            return "0:00";
        var ts = TimeSpan.FromSeconds(seconds.Value);
        return ts.TotalHours >= 1
            ? ts.ToString(@"h\:mm\:ss")
            : ts.ToString(@"m\:ss");
    }

    // --- Stream tracking ---

    private void ResetStreamTracking(int songId)
    {
        lock (_positionSync)
        {
            _streamTrackingSongId = songId;
            _continuousPlaybackSeconds = 0;
            _streamRecordedForCurrentSong = false;
            _skipNextStreamPositionSample = false;
        }
    }

    private void MarkExplicitSeek()
    {
        lock (_positionSync)
        {
            if (_streamRecordedForCurrentSong)
            {
                return;
            }

            _continuousPlaybackSeconds = 0;
            _skipNextStreamPositionSample = true;
        }
    }

    private void TrackStreamPlayback(TimeSpan position, TimeSpan previousPosition)
    {
        if (CurrentSong == null || !IsPlaying || _streamRecordedForCurrentSong)
            return;

        // Don't count streams for creators listening to their own songs
        if (_authService.IsCreator && CurrentSong.CreatorUserId == _authService.UserId)
        {
            _streamRecordedForCurrentSong = true;
            return;
        }

        if (IsAnonymousFeaturedStreamAlreadyRecorded(CurrentSong))
        {
            _streamRecordedForCurrentSong = true;
            return;
        }

        if (CurrentSong.Id != _streamTrackingSongId)
        {
            ResetStreamTracking(CurrentSong.Id);
        }

        if (_skipNextStreamPositionSample)
        {
            _skipNextStreamPositionSample = false;
            return;
        }

        var elapsed = position.TotalSeconds - previousPosition.TotalSeconds;
        if (elapsed < 0)
        {
            MarkExplicitSeek();
            _skipNextStreamPositionSample = false;
            return;
        }

        if (elapsed > 0)
        {
            _continuousPlaybackSeconds += elapsed;
        }

        if (_continuousPlaybackSeconds >= GetCurrentStreamQualifyingSeconds())
        {
            _streamRecordedForCurrentSong = true;
            MarkAnonymousFeaturedStreamRecorded(CurrentSong);
            _ = RecordQualifiedStreamAsync(CurrentSong.Id);
        }
    }

    private bool IsAnonymousFeaturedStreamAlreadyRecorded(SongDto song)
    {
        return IsAnonymousFeaturedStream(song) &&
               _anonymousFeaturedStreamStore?.HasRecordedFeaturedStream(song.Id) == true;
    }

    private void MarkAnonymousFeaturedStreamRecorded(SongDto song)
    {
        if (IsAnonymousFeaturedStream(song))
        {
            _anonymousFeaturedStreamStore?.MarkFeaturedStreamRecorded(song.Id);
        }
    }

    private bool IsAnonymousFeaturedStream(SongDto song)
    {
        return song.DisplayOnHomePage &&
               !_authService.IsLoggedIn &&
               !_authService.HasActiveSubscription;
    }

    private async Task RecordQualifiedStreamAsync(int songMetadataId)
    {
        var newCount = await _musicService.RecordStreamAsync(songMetadataId).ConfigureAwait(false);
        if (!newCount.HasValue)
        {
            return;
        }

        ApplyRecordedStreamCount(songMetadataId, newCount.Value);
    }

    private void ApplyRecordedStreamCount(int songMetadataId, int newCount)
    {
        if (CurrentSong?.Id == songMetadataId)
        {
            CurrentSong.StreamCount = newCount;
        }

        ApplyRecordedStreamCount(_playlist, songMetadataId, newCount);

        if (!ReferenceEquals(_playlistSourceOrder, _playlist))
        {
            ApplyRecordedStreamCount(_playlistSourceOrder, songMetadataId, newCount);
        }
    }

    private static void ApplyRecordedStreamCount(IEnumerable<SongDto>? songs, int songMetadataId, int newCount)
    {
        if (songs == null)
        {
            return;
        }

        foreach (var song in songs)
        {
            if (song.Id == songMetadataId)
            {
                song.StreamCount = newCount;
            }
        }
    }

    private void UpdatePositionSampling(bool isPlaying)
    {
        if (isPlaying)
        {
            MarkPositionChangedObserved();
            MarkPositionSamplerTickObserved();
            EnsurePositionSamplerRunning();
            return;
        }

        StopPositionSampler();
        Interlocked.Exchange(ref _lastPositionSamplerTickUtcTicks, 0);
    }

    private void EnsurePositionSamplerRunning()
    {
        if (_positionSamplerInterval <= TimeSpan.Zero)
        {
            return;
        }

        var existing = Volatile.Read(ref _positionSamplerCancellation);
        if (existing != null && !existing.IsCancellationRequested)
        {
            return;
        }

        var cancellationSource = new CancellationTokenSource();
        var previous = Interlocked.CompareExchange(ref _positionSamplerCancellation, cancellationSource, null);
        if (previous != null)
        {
            cancellationSource.Dispose();
            return;
        }

        _ = RunPositionSamplerAsync(cancellationSource);
    }

    private void StopPositionSampler()
    {
        var cancellationSource = Interlocked.Exchange(ref _positionSamplerCancellation, null);
        cancellationSource?.Cancel();
    }

    private async Task RunPositionSamplerAsync(CancellationTokenSource cancellationSource)
    {
        try
        {
            using var timer = new PeriodicTimer(_positionSamplerInterval);
            while (await timer.WaitForNextTickAsync(cancellationSource.Token).ConfigureAwait(false))
            {
                LogPositionSamplerDelayedTickIfNeeded();

                if (!ShouldUseFallbackPositionSampling())
                {
                    continue;
                }

                UpdatePosition(_playbackRuntime.Position, _playbackRuntime.Duration);
            }
        }
        catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fallback playback position sampler failed.");
        }
        finally
        {
            Interlocked.CompareExchange(ref _positionSamplerCancellation, null, cancellationSource);
            cancellationSource.Dispose();
        }
    }

    private void LogPositionSamplerDelayedTickIfNeeded()
    {
        var nowTicks = DateTime.UtcNow.Ticks;
        var previousTickTicks = Interlocked.Exchange(ref _lastPositionSamplerTickUtcTicks, nowTicks);
        if (previousTickTicks <= 0 || !IsPlaying || CurrentSong == null)
        {
            return;
        }

        var elapsedSincePreviousTick = TimeSpan.FromTicks(Math.Max(0, nowTicks - previousTickTicks));
        var delayedTickThreshold = _positionSamplerInterval + PositionSamplerDelayedTickLogThreshold;
        if (elapsedSincePreviousTick < delayedTickThreshold)
        {
            return;
        }

        _logger.LogWarning(
            "Playback diagnostic heartbeat delayed. Timer=PositionSampler; ElapsedSincePreviousTick={ElapsedSincePreviousTick}; ExpectedInterval={ExpectedInterval}; Position={Position}; Duration={Duration}; PlaybackRuntimeState={PlaybackRuntimeState}; LastObservedState={LastObservedState}; {Snapshot}",
            elapsedSincePreviousTick,
            _positionSamplerInterval,
            _playbackRuntime.Position,
            _playbackRuntime.Duration,
            _playbackRuntime.State,
            _lastObservedPlaybackRuntimeState,
            CreatePlaybackSnapshot(CurrentSong, null));
    }

    private bool ShouldUseFallbackPositionSampling()
    {
        if (!IsPlaying || CurrentSong == null)
        {
            return false;
        }

        var lastObservedTicks = Volatile.Read(ref _lastPositionChangedUtcTicks);
        if (lastObservedTicks <= 0)
        {
            return true;
        }

        return TimeSpan.FromTicks(Math.Max(0, DateTime.UtcNow.Ticks - lastObservedTicks)) >= _positionEventStaleThreshold;
    }

    private void MarkPositionChangedObserved()
    {
        Interlocked.Exchange(ref _lastPositionChangedUtcTicks, DateTime.UtcNow.Ticks);
    }

    private void MarkPositionSamplerTickObserved()
    {
        Interlocked.Exchange(ref _lastPositionSamplerTickUtcTicks, DateTime.UtcNow.Ticks);
    }

    private void ScheduleTerminalPlaybackStateConfirmation(PlaybackRuntimeState state)
    {
        if (_transientStopConfirmationDelay <= TimeSpan.Zero || CurrentSong == null)
        {
            if (IsPlaying)
            {
                IsPlaying = false;
            }

            return;
        }

        CancelPendingTerminalPlaybackStateConfirmation();

        var cancellationSource = new CancellationTokenSource();
        var previous = Interlocked.CompareExchange(ref _terminalStateConfirmationCancellation, cancellationSource, null);
        if (previous != null)
        {
            cancellationSource.Dispose();
            return;
        }

        var currentSongId = CurrentSong.Id;
        var observedPosition = _playbackRuntime.Position;
        _logger.LogInformation(
            "Deferring Playback runtime terminal state. State={State}; SongId={SongId}; ObservedPosition={ObservedPosition}; DelayMs={DelayMs}; {Snapshot}",
            state,
            currentSongId,
            observedPosition,
            _transientStopConfirmationDelay.TotalMilliseconds,
            CreatePlaybackSnapshot(CurrentSong, null));

        _ = ConfirmTerminalPlaybackStateAsync(state, currentSongId, observedPosition, cancellationSource);
    }

    private void CancelPendingTerminalPlaybackStateConfirmation()
    {
        var cancellationSource = Interlocked.Exchange(ref _terminalStateConfirmationCancellation, null);
        cancellationSource?.Cancel();
    }

    private async Task ConfirmTerminalPlaybackStateAsync(
        PlaybackRuntimeState expectedState,
        int songId,
        TimeSpan observedPosition,
        CancellationTokenSource cancellationSource)
    {
        try
        {
            await Task.Delay(_transientStopConfirmationDelay, cancellationSource.Token).ConfigureAwait(false);

            if (CurrentSong?.Id != songId)
            {
                return;
            }

            var currentState = _playbackRuntime.State;
            if (currentState != PlaybackRuntimeState.Paused && currentState != PlaybackRuntimeState.Stopped)
            {
                return;
            }

            var currentPosition = _playbackRuntime.Position;
            if (currentPosition > observedPosition)
            {
                _logger.LogInformation(
                    "Ignoring transient Playback runtime terminal state because playback position advanced. ExpectedState={ExpectedState}; CurrentState={CurrentState}; SongId={SongId}; ObservedPosition={ObservedPosition}; CurrentPosition={CurrentPosition}; {Snapshot}",
                    expectedState,
                    currentState,
                    songId,
                    observedPosition,
                    currentPosition,
                    CreatePlaybackSnapshot(CurrentSong, null));
                return;
            }

            if (TryRecoverConfirmedTerminalPlaylistState(currentState, songId, observedPosition, currentPosition))
            {
                return;
            }

            if (IsPlaying)
            {
                _logger.LogInformation(
                    "Confirmed Playback runtime terminal state. ExpectedState={ExpectedState}; CurrentState={CurrentState}; SongId={SongId}; ObservedPosition={ObservedPosition}; CurrentPosition={CurrentPosition}; {Snapshot}",
                    expectedState,
                    currentState,
                    songId,
                    observedPosition,
                    currentPosition,
                    CreatePlaybackSnapshot(CurrentSong, null));
                IsPlaying = false;
            }
        }
        catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
        {
        }
        finally
        {
            Interlocked.CompareExchange(ref _terminalStateConfirmationCancellation, null, cancellationSource);
            cancellationSource.Dispose();
        }
    }

    private bool TryRecoverConfirmedTerminalPlaylistState(
        PlaybackRuntimeState currentState,
        int songId,
        TimeSpan observedPosition,
        TimeSpan currentPosition)
    {
        if (!IsPlaying || !HasPlaylist || _playlist == null || CurrentSong?.Id != songId)
        {
            return false;
        }

        if (currentState == PlaybackRuntimeState.Stopped &&
            !ResolveSequentialNextTrackIndex(_currentTrackIndex).HasValue &&
            !IsRepeatEnabled)
        {
            return false;
        }

        var recoveryIndex = ResolveTerminalPlaybackRecoveryIndex(currentState);
        if (ShouldStopTerminalZeroPositionRecovery(currentState, songId, observedPosition, currentPosition))
        {
            if (TryAdvancePastUnplayableTrack(songId, _currentTrackIndex, "terminal zero-position recovery exhausted"))
            {
                return true;
            }

            _logger.LogError(
                "Terminal {State} recovery stopped after repeated zero-position failures. SongId={SongId}; RecoveryIndex={RecoveryIndex}; ObservedPosition={ObservedPosition}; CurrentPosition={CurrentPosition}; {Snapshot}",
                currentState,
                songId,
                recoveryIndex,
                observedPosition,
                currentPosition,
                CreatePlaybackSnapshot(CurrentSong, null));
            PreparationState = PlaybackPreparationState.Error;
            IsPlaying = false;
            return true;
        }

        _logger.LogWarning(
            "Confirmed terminal {State} state during playlist playback; attempting recovery instead of stopping. SongId={SongId}; RecoveryIndex={RecoveryIndex}; ObservedPosition={ObservedPosition}; CurrentPosition={CurrentPosition}; {Snapshot}",
            currentState,
            songId,
            recoveryIndex,
            observedPosition,
            currentPosition,
            CreatePlaybackSnapshot(CurrentSong, null));
        PlayTrackAtIndexWithQueueReload(recoveryIndex, "terminal playback state recovery");
        return true;
    }

    private bool ShouldStopTerminalZeroPositionRecovery(
        PlaybackRuntimeState currentState,
        int songId,
        TimeSpan observedPosition,
        TimeSpan currentPosition)
    {
        if (currentState != PlaybackRuntimeState.Stopped ||
            observedPosition > TimeSpan.Zero ||
            currentPosition > TimeSpan.Zero)
        {
            return false;
        }

        var nowTicks = DateTime.UtcNow.Ticks;
        if (_terminalZeroPositionRecoverySongId != songId ||
            nowTicks > Volatile.Read(ref _terminalZeroPositionRecoveryWindowExpiresUtcTicks))
        {
            _terminalZeroPositionRecoverySongId = songId;
            Interlocked.Exchange(ref _terminalZeroPositionRecoveryAttemptCount, 0);
            Interlocked.Exchange(
                ref _terminalZeroPositionRecoveryWindowExpiresUtcTicks,
                nowTicks + TerminalZeroPositionRecoveryWindow.Ticks);
        }

        return Interlocked.Increment(ref _terminalZeroPositionRecoveryAttemptCount) > MaxTerminalZeroPositionRecoveryAttempts;
    }

    /// <summary>
    /// Bounds how often a failing track may be re-played from its cached copy. A cached entry
    /// that keeps failing is unplayable content (e.g. a corrupt download), and endless replays
    /// would starve the advance-to-next-track recovery path.
    /// </summary>
    private bool CachedFailureRecoveryAttemptsExhausted(int songId)
    {
        var nowTicks = DateTime.UtcNow.Ticks;
        if (_cachedFailureRecoverySongId != songId ||
            nowTicks > Volatile.Read(ref _cachedFailureRecoveryWindowExpiresUtcTicks))
        {
            _cachedFailureRecoverySongId = songId;
            Interlocked.Exchange(ref _cachedFailureRecoveryAttemptCount, 0);
            Interlocked.Exchange(
                ref _cachedFailureRecoveryWindowExpiresUtcTicks,
                nowTicks + CachedFailureRecoveryWindow.Ticks);
        }

        return Interlocked.Increment(ref _cachedFailureRecoveryAttemptCount) > MaxCachedFailureRecoveryAttemptsPerSong;
    }

    private int ResolveTerminalPlaybackRecoveryIndex(PlaybackRuntimeState currentState)
    {
        var nativeCurrentIndex = TryResolveNativeQueueIndex();
        if (!nativeCurrentIndex.HasValue ||
            _playlist == null ||
            nativeCurrentIndex.Value < 0 ||
            nativeCurrentIndex.Value >= _playlist.Count)
        {
            return _currentTrackIndex;
        }

        var resolvedIndex = nativeCurrentIndex.Value;
        if (resolvedIndex == _currentTrackIndex)
        {
            return resolvedIndex;
        }

        var nextExpectedIndex = ResolveSequentialNextTrackIndex(_currentTrackIndex);
        if (nextExpectedIndex == resolvedIndex)
        {
            return resolvedIndex;
        }

        if (resolvedIndex > _currentTrackIndex)
        {
            return resolvedIndex;
        }

        _logger.LogWarning(
            "Ignoring backward native terminal recovery index. State={State}; NativeCurrentIndex={NativeCurrentIndex}; CurrentTrackIndex={CurrentTrackIndex}; {Snapshot}",
            currentState,
            resolvedIndex,
            _currentTrackIndex,
            CreatePlaybackSnapshot(CurrentSong, null));
        return _currentTrackIndex;
    }

    // --- Preview limit ---

    private bool ShouldEnforcePreviewLimit()
    {
        if (CurrentSong == null || !IsPlaying)
            return false;
        return ShouldLimitPreviewForSong(CurrentSong);
    }

    private bool ShouldLimitPreviewForSong(SongDto song)
        => PreviewAccessPolicy.ShouldLimitPreview(_authService, song);

    private void CheckPreviewLimit(TimeSpan position)
    {
        if (!ShouldEnforcePreviewLimit())
            return;

        if (position.TotalSeconds >= PreviewLimitSeconds)
        {
            EnforcePreviewLimit("CheckPreviewLimit");
        }
    }

    private void EnforcePreviewLimit(string reason)
    {
        if (CurrentSong == null || PreviewLimitReached)
        {
            return;
        }

        _logger.LogInformation(
            "Enforcing preview limit. Reason={Reason}; HasActiveSubscription={HasActiveSubscription}; SubscriptionStatus={SubscriptionStatus}; SubscriptionEndDate={SubscriptionEndDate}; {Snapshot}",
            reason,
            _authService.HasActiveSubscription,
            _authService.SubscriptionStatus,
            _authService.SubscriptionEndDate,
            CreatePlaybackSnapshot(CurrentSong, null));

        IsPlaying = false;
        PreviewLimitReached = true;
        ClampPreviewPositionDisplay();
        ObserveMediaCommand($"Playback runtime.Pause from {reason}", _playbackRuntime.PauseAsync(), CurrentSong, null);
        _previewEndCount++;

        if (_previewEndCount >= _nextCtaThreshold)
        {
            _nextCtaThreshold = _previewEndCount + _random.Next(MinPreviewInterval, MaxPreviewIntervalExclusive);
            RequestSubscribeCta();
        }

        ContinueAfterPreviewLimit(reason);
    }

    private void RequestSubscribeCta()
    {
        if (Interlocked.CompareExchange(ref _subscribeCtaRequestInProgress, 1, 0) != 0)
        {
            _logger.LogInformation("Subscribe CTA request suppressed because a previous request is still active. {Snapshot}", CreatePlaybackSnapshot(CurrentSong, null));
            return;
        }

        _ = InvokeSubscribeCtaAsync();
    }

    private async Task InvokeSubscribeCtaAsync()
    {
        try
        {
            var handlers = ShowSubscribeCtaRequested?
                .GetInvocationList()
                .OfType<Func<Task>>()
                .ToArray();

            if (handlers == null || handlers.Length == 0)
            {
                return;
            }

            _logger.LogInformation("Invoking subscribe CTA handler. HandlerCount={HandlerCount}; {Snapshot}", handlers.Length, CreatePlaybackSnapshot(CurrentSong, null));
            await handlers[^1]().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Subscribe CTA handler failed.");
        }
        finally
        {
            Interlocked.Exchange(ref _subscribeCtaRequestInProgress, 0);
        }
    }

    private void ContinueAfterPreviewLimit(string reason)
    {
        if (CurrentSong == null)
        {
            return;
        }

        if (!HasPlaylist)
        {
            ResetPreviewPositionToStart($"{reason} non-playlist stop");
            return;
        }

        var finishedSongId = CurrentSong.Id;
        var finishedTrackIndex = _currentTrackIndex;
        var generation = _playlistAdvanceGeneration;
        _ = ContinuePlaylistAfterPreviewLimitAsync(finishedSongId, finishedTrackIndex, generation);
    }

    private async Task ContinuePlaylistAfterPreviewLimitAsync(int finishedSongId, int finishedTrackIndex, int generation)
    {
        if (!HasPlaylist || _playlist == null)
        {
            ResetPreviewPositionToStart("preview-limit playlist continuation fallback");
            return;
        }

        if (generation != _playlistAdvanceGeneration || CurrentSong?.Id != finishedSongId || _currentTrackIndex != finishedTrackIndex)
        {
            return;
        }

        if (_isShuffleEnabled)
        {
            var advanced = await _playbackRuntime.PlayNextAsync().ConfigureAwait(false);
            if (advanced)
            {
                _logger.LogInformation(
                    "Preview limit advanced shuffled queue via Playback runtime.PlayNext. FinishedSongId={FinishedSongId}; FinishedTrackIndex={FinishedTrackIndex}; {Snapshot}",
                    finishedSongId,
                    finishedTrackIndex,
                    CreatePlaybackSnapshot(CurrentSong, null));

                _ = EnsurePlaylistContinuesAsync(finishedSongId, finishedTrackIndex, generation, resetPositionWhenStopped: true);
                return;
            }

            _logger.LogInformation(
                "Preview limit shuffle PlayNext returned false; falling back to app-level next index. FinishedSongId={FinishedSongId}; FinishedTrackIndex={FinishedTrackIndex}; {Snapshot}",
                finishedSongId,
                finishedTrackIndex,
                CreatePlaybackSnapshot(CurrentSong, null));
        }

        var nextIndex = ResolveSequentialNextTrackIndex(finishedTrackIndex);
        if (nextIndex.HasValue)
        {
            PlayTrackAtIndex(nextIndex.Value);
            return;
        }

        IsPlaying = false;
        ResetPreviewPositionToStart("preview-limit playlist end");
        _logger.LogInformation(
            "Preview limit reached end of queue; stopped playback. FinishedSongId={FinishedSongId}; FinishedTrackIndex={FinishedTrackIndex}; {Snapshot}",
            finishedSongId,
            finishedTrackIndex,
            CreatePlaybackSnapshot(CurrentSong, null));
    }

    private void ResetPreviewPositionToStart(string reason)
    {
        if (CurrentSong == null)
        {
            return;
        }

        lock (_positionSync)
        {
            _playbackPosition = TimeSpan.Zero;
            PlaybackProgress = _playbackDuration.TotalSeconds > 0
                ? 0
                : 0;
            FormattedPosition = "0:00";
        }

        ObserveMediaCommand($"Playback runtime.SeekTo start from {reason}", _playbackRuntime.SeekToAsync(TimeSpan.Zero), CurrentSong, null);
    }

    private void ClampPreviewPositionDisplay()
    {
        if (_playbackPosition.TotalSeconds < PreviewLimitSeconds)
        {
            return;
        }

        var previewPosition = TimeSpan.FromSeconds(PreviewLimitSeconds);
        PlaybackProgress = _playbackDuration.TotalSeconds > 0
            ? previewPosition.TotalSeconds / _playbackDuration.TotalSeconds
            : 0;
        FormattedPosition = FormatDuration(previewPosition.TotalSeconds);
    }

    private bool ShouldRefreshSubscriptionStatusDuringPlayback(bool force = false)
    {
        if (_subscriptionStatusRefreshInterval <= TimeSpan.Zero || CurrentSong == null || !IsPlaying)
        {
            return false;
        }

        if (!_authService.IsLoggedIn || !_authService.HasActiveSubscription)
        {
            return false;
        }

        if (_authService.IsCreator && CurrentSong.CreatorUserId == _authService.UserId)
        {
            return false;
        }

        if (force)
        {
            return true;
        }

        var lastRefreshTicks = Volatile.Read(ref _lastSubscriptionStatusRefreshUtcTicks);
        var nowTicks = DateTime.UtcNow.Ticks;
        return lastRefreshTicks <= 0
            || TimeSpan.FromTicks(Math.Max(0, nowTicks - lastRefreshTicks)) >= _subscriptionStatusRefreshInterval;
    }

    private void QueueImmediateSubscriptionStatusRefreshForPlayback(TimeSpan observedPosition)
    {
        if (ShouldRefreshSubscriptionStatusDuringPlayback(force: true))
        {
            QueueSubscriptionStatusRefreshForPlayback(observedPosition);
        }
    }

    private void QueueSubscriptionStatusRefreshForPlayback(TimeSpan observedPosition)
    {
        if (Interlocked.CompareExchange(ref _subscriptionStatusRefreshInProgress, 1, 0) != 0)
        {
            return;
        }

        Interlocked.Exchange(ref _lastSubscriptionStatusRefreshUtcTicks, DateTime.UtcNow.Ticks);
        _ = RefreshSubscriptionStatusForPlaybackAsync(observedPosition);
    }

    private async Task RefreshSubscriptionStatusForPlaybackAsync(TimeSpan observedPosition)
    {
        try
        {
            var previousHasActiveSubscription = _authService.HasActiveSubscription;
            var previousStatus = _authService.SubscriptionStatus;
            var previousEndDate = _authService.SubscriptionEndDate;

            await _authService.RefreshUserStatusAsync().ConfigureAwait(false);

            _logger.LogInformation(
                "Playback subscription refresh completed. PreviousHasActiveSubscription={PreviousHasActiveSubscription}; CurrentHasActiveSubscription={CurrentHasActiveSubscription}; PreviousStatus={PreviousStatus}; CurrentStatus={CurrentStatus}; PreviousEndDate={PreviousEndDate}; CurrentEndDate={CurrentEndDate}; ObservedPosition={ObservedPosition}; {Snapshot}",
                previousHasActiveSubscription,
                _authService.HasActiveSubscription,
                previousStatus,
                _authService.SubscriptionStatus,
                previousEndDate,
                _authService.SubscriptionEndDate,
                observedPosition,
                CreatePlaybackSnapshot(CurrentSong, null));

            if (ShouldEnforcePreviewLimit() && _playbackPosition.TotalSeconds >= PreviewLimitSeconds)
            {
                EnforcePreviewLimit("SubscriptionStatusRefresh");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to refresh subscription status during playback.");
        }
        finally
        {
            Interlocked.Exchange(ref _subscriptionStatusRefreshInProgress, 0);
        }
    }

    // --- platform playback runtime event handlers ---

    private void OnPlaybackRuntimeStateChanged(object? sender, PlaybackRuntimeStateChangedEventArgs e)
    {
        var previousObservedState = _lastObservedPlaybackRuntimeState;
        var previousIsPlaying = IsPlaying;

        _logger.LogInformation(
            "Playback runtime state change received. PreviousObservedState={PreviousObservedState}; NewState={State}; Reason={Reason}; PreviousIsPlaying={PreviousIsPlaying}; {Snapshot}",
            previousObservedState,
            e.State,
            e.Reason,
            previousIsPlaying,
            CreatePlaybackSnapshot(CurrentSong, null));

        switch (e.State)
        {
            case PlaybackRuntimeState.Playing:
                CancelPendingBufferingStallRecovery();
                CancelPendingTerminalPlaybackStateConfirmation();
                ClearUserRequestedStopCleanupSuppression();
                if (!IsPlaying) IsPlaying = true;
                break;
            case PlaybackRuntimeState.Buffering:
                CancelPendingTerminalPlaybackStateConfirmation();
                ScheduleBufferingStallRecovery("media manager buffering state");
                break;
            case PlaybackRuntimeState.Paused:
            case PlaybackRuntimeState.Stopped:
                CancelPendingBufferingStallRecovery();
                if (e.IsUserRequest)
                {
                    ApplyUserRequestedTerminalPlaybackState(e.State);
                    break;
                }

                if (IsPlaying && CurrentSong != null)
                {
                    ScheduleTerminalPlaybackStateConfirmation(e.State);
                }
                else if (IsPlaying)
                {
                    IsPlaying = false;
                }
                break;
            case PlaybackRuntimeState.Failed:
                CancelPendingBufferingStallRecovery();
                CancelPendingTerminalPlaybackStateConfirmation();
                if (ShouldKeepPlaybackActiveDuringFailedState())
                {
                    var failedStateSong = CurrentSong!;
                    if (_lastMediaFailureSongId == failedStateSong.Id &&
                        _lastMediaFailureTrackIndex == _currentTrackIndex &&
            TryRecoverFromMediaItemFailure(failedStateSong.Id, _currentTrackIndex, _playlistAdvanceGeneration, "media manager failed state after media item failure"))
                    {
                        break;
                    }

                    _logger.LogWarning(
                        "Playback runtime failed during recoverable playlist playback; keeping playback active while recovery advances. {Snapshot}",
                        CreatePlaybackSnapshot(CurrentSong, null));
                    ScheduleFailedStateRecovery(failedStateSong.Id, _currentTrackIndex, "media manager failed state");
                }
                else
                {
                    IsPlaying = false;
                }
                break;
        }

        _lastObservedPlaybackRuntimeState = e.State;
        _logger.LogInformation(
            "Playback runtime state change applied. PreviousObservedState={PreviousObservedState}; NewState={State}; Reason={Reason}; PreviousIsPlaying={PreviousIsPlaying}; CurrentIsPlaying={CurrentIsPlaying}; {Snapshot}",
            previousObservedState,
            e.State,
            e.Reason,
            previousIsPlaying,
            IsPlaying,
            CreatePlaybackSnapshot(CurrentSong, null));
    }

    private void ApplyUserRequestedTerminalPlaybackState(PlaybackRuntimeState state)
    {
        CancelPendingPlaylistAdvance();
        CancelPendingPlaybackRequest();
        CancelPendingTerminalPlaybackStateConfirmation();

        if (IsPlaying)
        {
            _logger.LogInformation(
                "Playback runtime terminal state was requested by user/media controls; recovery will not restart playback. State={State}; {Snapshot}",
                state,
                CreatePlaybackSnapshot(CurrentSong, null));
            IsPlaying = false;
        }

        if (state == PlaybackRuntimeState.Stopped && CurrentSong != null)
        {
            CancelQueuePreparation();
            if (TryBeginUserRequestedStopCleanup())
            {
                ObserveMediaCommand(
                    "Playback runtime.Stop from user-requested terminal state",
                    StopRuntimeAfterUserRequestedTerminalStateAsync(),
                    CurrentSong,
                    null);
            }
        }
    }

    private bool TryBeginUserRequestedStopCleanup()
    {
        var nowTicks = DateTime.UtcNow.Ticks;
        if (nowTicks <= Volatile.Read(ref _userRequestedStopCleanupSuppressUntilUtcTicks))
        {
            return false;
        }

        if (Interlocked.CompareExchange(ref _userRequestedStopCleanupInProgress, 1, 0) != 0)
        {
            return false;
        }

        Volatile.Write(
            ref _userRequestedStopCleanupSuppressUntilUtcTicks,
            nowTicks + UserRequestedStopCleanupSuppressionWindow.Ticks);
        return true;
    }

    private async Task StopRuntimeAfterUserRequestedTerminalStateAsync()
    {
        try
        {
            await _playbackRuntime.StopAsync().ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Exchange(ref _userRequestedStopCleanupInProgress, 0);
        }
    }

    private void ClearUserRequestedStopCleanupSuppression()
    {
        Interlocked.Exchange(ref _userRequestedStopCleanupInProgress, 0);
        Volatile.Write(ref _userRequestedStopCleanupSuppressUntilUtcTicks, 0);
    }

    private void OnMediaItemChanged(object? sender, PlaybackMediaItemEventArgs e)
    {
        _logger.LogInformation("MediaItemChanged received. MediaItem={MediaItem}; {Snapshot}", DescribeMediaItem(e.MediaItem), CreatePlaybackSnapshot(CurrentSong, e.MediaItem));

        if (e.MediaItem == null) return;

        if (!TryResolveSongFromMediaItem(e.MediaItem, out var song, out var playlistIndex))
        {
            _logger.LogWarning("MediaItemChanged could not resolve song. MediaItem={MediaItem}; {Snapshot}", DescribeMediaItem(e.MediaItem), CreatePlaybackSnapshot(CurrentSong, e.MediaItem));
            return;
        }

        if (playlistIndex.HasValue && ShouldSuppressFailureInducedRewind(song.Id, playlistIndex.Value))
        {
            _logger.LogWarning(
                "MediaItemChanged ignored because it rewound playlist index during failure recovery. ResolvedSongId={SongId}; ResolvedIndex={ResolvedIndex}; CurrentSongId={CurrentSongId}; CurrentIndex={CurrentIndex}; {Snapshot}",
                song.Id,
                playlistIndex.Value,
                CurrentSong?.Id,
                _currentTrackIndex,
                CreatePlaybackSnapshot(CurrentSong, e.MediaItem));
            return;
        }

        if (playlistIndex.HasValue && ShouldSuppressQueueSelectionRewind(song.Id, playlistIndex.Value))
        {
            _logger.LogWarning(
                "MediaItemChanged ignored because queue rebuild has not selected the requested start track yet. ResolvedSongId={SongId}; ResolvedIndex={ResolvedIndex}; CurrentSongId={CurrentSongId}; CurrentIndex={CurrentIndex}; {Snapshot}",
                song.Id,
                playlistIndex.Value,
                CurrentSong?.Id,
                _currentTrackIndex,
                CreatePlaybackSnapshot(CurrentSong, e.MediaItem));
            return;
        }

        // Skip if we already set this song (e.g., from PlayTrackAtIndex or PlaySong)
        if (song.Id == CurrentSong?.Id)
        {
            if (_playbackRuntime.State == PlaybackRuntimeState.Failed)
            {
                _logger.LogWarning(
                    "MediaItemChanged resolved to current song while Playback runtime remains failed. ResolvedSongId={SongId}; PlaylistIndex={PlaylistIndex}; {Snapshot}",
                    song.Id,
                    playlistIndex,
                    CreatePlaybackSnapshot(CurrentSong, e.MediaItem));
            }

            _logger.LogInformation("MediaItemChanged resolved to current song; no app state update needed. ResolvedSongId={SongId}; PlaylistIndex={PlaylistIndex}; {Snapshot}", song.Id, playlistIndex, CreatePlaybackSnapshot(CurrentSong, e.MediaItem));
            return;
        }

        CancelPendingPlaylistAdvance();

        // Auto-advance from platform playback runtime (song ended naturally or user tapped Next in notification)
        if (playlistIndex.HasValue)
        {
            _currentTrackIndex = playlistIndex.Value;
            RaiseStateChanged(nameof(CurrentTrackIndex));
        }

        ResetStreamTracking(song.Id);
        PreviewLimitReached = false;
        ResetPlaybackState();
        CurrentSong = song;
        IsPlaying = true;
        QueueImmediateSubscriptionStatusRefreshForPlayback(TimeSpan.Zero);
        if (_playlist != null && playlistIndex.HasValue)
        {
            WarmPlaybackCacheInBackground(_playlist, playlistIndex.Value);
        }
        _logger.LogInformation("MediaItemChanged updated app state. ResolvedSongId={SongId}; PlaylistIndex={PlaylistIndex}; {Snapshot}", song.Id, playlistIndex, CreatePlaybackSnapshot(CurrentSong, e.MediaItem));
    }

    private void OnPositionChanged(object? sender, PlaybackPositionChangedEventArgs e)
    {
        MarkPositionChangedObserved();
        UpdatePosition(e.Position, _playbackRuntime.Duration);
    }

    private void OnMediaItemFinished(object? sender, PlaybackMediaItemEventArgs e)
    {
        _logger.LogInformation("MediaItemFinished received. MediaUri={MediaUri}; {Snapshot}", SanitizeMediaUri(e.MediaItem?.MediaUri), CreatePlaybackSnapshot(CurrentSong, e.MediaItem));

        if (ShouldIgnoreMediaItemFinished(e.MediaItem))
        {
            return;
        }

        OnMediaEnded();
    }

    private void OnMediaItemFailed(object? sender, PlaybackMediaItemFailedEventArgs e)
    {
        var exception = e.Exception;

        _logger.LogError(
            exception,
            "MediaItemFailed received. MediaUri={MediaUri}; Message={Message}; ExceptionType={ExceptionType}; {Snapshot}",
            SanitizeMediaUri(e.MediaItem?.MediaUri),
            e.Message,
            exception.GetType().FullName,
            CreatePlaybackSnapshot(CurrentSong, e.MediaItem));

        if (!HasPlaylist || _playlist == null || CurrentSong == null)
        {
            return;
        }

        var failedTrackIndex = ResolveCurrentFailureTrackIndex(e.MediaItem);
        var failedSongId = CurrentSong.Id;
        ScheduleMediaFailureRecovery(failedSongId, failedTrackIndex, "media item failure");

        var state = _playbackRuntime.State;
        if (state == PlaybackRuntimeState.Failed || state == PlaybackRuntimeState.Buffering)
        {
            TryRecoverFromMediaItemFailure(failedSongId, failedTrackIndex, _playlistAdvanceGeneration, "media item failure immediate recovery", e.MediaItem);
        }
    }

    private void ScheduleMediaFailureRecovery(int failedSongId, int failedTrackIndex, string reason)
    {
        _lastMediaFailureUtcTicks = DateTime.UtcNow.Ticks;
        _lastMediaFailureSongId = failedSongId;
        _lastMediaFailureTrackIndex = failedTrackIndex;
        var generation = _playlistAdvanceGeneration;
        _logger.LogInformation(
            "Scheduling media failure recovery. Reason={Reason}; FailedSongId={FailedSongId}; FailedTrackIndex={FailedTrackIndex}; Generation={Generation}; {Snapshot}",
            reason,
            failedSongId,
            failedTrackIndex,
            generation,
            CreatePlaybackSnapshot(CurrentSong, null));
        _ = RecoverFromMediaItemFailureAsync(failedSongId, failedTrackIndex, generation);
    }

    private void ScheduleFailedStateRecovery(int songId, int trackIndex, string reason)
    {
        _lastMediaFailureUtcTicks = DateTime.UtcNow.Ticks;
        _lastMediaFailureSongId = songId;
        _lastMediaFailureTrackIndex = trackIndex;
        var generation = _playlistAdvanceGeneration;
        _logger.LogInformation(
            "Scheduling failed-state queue recovery. Reason={Reason}; SongId={SongId}; TrackIndex={TrackIndex}; Generation={Generation}; {Snapshot}",
            reason,
            songId,
            trackIndex,
            generation,
            CreatePlaybackSnapshot(CurrentSong, null));
        _ = RecoverCurrentTrackFromFailedStateAsync(songId, trackIndex, generation);
    }

    private bool ShouldSuppressFailureInducedRewind(int resolvedSongId, int resolvedIndex)
    {
        if (!HasPlaylist || _playlist == null || CurrentSong == null)
        {
            return false;
        }

        if (resolvedIndex >= _currentTrackIndex)
        {
            return false;
        }

        var elapsedSinceFailure = DateTime.UtcNow - new DateTime(_lastMediaFailureUtcTicks, DateTimeKind.Utc);
        if (elapsedSinceFailure > FailureInducedRewindSuppressionWindow)
        {
            return false;
        }

        var lastFailureMatchesCurrent = _lastMediaFailureSongId == CurrentSong.Id &&
                                       _lastMediaFailureTrackIndex == _currentTrackIndex;
        if (!lastFailureMatchesCurrent)
        {
            return false;
        }

        var nextExpectedIndex = ResolveSequentialNextTrackIndex(_currentTrackIndex);
        if (nextExpectedIndex.HasValue && nextExpectedIndex.Value == resolvedIndex)
        {
            return false;
        }

        _logger.LogInformation(
            "Detected failure-induced rewind candidate. ResolvedSongId={ResolvedSongId}; ResolvedIndex={ResolvedIndex}; CurrentSongId={CurrentSongId}; CurrentIndex={CurrentIndex}; LastFailureSongId={LastFailureSongId}; LastFailureTrackIndex={LastFailureTrackIndex}; FailureAge={FailureAge}; {Snapshot}",
            resolvedSongId,
            resolvedIndex,
            CurrentSong.Id,
            _currentTrackIndex,
            _lastMediaFailureSongId,
            _lastMediaFailureTrackIndex,
            elapsedSinceFailure,
            CreatePlaybackSnapshot(CurrentSong, null));

        return true;
    }

    private bool ShouldSuppressQueueSelectionRewind(int resolvedSongId, int resolvedIndex)
    {
        var suppressedStartIndex = Volatile.Read(ref _queueSelectionSuppressionStartIndex);
        if (suppressedStartIndex <= 0)
        {
            return false;
        }

        var suppressionGeneration = Volatile.Read(ref _queueSelectionSuppressionRequestGeneration);
        if (suppressionGeneration != Volatile.Read(ref _playbackRequestGeneration))
        {
            return false;
        }

        var expiresUtcTicks = Volatile.Read(ref _queueSelectionSuppressionExpiresUtcTicks);
        if (expiresUtcTicks > 0 && DateTime.UtcNow.Ticks > expiresUtcTicks)
        {
            return false;
        }

        if (resolvedIndex == suppressedStartIndex)
        {
            return false;
        }

        if (resolvedIndex != 0)
        {
            return false;
        }

        if (_currentTrackIndex <= resolvedIndex)
        {
            return false;
        }

        if (resolvedIndex == _currentTrackIndex - 1)
        {
            return false;
        }

        var nextExpectedIndex = ResolveSequentialNextTrackIndex(_currentTrackIndex);
        if (nextExpectedIndex == resolvedIndex)
        {
            return false;
        }

        _logger.LogInformation(
            "Detected queue-selection rewind candidate. ResolvedSongId={ResolvedSongId}; ResolvedIndex={ResolvedIndex}; SuppressedStartIndex={SuppressedStartIndex}; SuppressionGeneration={SuppressionGeneration}; CurrentGeneration={CurrentGeneration}; {Snapshot}",
            resolvedSongId,
            resolvedIndex,
            suppressedStartIndex,
            suppressionGeneration,
            Volatile.Read(ref _playbackRequestGeneration),
            CreatePlaybackSnapshot(CurrentSong, null));

        return true;
    }

    private int? ResolveFailedTrackIndex(PlaybackMediaItem? mediaItem)
    {
        if (mediaItem == null)
        {
            return null;
        }

        if (TryResolveSongFromMediaItem(mediaItem, out var song, out var playlistIndex))
        {
            return playlistIndex ?? ResolvePlaylistIndex(song.Id);
        }

        return ResolvePlaylistIndex(mediaItem);
    }

    private int ResolveCurrentFailureTrackIndex(PlaybackMediaItem? mediaItem)
    {
        var resolvedTrackIndex = ResolveFailedTrackIndex(mediaItem);
        if (!resolvedTrackIndex.HasValue || _playlist == null || CurrentSong == null)
        {
            return _currentTrackIndex;
        }

        var resolvedIndex = resolvedTrackIndex.Value;
        if (resolvedIndex < 0 || resolvedIndex >= _playlist.Count)
        {
            _logger.LogWarning(
                "MediaItemFailed resolved to an out-of-range playlist index; using current track for recovery. ResolvedTrackIndex={ResolvedTrackIndex}; CurrentTrackIndex={CurrentTrackIndex}; CurrentSongId={CurrentSongId}; {Snapshot}",
                resolvedIndex,
                _currentTrackIndex,
                CurrentSong.Id,
                CreatePlaybackSnapshot(CurrentSong, mediaItem));
            return _currentTrackIndex;
        }

        var resolvedSong = _playlist[resolvedIndex];
        if (resolvedSong.Id == CurrentSong.Id)
        {
            return resolvedIndex;
        }

        _logger.LogWarning(
            "MediaItemFailed resolved to a stale playlist item; using current track for recovery. ResolvedSongId={ResolvedSongId}; ResolvedTrackIndex={ResolvedTrackIndex}; CurrentSongId={CurrentSongId}; CurrentTrackIndex={CurrentTrackIndex}; {Snapshot}",
            resolvedSong.Id,
            resolvedIndex,
            CurrentSong.Id,
            _currentTrackIndex,
            CreatePlaybackSnapshot(CurrentSong, mediaItem));
        return _currentTrackIndex;
    }

    private async Task RecoverFromMediaItemFailureAsync(int failedSongId, int failedTrackIndex, int generation)
    {
        await Task.Delay(_playlistAdvanceFallbackDelay).ConfigureAwait(false);

        TryRecoverFromMediaItemFailure(failedSongId, failedTrackIndex, generation, "media item failure delayed recovery");
    }

    private bool TryRecoverFromMediaItemFailure(
        int failedSongId,
        int failedTrackIndex,
        int generation,
        string reason,
        PlaybackMediaItem? failedMediaItem = null,
        bool cacheStatusIsFresh = false)
    {
        if (!HasPlaylist || _playlist == null)
        {
            return false;
        }

        if (generation != _playlistAdvanceGeneration)
        {
            return false;
        }

        if (CurrentSong?.Id != failedSongId || _currentTrackIndex != failedTrackIndex)
        {
            return false;
        }

        var state = _playbackRuntime.State;
        var nativeCurrentIndex = TryResolveNativeQueueIndex();
        var nativeQueueDivergedFromFailedTrack = nativeCurrentIndex.HasValue && nativeCurrentIndex.Value != failedTrackIndex;
        var shouldRecover = state == PlaybackRuntimeState.Failed ||
                            state == PlaybackRuntimeState.Buffering ||
                            nativeQueueDivergedFromFailedTrack;
        if (!shouldRecover)
        {
            return false;
        }

        if (TryRecoverFailedTrackFromCachedPlaybackUri(failedSongId, failedTrackIndex, failedMediaItem, reason))
        {
            return true;
        }

        if (!cacheStatusIsFresh)
        {
            _ = RefreshCacheStatusAndRetryFailureRecoveryAsync(
                failedSongId,
                failedTrackIndex,
                generation,
                reason,
                failedMediaItem);
            return true;
        }

        return TryAdvancePastUnplayableTrack(failedSongId, failedTrackIndex, "media item failure recovery");
    }

    /// <summary>
    /// Advances the queue past a track that keeps failing to produce playback (for example a
    /// corrupt or unplayable media file) so one bad song cannot end the listening session.
    /// Consecutive skips are bounded; once the limit is hit the caller falls back to stopping.
    /// </summary>
    private bool TryAdvancePastUnplayableTrack(int failedSongId, int failedTrackIndex, string reason)
    {
        if (!HasPlaylist || _playlist == null)
        {
            return false;
        }

        var nextIndex = ResolveSequentialNextTrackIndex(failedTrackIndex);
        if (!nextIndex.HasValue)
        {
            if (!IsRepeatEnabled)
            {
                IsPlaying = false;
                _logger.LogInformation(
                    "Unplayable-track recovery reached queue end and stopped playback. Reason={Reason}; FailedSongId={FailedSongId}; FailedTrackIndex={FailedTrackIndex}; {Snapshot}",
                    reason,
                    failedSongId,
                    failedTrackIndex,
                    CreatePlaybackSnapshot(CurrentSong, null));
                return true;
            }

            nextIndex = 0;
        }

        if (nextIndex.Value == failedTrackIndex)
        {
            return false;
        }

        if (!IsTrackLocalReady(nextIndex.Value))
        {
            EnterWaitingForPreparedMediaState(
                reason + " found remote-only successor",
                failedSongId,
                failedTrackIndex,
                nextIndex.Value);
            return true;
        }

        if (Interlocked.Increment(ref _consecutiveUnplayableTrackSkipCount) > MaxConsecutiveUnplayableTrackSkips)
        {
            _logger.LogError(
                "Unplayable-track skip limit reached without successful playback; not advancing further. Reason={Reason}; FailedSongId={FailedSongId}; FailedTrackIndex={FailedTrackIndex}; {Snapshot}",
                reason,
                failedSongId,
                failedTrackIndex,
                CreatePlaybackSnapshot(CurrentSong, null));
            return false;
        }

        _logger.LogWarning(
            "Skipping unplayable track and continuing the queue. Reason={Reason}; FailedSongId={FailedSongId}; FailedTrackIndex={FailedTrackIndex}; NextIndex={NextIndex}; {Snapshot}",
            reason,
            failedSongId,
            failedTrackIndex,
            nextIndex.Value,
            CreatePlaybackSnapshot(CurrentSong, null));

        PlaybackRequestFailed?.Invoke(
            this,
            new PlaybackRequestFailedEventArgs(failedSongId, PlaybackRequestFailureReason.UnplayableTrackSkipped));

        PlayTrackAtIndexWithQueueReload(nextIndex.Value, reason);
        return true;
    }

    private async Task RefreshCacheStatusAndRetryFailureRecoveryAsync(
        int failedSongId,
        int failedTrackIndex,
        int generation,
        string reason,
        PlaybackMediaItem? failedMediaItem)
    {
        var playlistSnapshot = _playlist;
        if (playlistSnapshot == null)
        {
            return;
        }

        try
        {
            await ResolveQueueCacheStatusesAsync(playlistSnapshot, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Unable to refresh cache status during playback failure recovery.");
        }

        if (generation != _playlistAdvanceGeneration ||
            !ReferenceEquals(_playlist, playlistSnapshot) ||
            CurrentSong?.Id != failedSongId ||
            _currentTrackIndex != failedTrackIndex)
        {
            return;
        }

        TryRecoverFromMediaItemFailure(
            failedSongId,
            failedTrackIndex,
            generation,
            reason,
            failedMediaItem,
            cacheStatusIsFresh: true);
    }

    private bool IsTrackLocalReady(int trackIndex)
    {
        if (_playlist == null || trackIndex < 0 || trackIndex >= _playlist.Count)
        {
            return false;
        }

        var song = _playlist[trackIndex];
        return _cacheStatusSnapshot.TryGetValue(song.Id, out var status) &&
            (status.IsLocalReady || IsLocalPlaybackUri(status.LocalPlaybackUri));
    }

    private void EnterWaitingForPreparedMediaState(
        string reason,
        int currentSongId,
        int currentTrackIndex,
        int blockedNextIndex)
    {
        IsPlaying = false;
        if (_playlist != null)
        {
            StartQueuePreparation(_playlist, currentTrackIndex, QueuePreparationMode.SleepSafe);
        }

        PreparationState = PlaybackPreparationState.WaitingForNetwork;

        _logger.LogWarning(
            "Automatic recovery preserved queue position because the next track is not local-ready. Reason={Reason}; CurrentSongId={CurrentSongId}; CurrentTrackIndex={CurrentTrackIndex}; BlockedNextIndex={BlockedNextIndex}; {Snapshot}",
            reason,
            currentSongId,
            currentTrackIndex,
            blockedNextIndex,
            CreatePlaybackSnapshot(CurrentSong, null));
    }

    private bool TryRecoverFailedTrackFromCachedPlaybackUri(
        int failedSongId,
        int failedTrackIndex,
        PlaybackMediaItem? failedMediaItem,
        string reason)
    {
        if (_playlist == null ||
            failedTrackIndex < 0 ||
            failedTrackIndex >= _playlist.Count ||
            CurrentSong?.Id != failedSongId ||
            _currentTrackIndex != failedTrackIndex)
        {
            return false;
        }

        var failedMediaUri = failedMediaItem?.MediaUri ?? TryGetNativeCurrentMediaUri();
        if (IsLocalPlaybackUri(failedMediaUri))
        {
            return false;
        }

        var song = _playlist[failedTrackIndex];
        if (!_cacheStatusSnapshot.TryGetValue(song.Id, out var cacheStatus))
        {
            return false;
        }

        var canRecoverFromCache = cacheStatus.IsLocalReady || IsLocalPlaybackUri(cacheStatus.LocalPlaybackUri);
        if (canRecoverFromCache && CachedFailureRecoveryAttemptsExhausted(failedSongId))
        {
            _logger.LogWarning(
                "Cached recovery attempts exhausted for failed track; treating cached media as unplayable. Reason={Reason}; SongId={SongId}; TrackIndex={TrackIndex}; {Snapshot}",
                reason,
                failedSongId,
                failedTrackIndex,
                CreatePlaybackSnapshot(CurrentSong, failedMediaItem));
            return false;
        }

        if (cacheStatus.IsLocalReady || IsLocalPlaybackUri(cacheStatus.LocalPlaybackUri))
        {
            _logger.LogWarning(
                "Recovering failed remote playlist track from sleep-safe cached media. Reason={Reason}; SongId={SongId}; TrackIndex={TrackIndex}; FailedMediaUri={FailedMediaUri}; StableCacheKey={StableCacheKey}; {Snapshot}",
                reason,
                failedSongId,
                failedTrackIndex,
                SanitizeMediaUri(failedMediaUri),
                cacheStatus.StableCacheKey,
                CreatePlaybackSnapshot(CurrentSong, failedMediaItem));

            PlayTrackAtIndexWithQueueReload(failedTrackIndex, "cached playback recovery after media item failure");
            return true;
        }

        var cachedPlaybackUri = cacheStatus.LocalPlaybackUri;
        if (!IsLocalPlaybackUri(cachedPlaybackUri))
        {
            return false;
        }

        _logger.LogWarning(
            "Recovering failed remote playlist track from cached playback URI. Reason={Reason}; SongId={SongId}; TrackIndex={TrackIndex}; FailedMediaUri={FailedMediaUri}; CachedPlaybackUri={CachedPlaybackUri}; {Snapshot}",
            reason,
            failedSongId,
            failedTrackIndex,
            SanitizeMediaUri(failedMediaUri),
            SanitizeMediaUri(cachedPlaybackUri),
            CreatePlaybackSnapshot(CurrentSong, failedMediaItem));

        PlayTrackAtIndexWithQueueReload(failedTrackIndex, "cached playback recovery after media item failure");
        return true;
    }

    private async Task RecoverCurrentTrackFromFailedStateAsync(int songId, int trackIndex, int generation)
    {
        await Task.Delay(_playlistAdvanceFallbackDelay + _playlistAdvanceFallbackDelay).ConfigureAwait(false);

        if (!HasPlaylist || _playlist == null)
        {
            return;
        }

        if (generation != _playlistAdvanceGeneration)
        {
            return;
        }

        if (CurrentSong?.Id != songId || _currentTrackIndex != trackIndex)
        {
            return;
        }

        var state = _playbackRuntime.State;
        if (state != PlaybackRuntimeState.Failed && state != PlaybackRuntimeState.Buffering)
        {
            return;
        }

        _logger.LogWarning(
            "Failed-state recovery rebuilding current track. SongId={SongId}; TrackIndex={TrackIndex}; MediaState={MediaState}; {Snapshot}",
            songId,
            trackIndex,
            state,
            CreatePlaybackSnapshot(CurrentSong, null));

        PlayTrackAtIndexWithQueueReload(trackIndex, "media manager failed state recovery");
    }

    private void ScheduleBufferingStallRecovery(string reason)
    {
        if (!IsPlaying || !HasPlaylist || _playlist == null || CurrentSong == null)
        {
            return;
        }

        var recoveryGeneration = Interlocked.Increment(ref _bufferingStallRecoveryGeneration);
        var playlistGeneration = _playlistAdvanceGeneration;
        var songId = CurrentSong.Id;
        var trackIndex = _currentTrackIndex;

        _logger.LogInformation(
            "Scheduling buffering stall recovery. Reason={Reason}; SongId={SongId}; TrackIndex={TrackIndex}; PlaylistGeneration={PlaylistGeneration}; RecoveryGeneration={RecoveryGeneration}; DelayMs={DelayMs}; {Snapshot}",
            reason,
            songId,
            trackIndex,
            playlistGeneration,
            recoveryGeneration,
            _bufferingStallRecoveryDelay.TotalMilliseconds,
            CreatePlaybackSnapshot(CurrentSong, null));

        _ = RecoverFromBufferingStallAsync(songId, trackIndex, playlistGeneration, recoveryGeneration);
    }

    private void CancelPendingBufferingStallRecovery()
    {
        Interlocked.Increment(ref _bufferingStallRecoveryGeneration);
    }

    private async Task RecoverFromBufferingStallAsync(int songId, int trackIndex, int playlistGeneration, int recoveryGeneration)
    {
        await Task.Delay(_bufferingStallRecoveryDelay).ConfigureAwait(false);

        if (Volatile.Read(ref _bufferingStallRecoveryGeneration) != recoveryGeneration)
        {
            return;
        }

        if (!HasPlaylist || _playlist == null)
        {
            return;
        }

        if (playlistGeneration != _playlistAdvanceGeneration)
        {
            return;
        }

        if (CurrentSong?.Id != songId || _currentTrackIndex != trackIndex)
        {
            return;
        }

        var state = _playbackRuntime.State;
        if (state != PlaybackRuntimeState.Buffering)
        {
            return;
        }

        var nextIndex = ResolveSequentialNextTrackIndex(trackIndex);
        if (!nextIndex.HasValue)
        {
            if (!IsRepeatEnabled)
            {
                IsPlaying = false;
                _logger.LogInformation(
                    "Buffering stall recovery reached queue end and stopped playback. SongId={SongId}; TrackIndex={TrackIndex}; {Snapshot}",
                    songId,
                    trackIndex,
                    CreatePlaybackSnapshot(CurrentSong, null));
                return;
            }

            nextIndex = 0;
        }

        if (!IsTrackLocalReady(nextIndex.Value))
        {
            EnterWaitingForPreparedMediaState(
                "buffering stall recovery found remote-only successor",
                songId,
                trackIndex,
                nextIndex.Value);
            return;
        }

        _logger.LogWarning(
            "Buffering stall recovery advancing to next track. SongId={SongId}; TrackIndex={TrackIndex}; NextIndex={NextIndex}; MediaState={MediaState}; {Snapshot}",
            songId,
            trackIndex,
            nextIndex.Value,
            state,
            CreatePlaybackSnapshot(CurrentSong, null));

        PlayTrackAtIndexWithQueueReload(
            nextIndex.Value,
            "buffering stall recovery",
            scheduleBufferingStallRecoveryAfterQueueRebuild: false);
    }

    private async Task EnsurePlaylistContinuesAsync(int finishedSongId, int finishedTrackIndex, int generation, bool resetPositionWhenStopped = false)
    {
        await Task.Delay(_playlistAdvanceFallbackDelay).ConfigureAwait(false);

        _logger.LogInformation(
            "Playlist continuation fallback woke. FinishedSongId={FinishedSongId}; FinishedTrackIndex={FinishedTrackIndex}; Generation={Generation}; CurrentGeneration={CurrentGeneration}; {Snapshot}",
            finishedSongId,
            finishedTrackIndex,
            generation,
            _playlistAdvanceGeneration,
            CreatePlaybackSnapshot(CurrentSong, null));

        if (!HasPlaylist || _playlist == null)
        {
            _logger.LogInformation("Playlist continuation fallback skipped because playlist is no longer active. {Snapshot}", CreatePlaybackSnapshot(CurrentSong, null));
            return;
        }

        if (CurrentSong?.Id != finishedSongId || _currentTrackIndex != finishedTrackIndex)
        {
            var shouldRecoverAdvancedTrack = ShouldRecoverAdvancedPlaylistTrack();
            _logger.LogInformation(
                "Playlist continuation fallback observed app state divergence. FinishedSongId={FinishedSongId}; FinishedTrackIndex={FinishedTrackIndex}; ShouldRecoverAdvancedTrack={ShouldRecoverAdvancedTrack}; {Snapshot}",
                finishedSongId,
                finishedTrackIndex,
                shouldRecoverAdvancedTrack,
                CreatePlaybackSnapshot(CurrentSong, null));

            if (shouldRecoverAdvancedTrack)
            {
                _logger.LogWarning("Playlist continuation fallback retrying advanced track because native handoff is not playing. FinishedSongId={FinishedSongId}; FinishedTrackIndex={FinishedTrackIndex}; {Snapshot}", finishedSongId, finishedTrackIndex, CreatePlaybackSnapshot(CurrentSong, null));
                PlayTrackAtIndex(_currentTrackIndex);
                return;
            }

            _logger.LogInformation("Playlist continuation fallback skipped because app state already advanced. FinishedSongId={FinishedSongId}; FinishedTrackIndex={FinishedTrackIndex}; {Snapshot}", finishedSongId, finishedTrackIndex, CreatePlaybackSnapshot(CurrentSong, null));
            return;
        }

        if (generation != _playlistAdvanceGeneration)
        {
            _logger.LogInformation("Playlist continuation fallback skipped because generation changed. ScheduledGeneration={ScheduledGeneration}; CurrentGeneration={CurrentGeneration}; {Snapshot}", generation, _playlistAdvanceGeneration, CreatePlaybackSnapshot(CurrentSong, null));
            return;
        }

        var nativeCurrentIndex = TryResolveNativeQueueIndex();
        if (nativeCurrentIndex.HasValue && nativeCurrentIndex.Value != finishedTrackIndex)
        {
            _logger.LogInformation("Playlist continuation fallback syncing to native queue index. NativeCurrentIndex={NativeCurrentIndex}; FinishedTrackIndex={FinishedTrackIndex}; {Snapshot}", nativeCurrentIndex.Value, finishedTrackIndex, CreatePlaybackSnapshot(CurrentSong, null));
            PlayTrackAtIndex(nativeCurrentIndex.Value);
            return;
        }

        if (_isShuffleEnabled && await _playbackRuntime.PlayNextAsync().ConfigureAwait(false))
        {
            _logger.LogInformation("Playlist continuation fallback invoked Playback runtime.PlayNext for shuffle. {Snapshot}", CreatePlaybackSnapshot(CurrentSong, null));
            return;
        }

        var nextIndex = ResolveSequentialNextTrackIndex(finishedTrackIndex);
        if (nextIndex == null)
        {
            if (!IsRepeatEnabled)
            {
                IsPlaying = false;
                if (resetPositionWhenStopped)
                {
                    ResetPreviewPositionToStart("playlist continuation stop");
                }
                _logger.LogInformation("Playlist continuation fallback reached end of queue and stopped playback. {Snapshot}", CreatePlaybackSnapshot(CurrentSong, null));
            }

            return;
        }

        _logger.LogInformation("Playlist continuation fallback forcing next sequential track. NextIndex={NextIndex}; {Snapshot}", nextIndex.Value, CreatePlaybackSnapshot(CurrentSong, null));
        PlayTrackAtIndex(nextIndex.Value);
    }

    // --- Queue helpers ---

    private int BeginPlaybackRequest()
    {
        lock (_playbackRequestSync)
        {
            CancelPendingBufferingStallRecovery();
            CancelPendingQueueBuildLocked();
            return ++_playbackRequestGeneration;
        }
    }

    private void CancelPendingPlaybackRequest()
    {
        lock (_playbackRequestSync)
        {
            CancelPendingBufferingStallRecovery();
            CancelPendingQueueBuildLocked();
            _playbackRequestGeneration++;
        }
    }

    private bool TryBeginQueueBuild(int requestGeneration, out CancellationTokenSource cancellationSource)
    {
        lock (_playbackRequestSync)
        {
            if (_playbackRequestGeneration != requestGeneration)
            {
                cancellationSource = null!;
                return false;
            }

            cancellationSource = new CancellationTokenSource();
            CancelPendingQueueBuildLocked();
            _queueBuildCancellation = cancellationSource;
            return true;
        }
    }

    private void CompleteQueueBuild(CancellationTokenSource cancellationSource)
    {
        lock (_playbackRequestSync)
        {
            if (ReferenceEquals(_queueBuildCancellation, cancellationSource))
            {
                _queueBuildCancellation = null;
            }
        }

        cancellationSource.Dispose();
    }

    private void CancelPendingQueueBuild()
    {
        lock (_playbackRequestSync)
        {
            CancelPendingQueueBuildLocked();
        }
    }

    private void CancelPendingQueueBuildLocked()
    {
        var cancellationSource = _queueBuildCancellation;
        _queueBuildCancellation = null;
        cancellationSource?.Cancel();
        cancellationSource?.Dispose();
    }

    private void CancelQueuePreparation()
    {
        var cancellationSource = Interlocked.Exchange(ref _queuePreparationCancellation, null);
        cancellationSource?.Cancel();
        cancellationSource?.Dispose();
        PreparationState = PlaybackPreparationState.None;
    }

    private bool IsPlaybackRequestCurrent(int requestGeneration)
    {
        return Volatile.Read(ref _playbackRequestGeneration) == requestGeneration;
    }

    private void StartQueuePreparation(
        IReadOnlyList<SongDto> playlistSnapshot,
        int startIndex,
        QueuePreparationMode mode)
    {
        if (playlistSnapshot.Count == 0 || startIndex < 0 || startIndex >= playlistSnapshot.Count)
        {
            return;
        }

        var previous = Interlocked.Exchange(
            ref _queuePreparationCancellation,
            new CancellationTokenSource());
        previous?.Cancel();
        previous?.Dispose();

        PreparationState = PlaybackPreparationState.Preparing;
        var cancellationSource = _queuePreparationCancellation;
        if (cancellationSource == null)
        {
            return;
        }

        var queueCopy = playlistSnapshot.ToList();
        _ = PrepareQueueAsync(queueCopy, startIndex, mode, cancellationSource);
    }

    private async Task PrepareQueueAsync(
        IReadOnlyList<SongDto> queueSnapshot,
        int startIndex,
        QueuePreparationMode mode,
        CancellationTokenSource cancellationSource)
    {
        try
        {
            var continuityWindow = mode == QueuePreparationMode.SleepSafe
                ? GetSleepSafeContinuityWindow()
                : TimeSpan.Zero;
            var result = await _queuePreparationService
                .PrepareAsync(queueSnapshot, startIndex, mode, continuityWindow, cancellationSource.Token)
                .ConfigureAwait(false);

            if (cancellationSource.IsCancellationRequested)
            {
                return;
            }

            LastQueuePreparationResult = result;
            PreparationState = result.CurrentTrackReady
                ? PlaybackPreparationState.Ready
                : result.FailureReason.HasValue
                    ? PlaybackPreparationState.WaitingForNetwork
                    : PlaybackPreparationState.Preparing;

            // Refresh the cache-status snapshot from ground truth now that preparation has
            // downloaded/warmed content. Otherwise IsTrackLocalReady keeps trusting the pre-
            // preparation statuses and can treat a now-cached track as remote-only (entering
            // WaitingForPreparedMedia) or a no-longer-available track as sleep-safe.
            if (!cancellationSource.IsCancellationRequested)
            {
                await ResolveQueueCacheStatusesAsync(queueSnapshot, cancellationSource.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Queue preparation failed.");
            PreparationState = PlaybackPreparationState.Error;
        }
    }

    private static TimeSpan GetSleepSafeContinuityWindow()
    {
#if ANDROID
        return QueuePreparationService.FullQueueSleepSafeContinuityWindow;
#else
        return QueuePreparationService.DefaultSleepSafeContinuityWindow;
#endif
    }

    private void BuildAndStartQueue(
        int startIndex,
        int requestGeneration,
        bool pauseAfterQueueSelection = false,
        bool scheduleBufferingStallRecoveryAfterQueueRebuild = true)
    {
        _ = BuildAndStartQueueAsync(
            startIndex,
            requestGeneration,
            pauseAfterQueueSelection,
            scheduleBufferingStallRecoveryAfterQueueRebuild);
    }

    private void PlayTrackAtIndexWithQueueReload(
        int index,
        string reason,
        bool scheduleBufferingStallRecoveryAfterQueueRebuild = true)
    {
        if (_playlist == null || index < 0 || index >= _playlist.Count)
            return;

        CancelPendingPlaylistAdvance();
        var requestGeneration = BeginPlaybackRequest();
        _currentTrackIndex = index;
        RaiseStateChanged(nameof(CurrentTrackIndex));

        var song = _playlist[index];
        ResetStreamTracking(song.Id);
        ResetPlaybackState();
        CurrentSong = song;
        IsPlaying = true;
        QueueImmediateSubscriptionStatusRefreshForPlayback(TimeSpan.Zero);

        _logger.LogInformation(
            "Forcing queue reload for requested track. Reason={Reason}; Index={Index}; RequestGeneration={RequestGeneration}; {Snapshot}",
            reason,
            index,
            requestGeneration,
            CreatePlaybackSnapshot(song, null));
        StartQueuePreparation(_playlist, index, QueuePreparationMode.SleepSafe);
        BuildAndStartQueue(
            index,
            requestGeneration,
            scheduleBufferingStallRecoveryAfterQueueRebuild: scheduleBufferingStallRecoveryAfterQueueRebuild);
    }

    private async Task BuildAndStartQueueAsync(
        int startIndex,
        int requestGeneration,
        bool pauseAfterQueueSelection,
        bool scheduleBufferingStallRecoveryAfterQueueRebuild)
    {
        if (_playlist == null || startIndex < 0 || startIndex >= _playlist.Count)
        {
            return;
        }

        var playlistSnapshot = _playlist;
        var currentSong = playlistSnapshot[startIndex];
        if (!TryBeginQueueBuild(requestGeneration, out var queueBuildCancellation))
        {
            return;
        }
        IReadOnlyDictionary<int, TrackCacheStatus> cacheStatuses;
        try
        {
            cacheStatuses = await ResolveQueueCacheStatusesAsync(
                playlistSnapshot,
                queueBuildCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (queueBuildCancellation.IsCancellationRequested)
        {
            return;
        }
        finally
        {
            CompleteQueueBuild(queueBuildCancellation);
        }

        if (!IsPlaybackRequestCurrent(requestGeneration) ||
            !ReferenceEquals(_playlist, playlistSnapshot) ||
            _currentTrackIndex != startIndex ||
            CurrentSong?.Id != currentSong.Id)
        {
            _logger.LogInformation(
                "BuildAndStartQueue skipped because a newer playback request superseded it. StartIndex={StartIndex}; RequestGeneration={RequestGeneration}; CurrentGeneration={CurrentGeneration}; {Snapshot}",
                startIndex,
                requestGeneration,
                Volatile.Read(ref _playbackRequestGeneration),
                CreatePlaybackSnapshot(CurrentSong, null));
            return;
        }
        ClearSongPlaybackUriMap();
        var items = playlistSnapshot.Select((song, index) =>
        {
            var cacheStatus = cacheStatuses[song.Id];
            var mediaUri = ResolveImmediatePlaybackUri(song, cacheStatus);
            var item = CreateMediaItem(song, cacheStatus, mediaUri);
            return item;
        }).ToArray();

        var capturedStart = startIndex;
        _logger.LogInformation(
            "BuildAndStartQueue calling Playback runtime.Play. StartIndex={StartIndex}; QueueCount={QueueCount}; QueueItems={QueueItems}; {Snapshot}",
            startIndex,
            items.Length,
            DescribeMediaItems(items),
            CreatePlaybackSnapshot(CurrentSong, null));

        BeginQueueSelectionSuppression(capturedStart, requestGeneration);

        try
        {
            var usesIndexedQueueStart = _playbackRuntime is IIndexedQueuePlaybackRuntime;
            var playQueueTask = usesIndexedQueueStart
                ? ((IIndexedQueuePlaybackRuntime)_playbackRuntime).PlayAsync(items, capturedStart)
                : _playbackRuntime.PlayAsync((IEnumerable<PlaybackMediaItem>)items);
            ObserveMediaCommand("Playback runtime.Play queue", playQueueTask, DescribeMediaItem, CurrentSong, null);
            WarmPlaybackCacheInBackground(playlistSnapshot, startIndex);

            try
            {
                await playQueueTask.ConfigureAwait(false);
            }
            catch
            {
                return;
            }

            if (!IsPlaybackRequestCurrent(requestGeneration) || !ReferenceEquals(_playlist, playlistSnapshot))
            {
                return;
            }

            if (capturedStart > 0 && !usesIndexedQueueStart)
            {
                _logger.LogInformation("BuildAndStartQueue selecting captured start index after queue play. StartIndex={StartIndex}; {Snapshot}", capturedStart, CreatePlaybackSnapshot(CurrentSong, null));
                var selectCapturedStartTask = _playbackRuntime.PlayQueueItemAsync(capturedStart);
                ObserveMediaCommand($"Playback runtime.PlayQueueItem captured start {capturedStart}", selectCapturedStartTask, DescribeBooleanResult, CurrentSong, null);

                try
                {
                    await selectCapturedStartTask.ConfigureAwait(false);
                }
                catch
                {
                    return;
                }
            }

            if (scheduleBufferingStallRecoveryAfterQueueRebuild && !pauseAfterQueueSelection)
            {
                ScheduleBufferingStallRecoveryAfterQueueRebuild(requestGeneration, playlistSnapshot, capturedStart);
            }

            if (!pauseAfterQueueSelection || !IsPlaybackRequestCurrent(requestGeneration) || !ReferenceEquals(_playlist, playlistSnapshot))
            {
                return;
            }

            var pauseTask = _playbackRuntime.PauseAsync();
            ObserveMediaCommand("Playback runtime.Pause after queue rebuild", pauseTask, CurrentSong, null);

            try
            {
                await pauseTask.ConfigureAwait(false);
                IsPlaying = false;
            }
            catch
            {
                // ObserveMediaCommand logs the failure; keep the current playback state.
            }
        }
        finally
        {
            EndQueueSelectionSuppression(requestGeneration);
        }
    }

    private void ReplaceNativeQueuePreservingCurrentPlayback(
        List<SongDto> playlistSnapshot,
        int currentTrackIndex,
        int requestGeneration,
        TimeSpan currentPosition,
        bool playWhenReady)
    {
        if (_playbackRuntime is not IQueueReplacementPlaybackRuntime queueReplacementRuntime)
        {
            _logger.LogInformation(
                "Playback runtime does not support native queue replacement during preserved queue sync. {Snapshot}",
                CreatePlaybackSnapshot(CurrentSong, null));
            return;
        }

        _ = ReplaceNativeQueuePreservingCurrentPlaybackAsync(
            queueReplacementRuntime,
            playlistSnapshot,
            currentTrackIndex,
            requestGeneration,
            currentPosition,
            playWhenReady);
    }

    private async Task ReplaceNativeQueuePreservingCurrentPlaybackAsync(
        IQueueReplacementPlaybackRuntime queueReplacementRuntime,
        List<SongDto> playlistSnapshot,
        int currentTrackIndex,
        int requestGeneration,
        TimeSpan currentPosition,
        bool playWhenReady)
    {
        if (playlistSnapshot.Count == 0 || currentTrackIndex < 0 || currentTrackIndex >= playlistSnapshot.Count)
        {
            return;
        }

        var currentSong = playlistSnapshot[currentTrackIndex];
        if (!TryBeginQueueBuild(requestGeneration, out var queueBuildCancellation))
        {
            return;
        }
        IReadOnlyDictionary<int, TrackCacheStatus> cacheStatuses;
        try
        {
            cacheStatuses = await ResolveQueueCacheStatusesAsync(
                playlistSnapshot,
                queueBuildCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (queueBuildCancellation.IsCancellationRequested)
        {
            return;
        }
        finally
        {
            CompleteQueueBuild(queueBuildCancellation);
        }
        if (!IsPlaybackRequestCurrent(requestGeneration) ||
            !ReferenceEquals(_playlist, playlistSnapshot) ||
            _currentTrackIndex != currentTrackIndex ||
            CurrentSong?.Id != currentSong.Id)
        {
            _logger.LogInformation(
                "Preserved native queue replacement skipped because a newer playback request superseded it. CurrentIndex={CurrentIndex}; RequestGeneration={RequestGeneration}; CurrentGeneration={CurrentGeneration}; {Snapshot}",
                currentTrackIndex,
                requestGeneration,
                Volatile.Read(ref _playbackRequestGeneration),
                CreatePlaybackSnapshot(CurrentSong, null));
            return;
        }

        ClearSongPlaybackUriMap();
        var items = playlistSnapshot
            .Select(song =>
            {
                var cacheStatus = cacheStatuses[song.Id];
                return CreateMediaItem(song, cacheStatus, ResolveImmediatePlaybackUri(song, cacheStatus));
            })
            .ToArray();

        _logger.LogInformation(
            "Replacing native playback queue while preserving current song. CurrentIndex={CurrentIndex}; QueueCount={QueueCount}; CurrentPosition={CurrentPosition}; PlayWhenReady={PlayWhenReady}; QueueItems={QueueItems}; {Snapshot}",
            currentTrackIndex,
            items.Length,
            currentPosition,
            playWhenReady,
            DescribeMediaItems(items),
            CreatePlaybackSnapshot(CurrentSong, null));

        var replaceQueueTask = queueReplacementRuntime.ReplaceQueueAsync(
            items,
            currentTrackIndex,
            currentPosition,
            playWhenReady);
        ObserveMediaCommand(
            "Playback runtime.ReplaceQueue preserving current song",
            replaceQueueTask,
            DescribeMediaItem,
            CurrentSong,
            null);

        try
        {
            await replaceQueueTask.ConfigureAwait(false);
        }
        catch
        {
            return;
        }

        if (playWhenReady)
        {
            ScheduleBufferingStallRecoveryAfterQueueRebuild(requestGeneration, playlistSnapshot, currentTrackIndex);
        }
    }

    private void ScheduleBufferingStallRecoveryAfterQueueRebuild(int requestGeneration, List<SongDto> playlistSnapshot, int trackIndex)
    {
        if (!IsPlaybackRequestCurrent(requestGeneration) ||
            !ReferenceEquals(_playlist, playlistSnapshot) ||
            _currentTrackIndex != trackIndex ||
            CurrentSong == null ||
            _playbackRuntime.State != PlaybackRuntimeState.Buffering)
        {
            return;
        }

        ScheduleBufferingStallRecovery("queue rebuild completed while media manager remained buffering");
    }

    private void BeginQueueSelectionSuppression(int startIndex, int requestGeneration)
    {
        if (startIndex <= 0)
        {
            return;
        }

        Volatile.Write(ref _queueSelectionSuppressionStartIndex, startIndex);
        Volatile.Write(ref _queueSelectionSuppressionRequestGeneration, requestGeneration);
        Volatile.Write(ref _queueSelectionSuppressionExpiresUtcTicks, 0);
    }

    private void EndQueueSelectionSuppression(int requestGeneration)
    {
        if (Volatile.Read(ref _queueSelectionSuppressionRequestGeneration) != requestGeneration)
        {
            return;
        }

        var expiresUtcTicks = DateTime.UtcNow.Add(QueueSelectionRewindSuppressionGrace).Ticks;
        Volatile.Write(ref _queueSelectionSuppressionExpiresUtcTicks, expiresUtcTicks);
        _ = ClearQueueSelectionSuppressionAfterGraceAsync(requestGeneration, expiresUtcTicks);
    }

    private async Task ClearQueueSelectionSuppressionAfterGraceAsync(int requestGeneration, long expiresUtcTicks)
    {
        await Task.Delay(QueueSelectionRewindSuppressionGrace).ConfigureAwait(false);

        if (Volatile.Read(ref _queueSelectionSuppressionRequestGeneration) != requestGeneration ||
            Volatile.Read(ref _queueSelectionSuppressionExpiresUtcTicks) != expiresUtcTicks)
        {
            return;
        }

        Volatile.Write(ref _queueSelectionSuppressionStartIndex, -1);
        Volatile.Write(ref _queueSelectionSuppressionRequestGeneration, 0);
        Volatile.Write(ref _queueSelectionSuppressionExpiresUtcTicks, 0);
    }

    private void ClearQueueSelectionSuppression()
    {
        Volatile.Write(ref _queueSelectionSuppressionStartIndex, -1);
        Volatile.Write(ref _queueSelectionSuppressionRequestGeneration, 0);
        Volatile.Write(ref _queueSelectionSuppressionExpiresUtcTicks, 0);
    }

    private async Task<IReadOnlyDictionary<int, TrackCacheStatus>> ResolveQueueCacheStatusesAsync(
        IReadOnlyList<SongDto> playlistSnapshot,
        CancellationToken cancellationToken)
    {
        var statuses = await _audioCacheService
            .GetCacheStatusesAsync(playlistSnapshot, cancellationToken)
            .ConfigureAwait(false);
        foreach (var status in statuses.Values)
        {
            _cacheStatusSnapshot[status.SongId] = status;
        }

        return statuses;
    }

    private static string ResolveImmediatePlaybackUri(SongDto song, TrackCacheStatus status) =>
        status.IsLocalReady && !string.IsNullOrWhiteSpace(status.LocalPlaybackUri)
            ? status.LocalPlaybackUri
            : song.StreamUrl ?? string.Empty;

    private bool CanStartRequestedSong(SongDto song, TrackCacheStatus status)
    {
        if (status.IsLocalReady)
        {
            return true;
        }

        return _networkStatusService?.IsOffline != true &&
            !string.IsNullOrWhiteSpace(song.StreamUrl);
    }

    private void PublishUnavailableOffline(SongDto song)
    {
        _logger.LogInformation(
            "Playback request rejected because the song is not cached and internet access is unavailable. SongId={SongId}",
            song.Id);
        PlaybackRequestFailed?.Invoke(
            this,
            new PlaybackRequestFailedEventArgs(song.Id, PlaybackRequestFailureReason.UnavailableOffline));
    }

    private void StartSingleSongPlayback(
        SongDto song,
        int requestGeneration,
        TrackCacheStatus cacheStatus)
    {
        try
        {
            ClearSongPlaybackUriMap();
            var mediaItem = CreateMediaItem(song, cacheStatus, ResolveImmediatePlaybackUri(song, cacheStatus));

            if (!IsPlaybackRequestCurrent(requestGeneration) || CurrentSong?.Id != song.Id)
            {
                _logger.LogInformation(
                    "PlaySong start skipped because a newer playback request superseded it. SongId={SongId}; RequestGeneration={RequestGeneration}; CurrentGeneration={CurrentGeneration}; {Snapshot}",
                    song.Id,
                    requestGeneration,
                    Volatile.Read(ref _playbackRequestGeneration),
                    CreatePlaybackSnapshot(CurrentSong, null));
                return;
            }

            ObserveMediaCommand("Playback runtime.Play single song", _playbackRuntime.PlayAsync(mediaItem), DescribeMediaItem, song, mediaItem);
            StartQueuePreparation([song], 0, QueuePreparationMode.Normal);
            WarmPlaybackCacheInBackground([song], 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start playback for song {SongId}", song.Id);
        }
    }

    private void WarmPlaybackCacheInBackground(IReadOnlyList<SongDto> playlistSnapshot, int startIndex)
    {
        if (playlistSnapshot.Count == 0 || startIndex < 0 || startIndex >= playlistSnapshot.Count)
        {
            return;
        }

        var warmTargets = new List<SongDto>(Math.Min(BackgroundWarmAheadTrackCount, playlistSnapshot.Count - startIndex));
        for (var index = startIndex; index < playlistSnapshot.Count && warmTargets.Count < BackgroundWarmAheadTrackCount; index++)
        {
            warmTargets.Add(playlistSnapshot[index]);
        }

        _ = WarmPlaybackCacheInBackgroundAsync(warmTargets);
    }

    private async Task WarmPlaybackCacheInBackgroundAsync(IReadOnlyList<SongDto> songs)
    {
        if (songs.Count == 0)
        {
            return;
        }

        using var concurrencyGate = new SemaphoreSlim(QueueCacheResolutionConcurrency);
        var warmTasks = songs.Select(song => WarmPlaybackUriAsync(song, concurrencyGate));

        try
        {
            await Task.WhenAll(warmTasks).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Background playback cache warming encountered an unexpected failure.");
        }
    }

    private async Task WarmPlaybackUriAsync(SongDto song, SemaphoreSlim concurrencyGate)
    {
        await concurrencyGate.WaitAsync().ConfigureAwait(false);

        try
        {
            await _audioCacheService.ResolvePlaybackUriAsync(song).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Background playback cache warm failed for song {SongId}", song.Id);
        }
        finally
        {
            concurrencyGate.Release();
        }
    }

    private PlaybackMediaItem CreateMediaItem(
        SongDto song,
        TrackCacheStatus cacheStatus,
        string? playbackUri = null)
    {
        var mediaUri = string.IsNullOrWhiteSpace(playbackUri)
            ? song.StreamUrl ?? string.Empty
            : playbackUri;
        var (artworkUri, nowPlayingArtworkUri, nowPlayingArtworkVersion) = ResolveArtworkUris(song);
        RegisterSongPlaybackUri(song, mediaUri);

        var isLocal = IsLocalPlaybackUri(mediaUri);
        var mediaItem = new PlaybackMediaItem(mediaUri, song.Id, cacheStatus.StableCacheKey)
        {
            Title = song.SongTitle ?? string.Empty,
            Artist = song.ArtistName ?? string.Empty,
            ImageUri = artworkUri,
            AlbumImageUri = nowPlayingArtworkUri,
            AlbumImageContentVersion = nowPlayingArtworkVersion,
            IsLocal = isLocal,
            IsSleepSafe = cacheStatus.IsLocalReady || isLocal
        };

        return mediaItem;
    }

    /// <summary>
    /// The artwork URI handed to Android's Media3 media session for the notification and lock screen.
    /// Apple's now-playing surface takes a different rendition - see
    /// <see cref="ResolveNowPlayingArtworkUri"/>.
    ///
    /// <para>
    /// A locally cached file:// URI is preferred so notification artwork renders offline - Media3's
    /// default bitmap loader resolves file:// through FileDataSource without touching the network.
    /// Offline with nothing cached, this returns empty rather than a remote URL, so the loader never
    /// stalls on the media thread waiting for a request that cannot succeed.
    /// </para>
    ///
    /// <para>
    /// The small pre-resized rendition is preferred over the full-size original throughout. Media3 is
    /// given a bare URI and uses its default bitmap loader, so whatever is named here is decoded on
    /// the media thread at full resolution - a multi-megabyte cover is an expensive decode at exactly
    /// the moment the player is starting. A notification icon is a couple of hundred pixels, so the
    /// thumb is both cheaper and entirely sufficient.
    /// </para>
    /// </summary>
    internal string ResolveAlbumImageUri(SongDto song, ArtworkProbeCache? probeCache = null) =>
        ResolveMediaImageUri(
            [
                (song.AlbumArtThumbUrl, song.AlbumArtVersion),
                (song.AlbumArtUrl, song.AlbumArtVersion),
                (song.PersonaImageThumbUrl, song.PersonaImageVersion),
                (song.PersonaImageUrl, song.PersonaImageVersion)
            ],
            suppressRemoteWhenOffline: true,
            probeCache).Uri;

    /// <summary>
    /// Both artwork URIs for one queue item, sharing a single set of cache probes.
    ///
    /// <para>
    /// The two ladders overlap in four of their six candidates, and every probe is a real
    /// <c>File.Exists</c>. Sharing the results keeps building a long queue to one stat per distinct
    /// rendition instead of re-checking the same files twice per track.
    /// </para>
    ///
    /// <para>
    /// Android walks both ladders too, even though <c>ToMedia3Item</c> reads only <c>ImageUri</c> and
    /// the now-playing value is discarded there. That is deliberate: with the probes shared the extra
    /// cost is a handful of stats per queue item, and it buys a single code path instead of an
    /// <c>#if ANDROID</c> branch that no test running on the net10.0 test host could ever reach.
    /// Android's guarantee rests on <c>ImageUri</c>'s ladder, which is pinned by tests.
    /// </para>
    /// </summary>
    internal (string MediaSessionUri, string NowPlayingUri, int NowPlayingContentVersion) ResolveArtworkUris(SongDto song)
    {
        var probeCache = new ArtworkProbeCache();
        var mediaSessionUri = ResolveAlbumImageUri(song, probeCache);
        var nowPlaying = ResolveNowPlayingArtwork(song, probeCache);
        return (mediaSessionUri, nowPlaying.Uri, nowPlaying.ContentVersion);
    }

    /// <summary>
    /// The now-playing artwork URI together with the content version of the rendition that won.
    ///
    /// <para>
    /// The version travels with the URI because a remote artwork URL alone cannot be cached correctly:
    /// <c>StableRemoteAssetKey</c> keys on the blob path plus the version, so caching a hero under
    /// version 0 would write a duplicate file under the wrong key, waste budget, and still serve
    /// pre-crop artwork after a re-crop. It also differs per candidate - album renditions carry
    /// <c>AlbumArtVersion</c>, persona ones <c>PersonaImageVersion</c> - so it cannot be inferred
    /// downstream from the URI.
    /// </para>
    /// </summary>
    internal ResolvedArtwork ResolveNowPlayingArtwork(SongDto song, ArtworkProbeCache? probeCache = null) =>
        ResolveMediaImageUri(
            [
                (song.AlbumArtHeroUrl, song.AlbumArtVersion),
                (song.AlbumArtThumbUrl, song.AlbumArtVersion),
                (song.AlbumArtUrl, song.AlbumArtVersion),
                (song.PersonaImageHeroUrl, song.PersonaImageVersion),
                (song.PersonaImageThumbUrl, song.PersonaImageVersion),
                (song.PersonaImageUrl, song.PersonaImageVersion)
            ],
            suppressRemoteWhenOffline: false,
            probeCache);

    /// <summary>An artwork URI and the content version of the rendition it came from.</summary>
    internal readonly record struct ResolvedArtwork(string Uri, int ContentVersion)
    {
        public static ResolvedArtwork None => new(string.Empty, 0);
    }

    /// <summary>Memoises <c>TryGetCachedImagePath</c> results for one queue item.</summary>
    internal sealed class ArtworkProbeCache
    {
        private readonly Dictionary<(string Url, int Version), string?> _probes = [];

        public string? GetOrProbe(string url, int version, Func<string, int, string?> probe)
        {
            if (_probes.TryGetValue((url, version), out var cachedPath))
            {
                return cachedPath;
            }

            cachedPath = probe(url, version);
            _probes[(url, version)] = cachedPath;
            return cachedPath;
        }
    }

    /// <summary>
    /// The artwork URI for a now-playing surface that renders it large - Apple's lock screen and
    /// Control Center. Two deliberate differences from <see cref="ResolveAlbumImageUri"/>:
    ///
    /// <para>
    /// The 640px hero rendition is preferred over the 320px thumb. The thumb exists because Media3
    /// decodes whatever URI it is given on the media thread for a notification icon a couple of
    /// hundred pixels wide. Apple's surface draws artwork near full-screen, where a thumb is visibly
    /// soft, and the decode happens on a thread pool thread in <c>AppleNowPlayingArtworkLoader</c>
    /// rather than anywhere that could stall playback.
    /// </para>
    ///
    /// <para>
    /// There is no offline gate. Returning empty while offline is right for Media3, whose bitmap
    /// loader would otherwise stall on the media thread. Nothing analogous applies here:
    /// <c>NowPlayingArtworkCoordinator</c> declines to fetch a remote URI with no network access, and
    /// does so without consuming a retry attempt, so keeping the URL means artwork appears on its own
    /// when connectivity returns mid-track instead of staying blank until the queue is next rebuilt.
    /// </para>
    /// </summary>
    internal string ResolveNowPlayingArtworkUri(SongDto song, ArtworkProbeCache? probeCache = null) =>
        ResolveNowPlayingArtwork(song, probeCache).Uri;

    private ResolvedArtwork ResolveMediaImageUri(
        ReadOnlySpan<(string? Url, int Version)> candidates,
        bool suppressRemoteWhenOffline,
        ArtworkProbeCache? probeCache = null)
    {
        foreach (var (url, version) in candidates)
        {
            if (TryResolveCachedMediaImageUri(url, version, out var cachedUri, probeCache))
            {
                return new ResolvedArtwork(cachedUri, version);
            }
        }

        if (suppressRemoteWhenOffline && _networkStatusService?.HasNoNetworkAccess == true)
        {
            return ResolvedArtwork.None;
        }

        foreach (var (url, version) in candidates)
        {
            if (TryResolveMediaImageUri(url, out var remoteUri))
            {
                return new ResolvedArtwork(remoteUri, version);
            }
        }

        return ResolvedArtwork.None;
    }

    private bool TryResolveCachedMediaImageUri(
        string? remoteImageUrl,
        int contentVersion,
        out string resolvedUri,
        ArtworkProbeCache? probeCache = null)
    {
        resolvedUri = string.Empty;

        if (string.IsNullOrWhiteSpace(remoteImageUrl))
        {
            return false;
        }

        var cachedPath = probeCache is null
            ? _imageCacheService?.TryGetCachedImagePath(remoteImageUrl, contentVersion)
            : probeCache.GetOrProbe(
                remoteImageUrl,
                contentVersion,
                (url, version) => _imageCacheService?.TryGetCachedImagePath(url, version));

        if (string.IsNullOrWhiteSpace(cachedPath))
        {
            return false;
        }

        try
        {
            // TryResolveMediaImageUri only accepts a file:// URI, not a bare path.
            resolvedUri = new Uri(cachedPath).AbsoluteUri;
            return true;
        }
        catch (UriFormatException)
        {
            return false;
        }
    }

    private static bool TryResolveMediaImageUri(string? candidate, out string resolvedUri)
    {
        resolvedUri = string.Empty;

        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        {
            resolvedUri = uri.ToString();
            return true;
        }

        if (uri.Scheme != Uri.UriSchemeFile || !candidate.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        resolvedUri = uri.ToString();
        return true;
    }

    private static bool IsLocalPlaybackUri(string? mediaUri)
    {
        return !string.IsNullOrWhiteSpace(mediaUri) &&
            (Path.IsPathRooted(mediaUri) || mediaUri.StartsWith("file://", StringComparison.OrdinalIgnoreCase));
    }

    private TimeSpan ResolveCurrentPlaybackPosition()
    {
        try
        {
            return _playbackRuntime.Position;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Unable to read playback runtime position while preserving queue; using last observed position.");
            return _playbackPosition;
        }
    }

    private string? TryGetNativeCurrentMediaUri()
    {
        try
        {
            return _playbackRuntime.Queue?.Current?.MediaUri;
        }
        catch
        {
            return null;
        }
    }

    private void RegisterSongPlaybackUri(SongDto song, string playbackUri)
    {
        lock (_urlToSongSync)
        {
            if (!string.IsNullOrWhiteSpace(song.StreamUrl))
            {
                _urlToSong[song.StreamUrl] = song;
            }

            if (!string.IsNullOrWhiteSpace(playbackUri))
            {
                _urlToSong[playbackUri] = song;
            }
        }
    }

    private void ClearSongPlaybackUriMap()
    {
        lock (_urlToSongSync)
        {
            _urlToSong.Clear();
        }
    }

    private int SongPlaybackUriMapCount()
    {
        lock (_urlToSongSync)
        {
            return _urlToSong.Count;
        }
    }

    private List<SongDto> BuildPlaybackPlaylist(IReadOnlyList<SongDto> sourceSongs, int currentSongId)
    {
        if (!_isShuffleEnabled)
        {
            return new List<SongDto>(sourceSongs);
        }

        var shuffledSongs = sourceSongs.ToList();
        var currentSongIndex = shuffledSongs.FindIndex(song => song.Id == currentSongId);
        if (currentSongIndex < 0)
        {
            ShuffleSongs(shuffledSongs);
            return shuffledSongs;
        }

        var currentSong = shuffledSongs[currentSongIndex];
        shuffledSongs.RemoveAt(currentSongIndex);
        ShuffleSongs(shuffledSongs);
        shuffledSongs.Insert(0, currentSong);
        return shuffledSongs;
    }

    private void ShuffleSongs(List<SongDto> songs)
    {
        for (var index = songs.Count - 1; index > 0; index--)
        {
            var swapIndex = _random.Next(index + 1);
            (songs[index], songs[swapIndex]) = (songs[swapIndex], songs[index]);
        }
    }

    private static int ResolveRequiredPlaylistIndex(IReadOnlyList<SongDto> playlist, int songId)
    {
        for (var index = 0; index < playlist.Count; index++)
        {
            if (playlist[index].Id == songId)
            {
                return index;
            }
        }

        throw new InvalidOperationException($"Song {songId} was not found in the active playback playlist.");
    }

    private int? ResolveSequentialNextTrackIndex(int finishedTrackIndex)
    {
        if (_playlist == null || _playlist.Count == 0)
            return null;

        if (finishedTrackIndex < _playlist.Count - 1)
            return finishedTrackIndex + 1;

        return IsRepeatEnabled ? 0 : null;
    }

    private int? ResolveSequentialPreviousTrackIndex(int currentTrackIndex)
    {
        if (_playlist == null || _playlist.Count == 0)
            return null;

        if (currentTrackIndex > 0 && currentTrackIndex < _playlist.Count)
            return currentTrackIndex - 1;

        return currentTrackIndex == 0 && IsRepeatEnabled
            ? _playlist.Count - 1
            : null;
    }

    private bool ShouldRecoverAdvancedPlaylistTrack()
    {
        var state = _playbackRuntime.State;
        return state == PlaybackRuntimeState.Failed ||
               (state == PlaybackRuntimeState.Buffering && !IsPlaying);
    }

    private bool ShouldKeepPlaybackActiveDuringFailedState()
    {
        if (!IsPlaying || !HasPlaylist || _playlist == null || CurrentSong == null)
        {
            return false;
        }

        return ResolveSequentialNextTrackIndex(_currentTrackIndex).HasValue;
    }

    private bool ShouldForceQueueReloadForRequestedTrack(int requestedIndex)
    {
        if (_playlist == null || requestedIndex < 0 || requestedIndex >= _playlist.Count)
        {
            return false;
        }

        if (requestedIndex != _currentTrackIndex)
        {
            return false;
        }

        var nativeCurrentIndex = TryResolveNativeQueueIndex();
        if (nativeCurrentIndex.HasValue && nativeCurrentIndex.Value != requestedIndex)
        {
            return false;
        }

        return ShouldRecoverAdvancedPlaylistTrack();
    }

    private bool ShouldForceQueueReloadForNativeQueueMismatch(int requestedIndex)
    {
        if (_playlist == null || requestedIndex < 0 || requestedIndex >= _playlist.Count)
        {
            return false;
        }

        var queue = _playbackRuntime.Queue;
        if (queue == null)
        {
            return _playbackRuntime is IIndexedQueuePlaybackRuntime;
        }

        try
        {
            var nativeCount = queue.Count;
            if (nativeCount <= 0)
            {
                return _playbackRuntime is IIndexedQueuePlaybackRuntime;
            }

            if (requestedIndex >= nativeCount || nativeCount != _playlist.Count)
            {
                return true;
            }

            var index = 0;
            foreach (var nativeItem in queue)
            {
                if (index >= _playlist.Count)
                {
                    return true;
                }

                var expectedSong = _playlist[index];
                var expectedCacheKey = _audioCacheService.GetStableCacheKey(expectedSong);
                if (!string.Equals(nativeItem.StableCacheKey, expectedCacheKey, StringComparison.Ordinal) &&
                    !MediaUrisMatch(expectedSong.StreamUrl, nativeItem.MediaUri))
                {
                    return true;
                }

                index++;
            }

            return index != _playlist.Count;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Unable to inspect native playback queue for mismatch detection.");
            return false;
        }
    }

    private bool ShouldIgnoreMediaItemFinished(PlaybackMediaItem? mediaItem)
    {
        if (CurrentSong == null)
        {
            return false;
        }

        if (mediaItem != null && TryResolveSongFromMediaItem(mediaItem, out var resolvedSong, out var playlistIndex))
        {
            if (resolvedSong.Id != CurrentSong.Id)
            {
                _logger.LogWarning(
                    "MediaItemFinished ignored because event item does not match current song. FinishedSongId={FinishedSongId}; PlaylistIndex={PlaylistIndex}; {Snapshot}",
                    resolvedSong.Id,
                    playlistIndex,
                    CreatePlaybackSnapshot(CurrentSong, mediaItem));
                return true;
            }
        }

        if (_playbackDuration > TimeSpan.Zero && _playbackPosition + MediaItemFinishedNearEndTolerance < _playbackDuration)
        {
            _logger.LogWarning(
                "MediaItemFinished ignored because playback position is not near the track end. Position={Position}; Duration={Duration}; MediaState={MediaState}; {Snapshot}",
                _playbackPosition,
                _playbackDuration,
                _playbackRuntime.State,
                CreatePlaybackSnapshot(CurrentSong, mediaItem));
            return true;
        }

        if (_playbackPosition == TimeSpan.Zero && _playbackDuration == TimeSpan.Zero)
        {
            _logger.LogWarning(
                "MediaItemFinished ignored because playback has not reported any position or duration yet. MediaState={MediaState}; {Snapshot}",
                _playbackRuntime.State,
                CreatePlaybackSnapshot(CurrentSong, mediaItem));
            return true;
        }

        return false;
    }

    private void CancelPendingPlaylistAdvance()
    {
        _playlistAdvanceGeneration++;
    }

    private bool TryResolveSongFromMediaItem(PlaybackMediaItem mediaItem, out SongDto song, out int? playlistIndex)
    {
        SongDto? resolved = null;
        if (!string.IsNullOrWhiteSpace(mediaItem.MediaUri))
        {
            lock (_urlToSongSync)
            {
                _urlToSong.TryGetValue(mediaItem.MediaUri, out resolved);
            }
        }

        if (resolved != null)
        {
            song = resolved;
            playlistIndex = ResolvePlaylistIndex(song.Id);
            _logger.LogInformation(
                "TryResolveSongFromMediaItem resolved from URL map. ResolvedSongId={ResolvedSongId}; PlaylistIndex={PlaylistIndex}; UrlMapCount={UrlMapCount}; MediaItem={MediaItem}; {Snapshot}",
                song.Id,
                playlistIndex,
                SongPlaybackUriMapCount(),
                DescribeMediaItem(mediaItem),
                CreatePlaybackSnapshot(CurrentSong, mediaItem));
            return true;
        }

        if (_playlist != null)
        {
            var mediaItemPlaylistIndex = ResolvePlaylistIndex(mediaItem);
            if (mediaItemPlaylistIndex.HasValue)
            {
                playlistIndex = mediaItemPlaylistIndex.Value;
                song = _playlist[mediaItemPlaylistIndex.Value];
                _logger.LogInformation(
                    "TryResolveSongFromMediaItem resolved from playlist URI scan. ResolvedSongId={ResolvedSongId}; PlaylistIndex={PlaylistIndex}; MediaItem={MediaItem}; {Snapshot}",
                    song.Id,
                    playlistIndex,
                    DescribeMediaItem(mediaItem),
                    CreatePlaybackSnapshot(CurrentSong, mediaItem));
                return true;
            }

            var nativeQueueIndex = TryResolveNativeQueueIndex();
            if (nativeQueueIndex.HasValue)
            {
                playlistIndex = nativeQueueIndex.Value;
                song = _playlist[nativeQueueIndex.Value];
                _logger.LogInformation(
                    "TryResolveSongFromMediaItem resolved from native queue index fallback. ResolvedSongId={ResolvedSongId}; PlaylistIndex={PlaylistIndex}; MediaItem={MediaItem}; {Snapshot}",
                    song.Id,
                    playlistIndex,
                    DescribeMediaItem(mediaItem),
                    CreatePlaybackSnapshot(CurrentSong, mediaItem));
                return true;
            }
        }

        song = null!;
        playlistIndex = null;
        return false;
    }

    private int? TryResolveNativeQueueIndex()
    {
        if (_playlist == null)
            return null;

        try
        {
            var queue = _playbackRuntime.Queue;
            if (queue?.HasCurrent != true)
            {
                _logger.LogInformation("Native queue index unavailable because Playback runtime.Queue.HasCurrent is false. {Snapshot}", CreatePlaybackSnapshot(CurrentSong, null));
                return null;
            }

            var currentIndex = queue.CurrentIndex;
            if (currentIndex >= 0 && currentIndex < _playlist.Count)
            {
                _logger.LogInformation("Native queue index resolved from CurrentIndex. CurrentIndex={CurrentIndex}; {Snapshot}", currentIndex, CreatePlaybackSnapshot(CurrentSong, queue.Current));
                return currentIndex;
            }

            var resolvedIndex = ResolvePlaylistIndex(queue.Current);
            _logger.LogInformation("Native queue index resolved from Current media item. CurrentIndex={CurrentIndex}; ResolvedIndex={ResolvedIndex}; {Snapshot}", currentIndex, resolvedIndex, CreatePlaybackSnapshot(CurrentSong, queue.Current));
            return resolvedIndex;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Native queue index resolution failed. {Snapshot}", CreatePlaybackSnapshot(CurrentSong, null));
            return null;
        }
    }

    private int? ResolvePlaylistIndex(int songId)
    {
        if (_playlist == null)
            return null;

        var index = _playlist.FindIndex(song => song.Id == songId);
        return index >= 0 ? index : null;
    }

    private int? ResolvePlaylistIndex(PlaybackMediaItem? mediaItem)
    {
        if (_playlist == null || mediaItem == null)
            return null;

        for (var index = 0; index < _playlist.Count; index++)
        {
            if (MediaUrisMatch(_playlist[index].StreamUrl, mediaItem.MediaUri))
            {
                return index;
            }
        }

        return null;
    }

    private static bool MediaUrisMatch(string? first, string? second)
    {
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
            return false;

        if (string.Equals(first, second, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!Uri.TryCreate(first, UriKind.Absolute, out var firstUri) ||
            !Uri.TryCreate(second, UriKind.Absolute, out var secondUri))
        {
            return false;
        }

        return string.Equals(firstUri.Scheme, secondUri.Scheme, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(firstUri.Host, secondUri.Host, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(firstUri.AbsolutePath, secondUri.AbsolutePath, StringComparison.OrdinalIgnoreCase);
    }

    private void LogPlaybackSnapshot(string message, SongDto? song, PlaybackMediaItem? mediaItem)
    {
        _logger.LogInformation("{Message}. {Snapshot}", message, CreatePlaybackSnapshot(song, mediaItem));
    }

    private string CreatePlaybackSnapshot(SongDto? song, PlaybackMediaItem? mediaItem)
    {
        var sequence = Interlocked.Increment(ref _playbackDiagnosticSequence);
        var queueInfo = GetNativeQueueSnapshot(mediaItem);
        return $"DiagSeq={sequence}; " +
               $"AppSongId={song?.Id.ToString() ?? CurrentSong?.Id.ToString() ?? "null"}; " +
               $"CurrentSongId={CurrentSong?.Id.ToString() ?? "null"}; " +
               $"CurrentTrackIndex={_currentTrackIndex}; " +
               $"HasPlaylist={HasPlaylist}; " +
               $"PlaylistCount={_playlist?.Count ?? 0}; " +
               $"IsShuffleEnabled={IsShuffleEnabled}; " +
               $"IsRepeatEnabled={IsRepeatEnabled}; " +
               $"IsPlaying={IsPlaying}; " +
               $"PlaybackRuntimeState={_playbackRuntime.State}; " +
               $"LastObservedPlaybackRuntimeState={_lastObservedPlaybackRuntimeState?.ToString() ?? "null"}; " +
               $"Position={_playbackPosition:c}; " +
               $"Duration={_playbackDuration:c}; " +
               $"AdvanceGeneration={_playlistAdvanceGeneration}; " +
               $"UrlMapCount={SongPlaybackUriMapCount()}; " +
               $"AppPlaylist={DescribePlaylistForLog()}; " +
               queueInfo;
    }

    private string GetNativeQueueSnapshot(PlaybackMediaItem? mediaItem)
    {
        try
        {
            var queue = _playbackRuntime.Queue;
            return $"NativeQueueType={queue?.GetType().FullName ?? "null"}; " +
                   $"NativeHasCurrent={queue?.HasCurrent}; " +
                   $"NativeCurrentIndex={queue?.CurrentIndex}; " +
                   $"NativeCurrentItem={DescribeMediaItem(queue?.Current)}; " +
                   $"NativeHasNext={DescribeNativeQueueProperty(queue, "HasNext")}; " +
                   $"NativeNextItem={DescribeNativeQueueMediaItemProperty(queue, "Next")}; " +
                   $"NativeHasPrevious={DescribeNativeQueueProperty(queue, "HasPrevious")}; " +
                   $"NativePreviousItem={DescribeNativeQueueMediaItemProperty(queue, "Previous")}; " +
                   $"NativeCount={DescribeNativeQueueProperty(queue, "Count")}; " +
                   $"NativeQueueItems={DescribeNativeQueueItems(queue)}; " +
                   $"EventMediaItem={DescribeMediaItem(mediaItem)}";
        }
        catch (Exception ex)
        {
            return $"NativeQueueSnapshotError={ex.GetType().Name}; EventMediaItem={DescribeMediaItem(mediaItem)}";
        }
    }

    private void ObserveMediaCommand(string operation, Task task, SongDto? song, PlaybackMediaItem? mediaItem)
    {
        _ = task.ContinueWith(continuation =>
        {
            if (continuation.IsCanceled)
            {
                _logger.LogWarning("{Operation} canceled. {Snapshot}", operation, CreatePlaybackSnapshot(song, mediaItem));
                return;
            }

            if (continuation.IsFaulted)
            {
                _logger.LogError(continuation.Exception, "{Operation} faulted. {Snapshot}", operation, CreatePlaybackSnapshot(song, mediaItem));
                return;
            }

            _logger.LogInformation("{Operation} completed. {Snapshot}", operation, CreatePlaybackSnapshot(song, mediaItem));
        }, TaskScheduler.Default);
    }

    private void ObserveMediaCommand<T>(string operation, Task<T> task, Func<T, string> describeResult, SongDto? song, PlaybackMediaItem? mediaItem)
    {
        _ = task.ContinueWith(continuation =>
        {
            if (continuation.IsCanceled)
            {
                _logger.LogWarning("{Operation} canceled. {Snapshot}", operation, CreatePlaybackSnapshot(song, mediaItem));
                return;
            }

            if (continuation.IsFaulted)
            {
                _logger.LogError(continuation.Exception, "{Operation} faulted. {Snapshot}", operation, CreatePlaybackSnapshot(song, mediaItem));
                return;
            }

            _logger.LogInformation(
                "{Operation} completed. Result={Result}; {Snapshot}",
                operation,
                describeResult(continuation.Result),
                CreatePlaybackSnapshot(song, mediaItem));
        }, TaskScheduler.Default);
    }

    private string DescribePlaylistForLog()
    {
        if (_playlist == null || _playlist.Count == 0)
        {
            return "[]";
        }

        var entries = _playlist
            .Take(MaxLoggedPlaylistItems)
            .Select((playlistSong, index) => $"{index}{(index == _currentTrackIndex ? "*" : string.Empty)}:{playlistSong.Id}:{SanitizeLogText(playlistSong.SongTitle)}")
            .ToList();

        if (_playlist.Count > MaxLoggedPlaylistItems)
        {
            entries.Add($"...(+{_playlist.Count - MaxLoggedPlaylistItems} more)");
        }

        return $"[{string.Join("|", entries)}]";
    }

    private static string DescribeSongIds(IReadOnlyList<SongDto> songs)
    {
        var songIds = songs
            .Take(MaxLoggedPlaylistItems)
            .Select(song => song.Id.ToString())
            .ToList();
        if (songs.Count > MaxLoggedPlaylistItems)
        {
            songIds.Add($"...(+{songs.Count - MaxLoggedPlaylistItems} more)");
        }

        return string.Join(",", songIds);
    }

    private static string DescribeMediaItems(IEnumerable<PlaybackMediaItem> items)
    {
        var itemList = items as IReadOnlyList<PlaybackMediaItem> ?? items.ToList();
        var entries = itemList
            .Take(MaxLoggedPlaylistItems)
            .Select((item, index) => $"{index}:{DescribeMediaItem(item)}")
            .ToList();
        if (itemList.Count > MaxLoggedPlaylistItems)
        {
            entries.Add($"...(+{itemList.Count - MaxLoggedPlaylistItems} more)");
        }

        return $"[{string.Join("|", entries)}]";
    }

    private static string DescribeMediaItem(PlaybackMediaItem? mediaItem)
    {
        if (mediaItem == null)
        {
            return "null";
        }

        return $"Uri={SanitizeMediaUri(mediaItem.MediaUri)},Title={SanitizeLogText(mediaItem.Title)},Artist={SanitizeLogText(mediaItem.Artist)}";
    }

    private static string DescribeBooleanResult(bool result) => result ? "true" : "false";

    private static string DescribeNativeQueueProperty(object? queue, string propertyName)
    {
        if (queue == null)
        {
            return "null";
        }

        try
        {
            var property = queue.GetType().GetProperty(propertyName);
            if (property == null)
            {
                return "unavailable";
            }

            return SanitizeLogText(property.GetValue(queue)?.ToString());
        }
        catch (Exception ex)
        {
            return $"error:{ex.GetType().Name}";
        }
    }

    private static string DescribeNativeQueueMediaItemProperty(object? queue, string propertyName)
    {
        if (queue == null)
        {
            return "null";
        }

        try
        {
            var property = queue.GetType().GetProperty(propertyName);
            if (property == null)
            {
                return "unavailable";
            }

            return property.GetValue(queue) is PlaybackMediaItem mediaItem
                ? DescribeMediaItem(mediaItem)
                : SanitizeLogText(property.GetValue(queue)?.ToString());
        }
        catch (Exception ex)
        {
            return $"error:{ex.GetType().Name}";
        }
    }

    private static string DescribeNativeQueueItems(object? queue)
    {
        if (queue is not IEnumerable enumerable)
        {
            return "unavailable";
        }

        try
        {
            var items = new List<string>();
            var index = 0;
            var truncated = false;

            foreach (var entry in enumerable)
            {
                if (items.Count >= MaxLoggedNativeQueueItems)
                {
                    truncated = true;
                    break;
                }

                items.Add(entry is PlaybackMediaItem mediaItem
                    ? $"{index}:{DescribeMediaItem(mediaItem)}"
                    : $"{index}:{SanitizeLogText(entry?.ToString())}");
                index++;
            }

            if (items.Count == 0)
            {
                return "[]";
            }

            if (truncated)
            {
                items.Add("...");
            }

            return $"[{string.Join("|", items)}]";
        }
        catch (Exception ex)
        {
            return $"error:{ex.GetType().Name}";
        }
    }

    private static string SanitizeMediaUri(string? mediaUri)
    {
        if (string.IsNullOrWhiteSpace(mediaUri))
            return "null";

        if (!Uri.TryCreate(mediaUri, UriKind.Absolute, out var uri))
            return "non-absolute-uri";

        return $"{uri.Scheme}://{uri.Host}{uri.AbsolutePath}";
    }

    private static string SanitizeLogText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "null";
        }

        return value.Replace(";", ",", StringComparison.Ordinal).Replace("|", "/", StringComparison.Ordinal);
    }

    private static string NormalizeQueueSourceDescription(string? description)
    {
        return string.IsNullOrWhiteSpace(description)
            ? UnspecifiedQueueSourceDescription
            : SanitizeLogText(description.Trim());
    }

    private static string DescribeQueueItems(IReadOnlyList<SongDto> songs)
    {
        if (songs.Count == 0)
        {
            return "[]";
        }

        var items = songs
            .Take(MaxLoggedPlaylistItems)
            .Select(song => $"{song.Id}:{SanitizeLogText(song.SongTitle)} by {SanitizeLogText(song.ArtistName)}");
        var description = string.Join(" | ", items);

        return songs.Count > MaxLoggedPlaylistItems
            ? $"{description} | ... +{songs.Count - MaxLoggedPlaylistItems} more"
            : description;
    }

    // --- Helpers ---

    private void ResetPlaybackState()
    {
        var shouldSuppressStaleHighPosition = false;
        lock (_positionSync)
        {
            shouldSuppressStaleHighPosition = _playbackPosition.TotalSeconds >= PreviewLimitSeconds;
            _playbackProgress = 0;
            _formattedPosition = "0:00";
            _formattedDuration = "0:00";
            _playbackPosition = TimeSpan.Zero;
            _playbackDuration = TimeSpan.Zero;
            _continuousPlaybackSeconds = 0;
            _streamRecordedForCurrentSong = false;
            _skipNextStreamPositionSample = false;
        }

        Interlocked.Exchange(ref _lastPositionChangedUtcTicks, 0);
        if (shouldSuppressStaleHighPosition)
        {
            ArmStaleHighPositionSuppression();
        }
        else
        {
            Interlocked.Exchange(ref _staleHighPositionSuppressionExpiresUtcTicks, 0);
        }
        PreviewLimitReached = false;

        RaiseStateChanged(nameof(PlaybackProgress));
        RaiseStateChanged(nameof(FormattedPosition));
        RaiseStateChanged(nameof(FormattedDuration));
    }

    private void ArmStaleHighPositionSuppression()
    {
        Interlocked.Exchange(
            ref _staleHighPositionSuppressionExpiresUtcTicks,
            DateTime.UtcNow.Ticks + StaleHighPositionAfterTrackResetSuppression.Ticks);
    }

    private bool ShouldIgnoreStaleHighPositionAfterTrackReset(TimeSpan position)
    {
        if (position.TotalSeconds < PreviewLimitSeconds)
        {
            return false;
        }

        var expiresTicks = Volatile.Read(ref _staleHighPositionSuppressionExpiresUtcTicks);
        if (expiresTicks <= 0 || DateTime.UtcNow.Ticks > expiresTicks)
        {
            return false;
        }

        _logger.LogDebug(
            "Ignoring stale high playback position after track reset. Position={Position}; ExpiresUtcTicks={ExpiresUtcTicks}; {Snapshot}",
            position,
            expiresTicks,
            CreatePlaybackSnapshot(CurrentSong, null));
        return true;
    }

    private void RaiseStateChanged(string propertyName)
    {
        var handler = StateChanged;
        if (handler == null)
        {
            return;
        }

        // State mutations can complete on a thread-pool thread (async cache continuations use
        // ConfigureAwait(false)), but subscribers set XAML-bound properties directly, so the
        // notification must be delivered on the main thread. The single main-thread queue also
        // preserves the order in which property changes were raised.
        try
        {
            if (MainThread.IsMainThread)
            {
                handler(propertyName);
                return;
            }

            MainThread.BeginInvokeOnMainThread(() => handler(propertyName));
        }
        catch (Exception ex) when (ex is NotImplementedException || ex.GetType().Name == "NotImplementedInReferenceAssemblyException")
        {
            // Reference-assembly (unit-test) environment: no MAUI main thread — invoke inline.
            handler(propertyName);
        }
    }
}
