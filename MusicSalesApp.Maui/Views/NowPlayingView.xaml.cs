using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.Views;

/// <summary>
/// Reusable player bar. All dynamic state is controlled from code-behind because
/// IPlaybackService does not implement INotifyPropertyChanged — XAML bindings
/// to it are one-shot and never refresh.
/// </summary>
public partial class NowPlayingView : ContentView
{
    private const string PlayIconPathData = "M8 5v14l11-7z";
    private const string PauseIconPathData = "M6 19h4V5H6zm8-14v14h4V5z";
    private const int SongInfoUpdate = 1 << 0;
    private const int PlayPauseUpdate = 1 << 1;
    private const int RepeatUpdate = 1 << 2;
    private const int ProgressUpdate = 1 << 3;
    private const int TimeUpdate = 1 << 4;
    private const int PlaylistUpdate = 1 << 5;
    private const int ShuffleUpdate = 1 << 6;
    private const int PreviewUpdate = 1 << 7;
    private const int AllUpdates = SongInfoUpdate | PlayPauseUpdate | RepeatUpdate | ProgressUpdate |
                                   TimeUpdate | PlaylistUpdate | ShuffleUpdate | PreviewUpdate;
    private static readonly Microsoft.Maui.Controls.Shapes.Geometry? PlayIconGeometry = CreateGeometry(PlayIconPathData);
    private static readonly Microsoft.Maui.Controls.Shapes.Geometry? PauseIconGeometry = CreateGeometry(PauseIconPathData);

    private readonly NowPlayingEmptyStateActionRunner _emptyStateActionRunner = new();
    private readonly CoalescedUiUpdateScheduler _updateScheduler;
    private IPlaybackService? _playbackService;
    private IAuthService? _authService;
    private Func<Task<bool>>? _playFromEmptyStateAsync;
    private string? _emptyStateHint;
    private bool _isActive;

    /// <summary>
    /// Legacy property kept for XAML compatibility. The player now stays visible
    /// even when no song is selected.
    /// </summary>
    public bool CollapseWhenEmpty { get; set; } = true;

    public NowPlayingView()
    {
        InitializeComponent();
        _updateScheduler = new CoalescedUiUpdateScheduler(
            action => MainThread.BeginInvokeOnMainThread(action),
            ApplyScheduledPlaybackUpdates);

        PlayPauseTap.Tapped += OnPlayPauseClicked;
        RepeatTap.Tapped += OnRepeatClicked;
        ShuffleTap.Tapped += OnShuffleClicked;
        PrevTap.Tapped += OnPrevClicked;
        NextTap.Tapped += OnNextClicked;
        ProgressSlider.DragStarted += OnSliderDragStarted;
        ProgressSlider.DragCompleted += OnSliderDragCompleted;
    }

    public void Initialize(
        IPlaybackService playbackService,
        IAuthService? authService = null,
        Func<Task<bool>>? playFromEmptyStateAsync = null,
        string? emptyStateHint = null)
    {
        if (_isActive && _playbackService != null)
        {
            _playbackService.StateChanged -= OnPlaybackStateChanged;
        }

        _playbackService = playbackService;
        _authService = authService;
        _playFromEmptyStateAsync = playFromEmptyStateAsync;
        _emptyStateHint = emptyStateHint;

        if (_isActive)
        {
            _playbackService.StateChanged += OnPlaybackStateChanged;
        }

        ApplyPlaybackUpdates(AllUpdates);
    }

    public void Activate()
    {
        if (_isActive)
        {
            return;
        }

        _isActive = true;
        if (_playbackService != null)
        {
            _playbackService.StateChanged += OnPlaybackStateChanged;
        }

        ApplyPlaybackUpdates(AllUpdates);
    }

    public void Deactivate()
    {
        if (!_isActive)
        {
            return;
        }

        _isActive = false;
        if (_playbackService != null)
        {
            _playbackService.StateChanged -= OnPlaybackStateChanged;
        }
    }

    private bool _isSeeking;

    private async void OnPlayPauseClicked(object? sender, TappedEventArgs e)
    {
        if (_playbackService?.CurrentSong == null)
        {
            if (_playFromEmptyStateAsync != null)
            {
                await _playFromEmptyStateAsync();
            }

            return;
        }

        _playbackService.TogglePlayPause();
    }

    private async void OnRepeatClicked(object? sender, TappedEventArgs e)
    {
        await _emptyStateActionRunner.ToggleRepeatAsync(_playbackService, _playFromEmptyStateAsync);
        UpdateRepeatVisual();
    }

    private async void OnShuffleClicked(object? sender, TappedEventArgs e)
    {
        await _emptyStateActionRunner.ToggleShuffleAsync(_playbackService, _playFromEmptyStateAsync);
        UpdateShuffleVisual();
    }

    private void OnPrevClicked(object? sender, TappedEventArgs e) =>
        _playbackService?.PlayPrevious();

    private void OnNextClicked(object? sender, TappedEventArgs e) =>
        _playbackService?.PlayNext();

    private void OnSliderDragStarted(object? sender, EventArgs e) =>
        _isSeeking = true;

    private void OnSliderDragCompleted(object? sender, EventArgs e)
    {
        _isSeeking = false;
        _playbackService?.Seek(ProgressSlider.Value);
    }

    private void OnPlaybackStateChanged(string propertyName)
    {
        var update = propertyName switch
        {
            nameof(IPlaybackService.CurrentSong) => SongInfoUpdate | PreviewUpdate,
            nameof(IPlaybackService.IsPlaying) => PlayPauseUpdate,
            nameof(IPlaybackService.IsRepeatEnabled) => RepeatUpdate,
            nameof(IPlaybackService.PlaybackProgress) => ProgressUpdate,
            nameof(IPlaybackService.FormattedPosition) or nameof(IPlaybackService.FormattedDuration) => TimeUpdate | PreviewUpdate,
            nameof(IPlaybackService.PreviewLimitReached) => PreviewUpdate,
            nameof(IPlaybackService.HasPlaylist) => PlaylistUpdate,
            nameof(IPlaybackService.IsShuffleEnabled) => ShuffleUpdate,
            _ => 0
        };

        _updateScheduler.Request(update);
    }

    private void ApplyPlaybackUpdates(int updates)
    {
        if ((updates & SongInfoUpdate) != 0) UpdateSongInfo();
        if ((updates & PlayPauseUpdate) != 0) UpdatePlayPauseIcon();
        if ((updates & RepeatUpdate) != 0) UpdateRepeatVisual();
        if ((updates & ProgressUpdate) != 0 && !_isSeeking)
            ProgressSlider.Value = _playbackService?.PlaybackProgress ?? 0;
        if ((updates & TimeUpdate) != 0) UpdateTimeLabels();
        if ((updates & PlaylistUpdate) != 0) UpdatePlaylistControls();
        if ((updates & ShuffleUpdate) != 0) UpdateShuffleVisual();
        if ((updates & PreviewUpdate) != 0) UpdatePreviewMarker();
        UpdateEmptyStateText();
    }

    private void ApplyScheduledPlaybackUpdates(int updates)
    {
        if (_isActive)
        {
            ApplyPlaybackUpdates(updates);
        }
    }

    private void UpdateSongInfo()
    {
        var song = _playbackService?.CurrentSong;
        var hasSong = song != null;

        IsVisible = true;
        PlayerBorder.IsVisible = true;
        SongContentContainer.IsVisible = hasSong;
        EmptyStateContainer.IsVisible = !hasSong;

        if (hasSong)
        {
            SongTitleLabel.Text = song!.SongTitle;
            ArtistNameLabel.Text = song.ArtistName;
            // The mini player renders at 36 units, so the small rendition is ample and keeps this
            // off the multi-megabyte original.
            AlbumArtworkView.AlbumArtUrl = song.AlbumArtThumbDisplaySource;
        }
        else
        {
            SongTitleLabel.Text = string.Empty;
            ArtistNameLabel.Text = string.Empty;
            AlbumArtworkView.AlbumArtUrl = null;
            UpdateEmptyStateText();
        }
    }

    private void UpdatePlayPauseIcon()
    {
        PlayPauseIcon.Data = _playbackService?.IsPlaying == true
            ? PauseIconGeometry
            : PlayIconGeometry;
    }

    private void UpdateTimeLabels()
    {
        PositionLabel.Text = _playbackService?.FormattedPosition ?? "0:00";
        DurationLabel.Text = _playbackService?.FormattedDuration ?? "0:00";
    }

    private static Microsoft.Maui.Controls.Shapes.Geometry? CreateGeometry(string pathData)
    {
        var converter = new Microsoft.Maui.Controls.Shapes.PathGeometryConverter();
        return converter.ConvertFromInvariantString(pathData) as Microsoft.Maui.Controls.Shapes.Geometry;
    }

    private void UpdateRepeatVisual()
    {
        var isRepeat = _playbackService?.IsRepeatEnabled == true;
        if (isRepeat)
        {
            RepeatIcon.Fill = new SolidColorBrush(Color.FromArgb("#1DB954"));
            RepeatBorder.BackgroundColor = Application.Current?.RequestedTheme == AppTheme.Dark
                ? Color.FromArgb("#1DB95433")
                : Color.FromArgb("#1DB95422");
        }
        else
        {
            RepeatIcon.Fill = new SolidColorBrush(
                Application.Current?.RequestedTheme == AppTheme.Dark
                    ? Color.FromArgb("#B3B3B3")
                    : Colors.Black);
            RepeatBorder.BackgroundColor = Colors.Transparent;
        }
    }

    private void UpdatePlaylistControls()
    {
        var hasPlaylist = _playbackService?.HasPlaylist == true;
        ShuffleBorder.IsVisible = true;
        PrevBorder.IsVisible = hasPlaylist;
        NextBorder.IsVisible = hasPlaylist;
        UpdateShuffleVisual();
    }

    private void UpdateShuffleVisual()
    {
        var isShuffle = _playbackService?.IsShuffleEnabled == true;
        if (isShuffle)
        {
            ShuffleIcon.Fill = new SolidColorBrush(Color.FromArgb("#1DB954"));
            ShuffleBorder.BackgroundColor = Application.Current?.RequestedTheme == AppTheme.Dark
                ? Color.FromArgb("#1DB95433")
                : Color.FromArgb("#1DB95422");
        }
        else
        {
            ShuffleIcon.Fill = new SolidColorBrush(
                Application.Current?.RequestedTheme == AppTheme.Dark
                    ? Color.FromArgb("#B3B3B3")
                    : Colors.Black);
            ShuffleBorder.BackgroundColor = Colors.Transparent;
        }
    }

    private void UpdatePreviewMarker()
    {
        var playbackService = _playbackService;
        var authService = _authService;
        var currentSong = playbackService?.CurrentSong;
        if (playbackService == null || !PreviewAccessPolicy.ShouldLimitPreview(authService, currentSong))
        {
            PreviewMarker.IsVisible = false;
            return;
        }

        var durationText = playbackService.FormattedDuration;
        if (durationText == "0:00")
        {
            PreviewMarker.IsVisible = false;
            return;
        }

        var totalSeconds = ParseDurationToSeconds(durationText);
        if (totalSeconds <= PreviewAccessPolicy.PreviewLimitSeconds)
        {
            PreviewMarker.IsVisible = false;
            return;
        }

        var percentage = PreviewAccessPolicy.PreviewLimitSeconds / totalSeconds;
        var sliderWidth = ProgressSlider.Width;
        if (sliderWidth > 0)
        {
            PreviewMarker.TranslationX = sliderWidth * percentage - (PreviewMarker.WidthRequest / 2);
            PreviewMarker.IsVisible = true;
        }
    }

    private void UpdateEmptyStateText()
    {
        EmptyStateHintLabel.Text = string.IsNullOrWhiteSpace(_emptyStateHint)
            ? "Press Play to start listening from this screen."
            : _emptyStateHint;
    }

    public bool CollapseDrawerIfExpanded()
    {
        return false;
    }

    private static double ParseDurationToSeconds(string duration)
    {
        var parts = duration.Split(':');
        if (parts.Length == 2 && int.TryParse(parts[0], out var min) && int.TryParse(parts[1], out var sec))
            return min * 60 + sec;
        if (parts.Length == 3 && int.TryParse(parts[0], out var hr) && int.TryParse(parts[1], out min) && int.TryParse(parts[2], out sec))
            return hr * 3600 + min * 60 + sec;
        return 0;
    }
}
