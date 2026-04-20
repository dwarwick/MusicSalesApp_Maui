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

    // Playlist state
    private List<SongDto>? _playlist;
    private int _currentTrackIndex;
    private bool _isShuffleEnabled;
    private List<int>? _shuffledTrackOrder;
    private int _currentShufflePosition;

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

    public List<SongDto>? Playlist => _playlist;

    public int CurrentTrackIndex => _currentTrackIndex;

    public bool HasPlaylist => _playlist != null && _playlist.Count > 0;

    public bool IsShuffleEnabled
    {
        get => _isShuffleEnabled;
        private set { _isShuffleEnabled = value; RaiseStateChanged(nameof(IsShuffleEnabled)); }
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
        // Playlist mode: auto-advance to next track
        if (HasPlaylist)
        {
            var nextIndex = GetNextTrackIndex();
            if (nextIndex.HasValue)
            {
                PlayTrackAtIndex(nextIndex.Value);
                return;
            }
            // End of playlist, no repeat — stop
        }
        else if (IsRepeatEnabled && CurrentSong != null)
        {
            // Single-song repeat (no playlist)
            ResetStreamTracking(CurrentSong.Id);
            PreviewLimitReached = false;
            SeekRequested?.Invoke(TimeSpan.Zero);
            ResumeRequested?.Invoke();
            return;
        }

        IsPlaying = false;
    }

    // --- Playlist methods ---

    public void SetPlaylist(List<SongDto> songs, int startIndex)
    {
        if (songs.Count == 0) return;

        _playlist = new List<SongDto>(songs);
        _currentTrackIndex = Math.Clamp(startIndex, 0, songs.Count - 1);

        if (_isShuffleEnabled)
            GenerateShuffleOrder();

        RaiseStateChanged(nameof(HasPlaylist));
        RaiseStateChanged(nameof(Playlist));
        RaiseStateChanged(nameof(CurrentTrackIndex));

        // Start playing the selected track
        var song = _playlist[_currentTrackIndex];
        ResetStreamTracking(song.Id);
        PreviewLimitReached = false;
        CurrentSong = song;
        IsPlaying = true;
        PlayRequested?.Invoke(song);
    }

    public void ClearPlaylist()
    {
        _playlist = null;
        _currentTrackIndex = 0;
        _shuffledTrackOrder = null;
        _currentShufflePosition = 0;

        RaiseStateChanged(nameof(HasPlaylist));
        RaiseStateChanged(nameof(Playlist));
        RaiseStateChanged(nameof(CurrentTrackIndex));
    }

    public void PlayNext()
    {
        if (!HasPlaylist) return;

        var nextIndex = GetNextTrackIndex();
        if (nextIndex.HasValue)
            PlayTrackAtIndex(nextIndex.Value);
    }

    public void PlayPrevious()
    {
        if (!HasPlaylist) return;

        var prevIndex = GetPreviousTrackIndex();
        if (prevIndex.HasValue)
            PlayTrackAtIndex(prevIndex.Value);
    }

    public void PlayTrackAtIndex(int index)
    {
        if (_playlist == null || index < 0 || index >= _playlist.Count)
            return;

        _currentTrackIndex = index;
        RaiseStateChanged(nameof(CurrentTrackIndex));

        // Update shuffle position if shuffle is active
        if (_isShuffleEnabled && _shuffledTrackOrder != null)
        {
            var shufflePos = _shuffledTrackOrder.IndexOf(index);
            if (shufflePos >= 0)
                _currentShufflePosition = shufflePos;
        }

        var song = _playlist[index];
        ResetStreamTracking(song.Id);
        PreviewLimitReached = false;
        CurrentSong = song;
        IsPlaying = true;
        PlayRequested?.Invoke(song);
    }

    public void ToggleShuffle()
    {
        IsShuffleEnabled = !_isShuffleEnabled;

        if (_isShuffleEnabled)
            GenerateShuffleOrder();
        else
            _shuffledTrackOrder = null;
    }

    private int? GetNextTrackIndex()
    {
        if (_playlist == null || _playlist.Count == 0) return null;

        if (_isShuffleEnabled && _shuffledTrackOrder != null)
        {
            var nextShufflePos = _currentShufflePosition + 1;
            if (nextShufflePos < _shuffledTrackOrder.Count)
                return _shuffledTrackOrder[nextShufflePos];

            // End of shuffle — if repeat, regenerate and start over
            if (IsRepeatEnabled)
            {
                GenerateShuffleOrder();
                return _shuffledTrackOrder.Count > 0 ? _shuffledTrackOrder[0] : null;
            }
            return null;
        }

        // Sequential mode
        var nextIndex = _currentTrackIndex + 1;
        if (nextIndex < _playlist.Count)
            return nextIndex;

        // End of playlist — if repeat, loop to start
        return IsRepeatEnabled ? 0 : null;
    }

    private int? GetPreviousTrackIndex()
    {
        if (_playlist == null || _playlist.Count == 0) return null;

        if (_isShuffleEnabled && _shuffledTrackOrder != null)
        {
            var prevShufflePos = _currentShufflePosition - 1;
            if (prevShufflePos >= 0)
                return _shuffledTrackOrder[prevShufflePos];
            return null; // At start of shuffle order
        }

        // Sequential mode
        var prevIndex = _currentTrackIndex - 1;
        return prevIndex >= 0 ? prevIndex : null;
    }

    private void GenerateShuffleOrder()
    {
        if (_playlist == null || _playlist.Count == 0)
        {
            _shuffledTrackOrder = null;
            return;
        }

        // Build list of all indices except current
        var remainingIndices = Enumerable.Range(0, _playlist.Count)
            .Where(i => i != _currentTrackIndex)
            .ToList();

        // Fisher-Yates shuffle
        for (int i = remainingIndices.Count - 1; i > 0; i--)
        {
            int j = _random.Next(i + 1);
            (remainingIndices[i], remainingIndices[j]) = (remainingIndices[j], remainingIndices[i]);
        }

        // Current track is always first in shuffle order
        _shuffledTrackOrder = new List<int> { _currentTrackIndex };
        _shuffledTrackOrder.AddRange(remainingIndices);
        _currentShufflePosition = 0;
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
