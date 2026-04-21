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
    private static readonly Microsoft.Maui.Controls.Shapes.Geometry? PlayIconGeometry = CreateGeometry(PlayIconPathData);
    private static readonly Microsoft.Maui.Controls.Shapes.Geometry? PauseIconGeometry = CreateGeometry(PauseIconPathData);

    private IPlaybackService? _playbackService;
    private IAuthService? _authService;

    /// <summary>
    /// When true, the entire view hides itself when no song is playing.
    /// Set to false on pages where the player should always remain visible (e.g. SongPlayerPage).
    /// </summary>
    public bool CollapseWhenEmpty { get; set; } = true;

    public NowPlayingView()
    {
        InitializeComponent();
    }

    public void Initialize(IPlaybackService playbackService, IAuthService? authService = null)
    {
        _playbackService = playbackService;
        _authService = authService;

        PlayPauseTap.Tapped += OnPlayPauseClicked;
        StopTap.Tapped += OnStopClicked;
        RepeatTap.Tapped += OnRepeatClicked;
        ShuffleTap.Tapped += OnShuffleClicked;
        PrevTap.Tapped += OnPrevClicked;
        NextTap.Tapped += OnNextClicked;

        _playbackService.StateChanged += OnPlaybackStateChanged;

        ProgressSlider.DragStarted += OnSliderDragStarted;
        ProgressSlider.DragCompleted += OnSliderDragCompleted;

        // Set initial state
        UpdateSongInfo();
        UpdatePlayPauseIcon();
        UpdateStopButtonVisibility();
        UpdateRepeatVisual();
        UpdateTimeLabels();
        UpdatePlaylistControls();
    }

    private bool _isSeeking;

    private void OnPlayPauseClicked(object? sender, TappedEventArgs e) =>
        _playbackService?.TogglePlayPause();

    private void OnStopClicked(object? sender, TappedEventArgs e) =>
        _playbackService?.Stop();

    private void OnRepeatClicked(object? sender, TappedEventArgs e)
    {
        _playbackService?.ToggleRepeat();
        UpdateRepeatVisual();
    }

    private void OnShuffleClicked(object? sender, TappedEventArgs e)
    {
        _playbackService?.ToggleShuffle();
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
        MainThread.BeginInvokeOnMainThread(() =>
        {
            switch (propertyName)
            {
                case nameof(IPlaybackService.CurrentSong):
                    UpdateSongInfo();
                    UpdatePreviewMarker();
                    break;
                case nameof(IPlaybackService.IsPlaying):
                    UpdatePlayPauseIcon();
                    UpdateStopButtonVisibility();
                    break;
                case nameof(IPlaybackService.IsRepeatEnabled):
                    UpdateRepeatVisual();
                    break;
                case nameof(IPlaybackService.PlaybackProgress):
                    if (!_isSeeking)
                        ProgressSlider.Value = _playbackService?.PlaybackProgress ?? 0;
                    break;
                case nameof(IPlaybackService.FormattedPosition):
                case nameof(IPlaybackService.FormattedDuration):
                    UpdateTimeLabels();
                    UpdatePreviewMarker();
                    break;
                case nameof(IPlaybackService.HasPlaylist):
                    UpdatePlaylistControls();
                    break;
                case nameof(IPlaybackService.IsShuffleEnabled):
                    UpdateShuffleVisual();
                    break;
            }
        });
    }

    private void UpdateSongInfo()
    {
        var song = _playbackService?.CurrentSong;
        var hasSong = song != null;

        // Only collapse the view when CollapseWhenEmpty is true (e.g. MusicLibraryPage).
        // On SongPlayerPage the player bar should always stay visible.
        if (CollapseWhenEmpty)
        {
            IsVisible = hasSong;
            PlayerBorder.IsVisible = hasSong;
        }
        else
        {
            IsVisible = true;
            PlayerBorder.IsVisible = true;
        }

        if (hasSong)
        {
            SongTitleLabel.Text = song!.SongTitle;
            ArtistNameLabel.Text = song.ArtistName;
            AlbumArtImage.Source = string.IsNullOrEmpty(song.AlbumArtUrl)
                ? null
                : ImageSource.FromUri(new Uri(song.AlbumArtUrl));
        }
    }

    private void UpdatePlayPauseIcon()
    {
        PlayPauseIcon.Data = _playbackService?.IsPlaying == true
            ? PauseIconGeometry
            : PlayIconGeometry;
    }

    private void UpdateStopButtonVisibility()
    {
        StopBorder.IsVisible = _playbackService?.IsPlaying == true;
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
        ShuffleBorder.IsVisible = hasPlaylist;
        PrevBorder.IsVisible = hasPlaylist;
        NextBorder.IsVisible = hasPlaylist;
        if (hasPlaylist)
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
        var hasSubscription = _authService?.HasActiveSubscription == true;
        if (hasSubscription || _playbackService?.CurrentSong == null)
        {
            PreviewMarker.IsVisible = false;
            return;
        }

        var durationText = _playbackService.FormattedDuration;
        if (durationText == "0:00")
        {
            PreviewMarker.IsVisible = false;
            return;
        }

        var totalSeconds = ParseDurationToSeconds(durationText);
        if (totalSeconds <= 60)
        {
            PreviewMarker.IsVisible = false;
            return;
        }

        var percentage = 60.0 / totalSeconds;
        var sliderWidth = ProgressSlider.Width;
        if (sliderWidth > 0)
        {
            PreviewMarker.TranslationX = sliderWidth * percentage - (PreviewMarker.WidthRequest / 2);
            PreviewMarker.IsVisible = true;
        }
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
