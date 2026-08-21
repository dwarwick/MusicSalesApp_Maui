using System.Windows.Input;
using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Views;

public partial class SongCardView : ContentView
{
    private IPlaybackService? _playbackService;
    private ILyricsService? _lyricsService;
    private CancellationTokenSource? _lyricsLoad;
    private int _lyricsLoadedForSongId = -1;

    public static readonly BindableProperty ShowFacebookShareButtonProperty =
        BindableProperty.Create(nameof(ShowFacebookShareButton), typeof(bool), typeof(SongCardView), true);

    public static readonly BindableProperty ShowShareButtonProperty =
        BindableProperty.Create(nameof(ShowShareButton), typeof(bool), typeof(SongCardView), true);

    public static readonly BindableProperty ShowAddToPlaylistButtonProperty =
        BindableProperty.Create(
            nameof(ShowAddToPlaylistButton), typeof(bool), typeof(SongCardView), true,
            propertyChanged: (bindable, _, _) =>
                ((SongCardView)bindable).OnPropertyChanged(nameof(HideAddToPlaylistButton)));

    public static readonly BindableProperty OpenSongCommandProperty =
        BindableProperty.Create(nameof(OpenSongCommand), typeof(ICommand), typeof(SongCardView));

    public static readonly BindableProperty NavigateToArtistCommandProperty =
        BindableProperty.Create(nameof(NavigateToArtistCommand), typeof(ICommand), typeof(SongCardView));

    public static readonly BindableProperty NavigateToGenreCommandProperty =
        BindableProperty.Create(nameof(NavigateToGenreCommand), typeof(ICommand), typeof(SongCardView));

    public static readonly BindableProperty LikeSongCommandProperty =
        BindableProperty.Create(nameof(LikeSongCommand), typeof(ICommand), typeof(SongCardView));

    public static readonly BindableProperty DislikeSongCommandProperty =
        BindableProperty.Create(nameof(DislikeSongCommand), typeof(ICommand), typeof(SongCardView));

    public static readonly BindableProperty ReportSongCommandProperty =
        BindableProperty.Create(nameof(ReportSongCommand), typeof(ICommand), typeof(SongCardView));

    /// <summary>
    /// Set false to hide the report flag. Pages bind this to their ViewModel's CanUseServerActions so
    /// the control disappears offline instead of failing on tap.
    /// </summary>
    public static readonly BindableProperty ShowReportButtonProperty =
        BindableProperty.Create(nameof(ShowReportButton), typeof(bool), typeof(SongCardView), true);

    public bool ShowReportButton
    {
        get => (bool)GetValue(ShowReportButtonProperty);
        set => SetValue(ShowReportButtonProperty, value);
    }

    /// <summary>
    /// Inverse of <see cref="ShowAddToPlaylistButton"/>, for AddToPlaylistButton.Suppressed - that
    /// control owns its own IsVisible so the host and the offline gate cannot fight over it.
    /// </summary>
    public bool HideAddToPlaylistButton => !ShowAddToPlaylistButton;

    public static readonly BindableProperty PlaySongCommandProperty =
        BindableProperty.Create(nameof(PlaySongCommand), typeof(ICommand), typeof(SongCardView));

    public ICommand? OpenSongCommand
    {
        get => (ICommand?)GetValue(OpenSongCommandProperty);
        set => SetValue(OpenSongCommandProperty, value);
    }

    public bool ShowFacebookShareButton
    {
        get => (bool)GetValue(ShowFacebookShareButtonProperty);
        set => SetValue(ShowFacebookShareButtonProperty, value);
    }

    public bool ShowShareButton
    {
        get => (bool)GetValue(ShowShareButtonProperty);
        set => SetValue(ShowShareButtonProperty, value);
    }

    public bool ShowAddToPlaylistButton
    {
        get => (bool)GetValue(ShowAddToPlaylistButtonProperty);
        set => SetValue(ShowAddToPlaylistButtonProperty, value);
    }

    public ICommand? NavigateToArtistCommand
    {
        get => (ICommand?)GetValue(NavigateToArtistCommandProperty);
        set => SetValue(NavigateToArtistCommandProperty, value);
    }

    public ICommand? NavigateToGenreCommand
    {
        get => (ICommand?)GetValue(NavigateToGenreCommandProperty);
        set => SetValue(NavigateToGenreCommandProperty, value);
    }

    public ICommand? LikeSongCommand
    {
        get => (ICommand?)GetValue(LikeSongCommandProperty);
        set => SetValue(LikeSongCommandProperty, value);
    }

    public ICommand? DislikeSongCommand
    {
        get => (ICommand?)GetValue(DislikeSongCommandProperty);
        set => SetValue(DislikeSongCommandProperty, value);
    }

    public ICommand? ReportSongCommand
    {
        get => (ICommand?)GetValue(ReportSongCommandProperty);
        set => SetValue(ReportSongCommandProperty, value);
    }

    public ICommand? PlaySongCommand
    {
        get => (ICommand?)GetValue(PlaySongCommandProperty);
        set => SetValue(PlaySongCommandProperty, value);
    }

    public SongCardView()
    {
        InitializeComponent();

        // Cards are built by a DataTemplate, so services are resolved on the way in and released
        // on the way out rather than injected - the same shape EqualizerPlayButton uses, and for
        // the same reason.
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>Injected by tests, which have no platform application to resolve from.</summary>
    public IPlaybackService? PlaybackServiceOverride { get; set; }

    /// <summary>Injected by tests.</summary>
    public ILyricsService? LyricsServiceOverride { get; set; }

    /// <summary>
    /// Cards are recycled by the list, so a card can be handed a different song at any moment.
    /// </summary>
    /// <remarks>
    /// Without this the panel, the toggle and the loaded-song guard would all carry over to
    /// whatever song scrolled into this slot next - showing one song's lyrics against another's
    /// title, which is worse than showing none.
    /// </remarks>
    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();

        _lyricsLoad?.Cancel();
        _lyricsLoadedForSongId = -1;
        LyricsPanel.Document = null;
        LyricsToggle.IsVisible = false;
        ShowArt();

        SyncToPlayingSong();
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        var services = IPlatformApplication.Current?.Services;
        _playbackService = PlaybackServiceOverride
            ?? services?.GetService(typeof(IPlaybackService)) as IPlaybackService;
        _lyricsService = LyricsServiceOverride
            ?? services?.GetService(typeof(ILyricsService)) as ILyricsService;

        if (_playbackService is not null)
        {
            _playbackService.StateChanged += OnPlaybackStateChanged;
        }

        SyncToPlayingSong();
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        if (_playbackService is not null)
        {
            _playbackService.StateChanged -= OnPlaybackStateChanged;
        }

        _lyricsLoad?.Cancel();
        LyricsPanel.Deactivate();
    }

    private void OnPlaybackStateChanged(string propertyName)
    {
        if (propertyName == nameof(IPlaybackService.CurrentSong))
        {
            MainThread.BeginInvokeOnMainThread(SyncToPlayingSong);
        }
    }

    /// <summary>
    /// Offer lyrics on this card only while it is the one playing.
    /// </summary>
    /// <remarks>
    /// Two reasons, and the second is the one that matters at scale. The highlight follows the
    /// audio clock, so on a card that is not playing there is nothing for it to follow. And a
    /// library page holds dozens of cards - fetching timings for all of them would be dozens of
    /// downloads to answer a question nobody asked.
    /// </remarks>
    private async void SyncToPlayingSong()
    {
        var song = BindingContext as SongDto;
        var isPlayingCard = song is not null && _playbackService?.CurrentSong?.Id == song.Id;

        if (!isPlayingCard)
        {
            _lyricsLoad?.Cancel();
            ShowArt();
            LyricsToggle.IsVisible = false;
            LyricsPanel.Document = null;
            _lyricsLoadedForSongId = -1;
            return;
        }

        if (song!.Id == _lyricsLoadedForSongId || _lyricsService is null)
        {
            return;
        }

        _lyricsLoadedForSongId = song.Id;
        _lyricsLoad?.Cancel();
        _lyricsLoad = new CancellationTokenSource();
        var token = _lyricsLoad.Token;

        try
        {
            var document = await _lyricsService.GetTimingsAsync(song, token);
            if (token.IsCancellationRequested)
            {
                return;
            }

            if (document is null)
            {
                // No lyrics for this one. Close anything the previous song left open rather than
                // leaving an empty panel over hidden artwork.
                ShowArt();
                LyricsToggle.IsVisible = false;
                return;
            }

            LyricsPanel.Document = document;
            LyricsToggle.IsVisible = true;
        }
        catch (OperationCanceledException)
        {
            // Playback moved to another card. That card owns the panel now.
        }
    }

    private void OnLyricsToggleTapped(object? sender, TappedEventArgs e)
    {
        if (LyricsPanelHost.IsVisible)
        {
            ShowArt();
            return;
        }

        HeroArt.IsVisible = false;
        LyricsPanelHost.IsVisible = true;
        LyricsToggleLabel.Text = "Art";
        LyricsPanel.Activate();
    }

    private void ShowArt()
    {
        HeroArt.IsVisible = true;
        LyricsPanelHost.IsVisible = false;
        LyricsToggleLabel.Text = "Lyrics";
        LyricsPanel.Deactivate();
    }
}