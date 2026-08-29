using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.Resources.Styles;

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

    /// <summary>
    /// The listener tapped the song title, artwork or artist name.
    /// </summary>
    /// <remarks>
    /// Raised rather than acted on, because what the tap should DO depends on the page: the music
    /// library and the playlist player scroll their list to the playing song, and the home page and
    /// single-song player have no list to scroll. Those two simply never subscribe, which is what
    /// makes the tap inert there without a per-page conditional in here.
    /// </remarks>
    public event Action? SongInfoTapped;

    public NowPlayingView()
    {
        InitializeComponent();

        // Paint for the default surface straight away. The property-changed handler only fires when
        // a page SETS OnDarkSurface, so a bar left at the default would keep whatever colours the
        // XAML happens to carry - which is how the home page ended up with a player-dark bar.
        ApplySurface();
        _updateScheduler = new CoalescedUiUpdateScheduler(
            action => MainThread.BeginInvokeOnMainThread(action),
            ApplyScheduledPlaybackUpdates);

        SongInfoTap.Tapped += OnSongInfoClicked;
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

    /// <summary>
    /// Whether this bar sits on a player page.
    /// </summary>
    /// <remarks>
    /// On the players the bar is the player's own surface and stays dark in either OS theme - the
    /// web paints .song-player-container .player-bar the same colour in light.css and dark.css.
    /// Everywhere else it is page chrome and follows the theme, which is how the web scopes it too:
    /// a dark bar welded to the bottom of a light home page reads as a mistake.
    /// </remarks>
    public static readonly BindableProperty OnDarkSurfaceProperty =
        BindableProperty.Create(
            nameof(OnDarkSurface), typeof(bool), typeof(NowPlayingView), false,
            propertyChanged: (b, _, _) => ((NowPlayingView)b).ApplySurface());

    public bool OnDarkSurface
    {
        get => (bool)GetValue(OnDarkSurfaceProperty);
        set => SetValue(OnDarkSurfaceProperty, value);
    }

    /// <summary>
    /// Paint the bar for the surface it is on.
    /// </summary>
    /// <remarks>
    /// The themed branch uses SetAppThemeColor rather than a resolved colour, so the bar keeps
    /// following the OS theme if it changes while the app is open. The dark branch assigns flat
    /// values, which is the point - it must NOT follow the theme.
    /// </remarks>
    private void ApplySurface()
    {
        if (OnDarkSurface)
        {
            PlayerBorder.BackgroundColor = AppColors.PlayerBarDark;
            ProgressSlider.MaximumTrackColor = AppColors.ProgressTrack;
            PositionLabel.TextColor = AppColors.TimeText;
            DurationLabel.TextColor = AppColors.TimeText;
            SongTitleLabel.TextColor = AppColors.PlayerText;
            EmptyStateTitleLabel.TextColor = AppColors.PlayerText;
            ArtistNameLabel.TextColor = AppColors.PlayerText2;
            EmptyStateHintLabel.TextColor = AppColors.PlayerText2;
            PrevIcon.Fill = new SolidColorBrush(AppColors.PlayerText);
            PlayPauseIcon.Fill = new SolidColorBrush(AppColors.PlayerText);
            NextIcon.Fill = new SolidColorBrush(AppColors.PlayerText);
            return;
        }

        PlayerBorder.SetAppThemeColor(Border.BackgroundColorProperty, AppColors.Gray100, AppColors.PlayerBarDark);
        ProgressSlider.SetAppThemeColor(Slider.MaximumTrackColorProperty, AppColors.Gray200, AppColors.ProgressTrack);
        PositionLabel.SetAppThemeColor(Label.TextColorProperty, AppColors.Gray500, AppColors.TimeText);
        DurationLabel.SetAppThemeColor(Label.TextColorProperty, AppColors.Gray500, AppColors.TimeText);
        SongTitleLabel.SetAppThemeColor(Label.TextColorProperty, Colors.Black, Colors.White);
        EmptyStateTitleLabel.SetAppThemeColor(Label.TextColorProperty, Colors.Black, Colors.White);
        ArtistNameLabel.SetAppThemeColor(Label.TextColorProperty, AppColors.Gray600, AppColors.Gray300);
        EmptyStateHintLabel.SetAppThemeColor(Label.TextColorProperty, AppColors.Gray600, AppColors.Gray300);
        PrevIcon.SetAppTheme(Microsoft.Maui.Controls.Shapes.Shape.FillProperty, new SolidColorBrush(Colors.Black), new SolidColorBrush(Colors.White));
        PlayPauseIcon.SetAppTheme(Microsoft.Maui.Controls.Shapes.Shape.FillProperty, new SolidColorBrush(Colors.Black), new SolidColorBrush(Colors.White));
        NextIcon.SetAppTheme(Microsoft.Maui.Controls.Shapes.Shape.FillProperty, new SolidColorBrush(Colors.Black), new SolidColorBrush(Colors.White));
    }

    public void Activate()
    {
        if (!_isActive)
        {
            _isActive = true;
            if (_playbackService != null)
            {
                _playbackService.StateChanged += OnPlaybackStateChanged;
            }
        }

        // Outside the guard on purpose. Subscribing is what must happen once; REPAINTING must
        // happen every time this bar comes back, because a track that advanced while it was away
        // raised its change to nobody. Android does not reliably tear a page down on backgrounding,
        // so an already-active bar is precisely the case that used to come back showing the
        // previous song's title over the new song's audio.
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

    private void OnSongInfoClicked(object? sender, TappedEventArgs e)
    {
        // Nothing is playing, so there is nothing to scroll to - and the empty state occupies this
        // same space, where a tap must not look like it failed to do something.
        if (_playbackService?.CurrentSong == null)
        {
            return;
        }

        SongInfoTapped?.Invoke();
    }

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
            RepeatIcon.Fill = new SolidColorBrush(AppColors.AccentFill);
            RepeatBorder.BackgroundColor = Application.Current?.RequestedTheme == AppTheme.Dark
                ? AppColors.AccentFill.WithAlpha(0.20f)
                : AppColors.AccentFill.WithAlpha(0.13f);
        }
        else
        {
            RepeatIcon.Fill = new SolidColorBrush(
AppColors.Text3);
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
            ShuffleIcon.Fill = new SolidColorBrush(AppColors.AccentFill);
            ShuffleBorder.BackgroundColor = Application.Current?.RequestedTheme == AppTheme.Dark
                ? AppColors.AccentFill.WithAlpha(0.20f)
                : AppColors.AccentFill.WithAlpha(0.13f);
        }
        else
        {
            ShuffleIcon.Fill = new SolidColorBrush(
AppColors.Text3);
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
