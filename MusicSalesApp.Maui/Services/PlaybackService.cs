using MediaManager;
using MediaManager.Library;
using MediaManager.Media;
using MediaManager.Playback;
using MediaManager.Player;
using MediaManager.Queue;
using MusicSalesApp.Maui.ViewModels;
using MmPositionChangedEventArgs = MediaManager.Playback.PositionChangedEventArgs;

namespace MusicSalesApp.Maui.Services;

/// <summary>
/// Singleton playback service shared between MusicLibraryPage and SongPlayerPage.
/// Manages all playback state, stream tracking, and preview limits.
/// Uses Plugin.MediaManager for actual audio output, foreground service, and
/// notification controls (Next/Previous buttons appear automatically from the queue).
/// </summary>
public class PlaybackService : IPlaybackService
{
    private readonly IAuthService _authService;
    private readonly IMusicService _musicService;
    private readonly IMediaManager _mediaManager;

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

    // Map MediaItem URL -> SongDto for auto-advance detection via MediaItemChanged
    private readonly Dictionary<string, SongDto> _urlToSong = new();

    public PlaybackService(IAuthService authService, IMusicService musicService, IMediaManager mediaManager)
    {
        _authService = authService;
        _musicService = musicService;
        _mediaManager = mediaManager;
        _nextCtaThreshold = 0;

        _mediaManager.StateChanged += OnMediaManagerStateChanged;
        _mediaManager.MediaItemChanged += OnMediaItemChanged;
        _mediaManager.PositionChanged += OnPositionChanged;
        _mediaManager.MediaItemFinished += OnMediaItemFinished;
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

    public event Func<Task>? ShowSubscribeCtaRequested;
    public event Action<string>? StateChanged;

    // --- Actions ---

    public void PlaySong(SongDto song)
    {
        if (CurrentSong?.Id == song.Id && IsPlaying)
        {
            // Tapping the same song that's playing — pause it
            IsPlaying = false;
            _ = _mediaManager.Pause();
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
            _ = _mediaManager.SeekTo(TimeSpan.Zero);
            _ = _mediaManager.Play();
        }
        else
        {
            _urlToSong.Clear();
            _urlToSong[song.StreamUrl ?? ""] = song;
            _ = _mediaManager.Play(CreateMediaItem(song));
        }
    }

    public void TogglePlayPause()
    {
        if (CurrentSong == null) return;

        IsPlaying = !IsPlaying;
        if (IsPlaying)
            _ = _mediaManager.Play();
        else
            _ = _mediaManager.Pause();
    }

    public void Stop()
    {
        IsPlaying = false;
        ResetPlaybackState();
        _ = _mediaManager.Pause();
        _ = _mediaManager.SeekTo(TimeSpan.Zero);
    }

    public void ToggleRepeat()
    {
        IsRepeatEnabled = !IsRepeatEnabled;
        _mediaManager.RepeatMode = IsRepeatEnabled ? RepeatMode.All : RepeatMode.Off;
    }

    internal void UpdatePosition(TimeSpan position, TimeSpan duration)
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
        _ = _mediaManager.SeekTo(position);
    }

    internal void OnMediaEnded()
    {
        if (!HasPlaylist && IsRepeatEnabled && CurrentSong != null)
        {
            // Single-song repeat: restart
            ResetStreamTracking(CurrentSong.Id);
            PreviewLimitReached = false;
            _ = _mediaManager.SeekTo(TimeSpan.Zero);
            _ = _mediaManager.Play();
            return;
        }

        if (!HasPlaylist)
            IsPlaying = false;
        // HasPlaylist: Plugin.MediaManager auto-advances through the queue.
        // OnMediaItemChanged updates state when the next song starts.
    }

    // --- Playlist methods ---

    public void SetPlaylist(List<SongDto> songs, int startIndex)
    {
        if (songs.Count == 0) return;

        _playlist = new List<SongDto>(songs);
        _currentTrackIndex = Math.Clamp(startIndex, 0, songs.Count - 1);

        RaiseStateChanged(nameof(HasPlaylist));
        RaiseStateChanged(nameof(Playlist));
        RaiseStateChanged(nameof(CurrentTrackIndex));

        var song = _playlist[_currentTrackIndex];
        ResetStreamTracking(song.Id);
        PreviewLimitReached = false;
        CurrentSong = song;
        IsPlaying = true;

        _mediaManager.RepeatMode = IsRepeatEnabled ? RepeatMode.All : RepeatMode.Off;
        _mediaManager.ShuffleMode = _isShuffleEnabled ? ShuffleMode.All : ShuffleMode.Off;

        BuildAndStartQueue(startIndex);
    }

    public void ClearPlaylist()
    {
        _playlist = null;
        _currentTrackIndex = 0;

        RaiseStateChanged(nameof(HasPlaylist));
        RaiseStateChanged(nameof(Playlist));
        RaiseStateChanged(nameof(CurrentTrackIndex));
    }

    public void PlayNext()
    {
        if (!HasPlaylist) return;
        _ = _mediaManager.PlayNext();
        // State updated via OnMediaItemChanged when Plugin.MediaManager advances
    }

    public void PlayPrevious()
    {
        if (!HasPlaylist) return;
        _ = _mediaManager.PlayPrevious();
        // State updated via OnMediaItemChanged when Plugin.MediaManager goes back
    }

    public void PlayTrackAtIndex(int index)
    {
        if (_playlist == null || index < 0 || index >= _playlist.Count)
            return;

        _currentTrackIndex = index;
        RaiseStateChanged(nameof(CurrentTrackIndex));

        var song = _playlist[index];
        ResetStreamTracking(song.Id);
        PreviewLimitReached = false;
        CurrentSong = song;
        IsPlaying = true;
        _ = _mediaManager.PlayQueueItem(index);
    }

    public void ToggleShuffle()
    {
        IsShuffleEnabled = !_isShuffleEnabled;
        _mediaManager.ShuffleMode = _isShuffleEnabled ? ShuffleMode.All : ShuffleMode.Off;
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
            _ = _mediaManager.Pause();
            _previewEndCount++;

            if (_previewEndCount >= _nextCtaThreshold)
            {
                _nextCtaThreshold = _previewEndCount + _random.Next(MinPreviewInterval, MaxPreviewIntervalExclusive);
                _ = ShowSubscribeCtaRequested?.Invoke();
            }
        }
    }

    // --- Plugin.MediaManager event handlers ---

    private void OnMediaManagerStateChanged(object? sender, StateChangedEventArgs e)
    {
        switch (e.State)
        {
            case MediaPlayerState.Playing:
                if (!IsPlaying) IsPlaying = true;
                break;
            case MediaPlayerState.Paused:
            case MediaPlayerState.Stopped:
                if (IsPlaying) IsPlaying = false;
                break;
            case MediaPlayerState.Failed:
                IsPlaying = false;
                break;
        }
    }

    private void OnMediaItemChanged(object? sender, MediaItemEventArgs e)
    {
        if (e.MediaItem == null) return;

        var url = e.MediaItem.MediaUri;
        if (string.IsNullOrEmpty(url) || !_urlToSong.TryGetValue(url, out var song)) return;

        // Skip if we already set this song (e.g., from PlayTrackAtIndex or PlaySong)
        if (song.Id == CurrentSong?.Id) return;

        // Auto-advance from Plugin.MediaManager (song ended naturally or user tapped Next in notification)
        if (_playlist != null)
        {
            var idx = _playlist.FindIndex(s => s.Id == song.Id);
            if (idx >= 0)
            {
                _currentTrackIndex = idx;
                RaiseStateChanged(nameof(CurrentTrackIndex));
            }
        }

        ResetStreamTracking(song.Id);
        PreviewLimitReached = false;
        CurrentSong = song;
        IsPlaying = true;
    }

    private void OnPositionChanged(object? sender, MmPositionChangedEventArgs e)
    {
        UpdatePosition(e.Position, _mediaManager.Duration);
    }

    private void OnMediaItemFinished(object? sender, MediaItemEventArgs e)
        => OnMediaEnded();

    // --- Queue helpers ---

    private void BuildAndStartQueue(int startIndex)
    {
        _urlToSong.Clear();
        var items = _playlist!.Select(s =>
        {
            var item = CreateMediaItem(s);
            _urlToSong[s.StreamUrl ?? ""] = s;
            return item;
        }).ToArray();

        var capturedStart = startIndex;
        _ = _mediaManager.Play((IEnumerable<IMediaItem>)items)
            .ContinueWith(t =>
            {
                if (!t.IsFaulted && capturedStart > 0)
                    return _mediaManager.PlayQueueItem(capturedStart);
                return Task.CompletedTask;
            })
            .Unwrap();
    }

    private IMediaItem CreateMediaItem(SongDto song)
    {
        return new MediaItem(song.StreamUrl ?? string.Empty)
        {
            Title = song.SongTitle ?? string.Empty,
            Artist = song.ArtistName ?? string.Empty,
            AlbumImageUri = song.AlbumArtUrl ?? string.Empty,
        };
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
