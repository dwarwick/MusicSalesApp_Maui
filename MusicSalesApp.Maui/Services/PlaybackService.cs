using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Services;

/// <summary>
/// Singleton playback service shared between MusicLibraryPage and SongPlayerPage.
/// Manages all playback state, stream tracking, preview limits, and repeat logic.
/// Does NOT own the MediaElement — communicates via events to the code-behind.
/// </summary>
public class PlaybackService : IPlaybackService
{
    private readonly IAuthService _authService;
    private readonly IMusicService _musicService;

    // Stream tracking state
    private int _streamQualifyingSeconds = 30;
    private int _streamTrackingSongId;
    private double _continuousPlaybackSeconds;
    private bool _streamRecordedForCurrentSong;

    // Playback position state
    private TimeSpan _playbackPosition;
    private TimeSpan _playbackDuration;

    // Preview limit state
    private const double PreviewLimitSeconds = 60.0;
    private const int MinPreviewInterval = 2;
    private const int MaxPreviewIntervalExclusive = 5;
    private int _previewEndCount;
    private int _nextCtaThreshold;
    private readonly Random _random = new();

    public PlaybackService(IAuthService authService, IMusicService musicService)
    {
        _authService = authService;
        _musicService = musicService;
        _nextCtaThreshold = 0; // show on first preview end
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
        private set { _isPlaying = value; RaiseStateChanged(nameof(IsPlaying)); }
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

    // --- Events ---

    public event Action<SongDto>? PlayRequested;
    public event Action? ResumeRequested;
    public event Action? PauseRequested;
    public event Action? StopRequested;
    public event Action<TimeSpan>? SeekRequested;
    public event Func<Task>? ShowSubscribeCtaRequested;
    public event Action<string>? StateChanged;

    // --- Actions ---

    public void PlaySong(SongDto song)
    {
        if (CurrentSong?.Id == song.Id && IsPlaying)
        {
            // Tapping the same song that's playing — pause it
            IsPlaying = false;
            PauseRequested?.Invoke();
            return;
        }

        var isSameSong = CurrentSong?.Id == song.Id;

        // Reset stream tracking for the new song
        ResetStreamTracking(song.Id);
        PreviewLimitReached = false;

        CurrentSong = song;
        IsPlaying = true;

        if (isSameSong)
        {
            // Same song replay (e.g., after preview limit) — seek to start and resume
            SeekRequested?.Invoke(TimeSpan.Zero);
            ResumeRequested?.Invoke();
        }
        else
        {
            PlayRequested?.Invoke(song);
        }
    }

    public void TogglePlayPause()
    {
        if (CurrentSong == null) return;

        IsPlaying = !IsPlaying;
        if (IsPlaying)
            ResumeRequested?.Invoke();
        else
            PauseRequested?.Invoke();
    }

    public void Stop()
    {
        IsPlaying = false;
        CurrentSong = null;
        ResetPlaybackState();
        StopRequested?.Invoke();
    }

    public void ToggleRepeat()
    {
        IsRepeatEnabled = !IsRepeatEnabled;
    }

    public void UpdatePosition(TimeSpan position, TimeSpan duration)
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

        TrackStreamPlayback(position, previousPosition);
        CheckPreviewLimit(position);
    }

    public TimeSpan GetSeekPosition(double progress)
    {
        return TimeSpan.FromSeconds(progress * _playbackDuration.TotalSeconds);
    }

    public void Seek(double progress)
    {
        var position = GetSeekPosition(progress);
        SeekRequested?.Invoke(position);
    }

    public void OnMediaEnded()
    {
        if (IsRepeatEnabled && CurrentSong != null)
        {
            // Restart the same song
            ResetStreamTracking(CurrentSong.Id);
            PreviewLimitReached = false;
            SeekRequested?.Invoke(TimeSpan.Zero);
            ResumeRequested?.Invoke();
            return;
        }

        IsPlaying = false;
    }

    public void SetStreamQualifyingSeconds(int seconds)
    {
        _streamQualifyingSeconds = seconds;
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
        _streamTrackingSongId = songId;
        _continuousPlaybackSeconds = 0;
        _streamRecordedForCurrentSong = false;
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

        if (CurrentSong.Id != _streamTrackingSongId)
        {
            ResetStreamTracking(CurrentSong.Id);
        }

        var elapsed = position.TotalSeconds - previousPosition.TotalSeconds;
        if (elapsed > 0 && elapsed < 2.0)
        {
            _continuousPlaybackSeconds += elapsed;
        }

        if (_continuousPlaybackSeconds >= _streamQualifyingSeconds)
        {
            _streamRecordedForCurrentSong = true;
            _ = _musicService.RecordStreamAsync(CurrentSong.Id);
        }
    }

    // --- Preview limit ---

    private bool ShouldEnforcePreviewLimit()
    {
        if (CurrentSong == null || !IsPlaying)
            return false;
        if (_authService.HasActiveSubscription)
            return false;
        if (_authService.IsCreator && CurrentSong.CreatorUserId == _authService.UserId)
            return false;
        return true;
    }

    private void CheckPreviewLimit(TimeSpan position)
    {
        if (!ShouldEnforcePreviewLimit())
            return;

        if (position.TotalSeconds >= PreviewLimitSeconds)
        {
            IsPlaying = false;
            PreviewLimitReached = true;
            PauseRequested?.Invoke();
            _previewEndCount++;

            if (_previewEndCount >= _nextCtaThreshold)
            {
                _nextCtaThreshold = _previewEndCount + _random.Next(MinPreviewInterval, MaxPreviewIntervalExclusive);
                _ = ShowSubscribeCtaRequested?.Invoke();
            }
        }
    }

    // --- Helpers ---

    private void ResetPlaybackState()
    {
        _playbackProgress = 0;
        _formattedPosition = "0:00";
        _formattedDuration = "0:00";
        _playbackPosition = TimeSpan.Zero;
        _playbackDuration = TimeSpan.Zero;
        _continuousPlaybackSeconds = 0;
        _streamRecordedForCurrentSong = false;
        PreviewLimitReached = false;

        RaiseStateChanged(nameof(PlaybackProgress));
        RaiseStateChanged(nameof(FormattedPosition));
        RaiseStateChanged(nameof(FormattedDuration));
    }

    private void RaiseStateChanged(string propertyName)
    {
        StateChanged?.Invoke(propertyName);
    }
}
