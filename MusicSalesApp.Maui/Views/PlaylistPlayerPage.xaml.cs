using System.Globalization;
using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Views;

public partial class PlaylistPlayerPage : ContentPage
{
    private readonly PlaylistPlayerViewModel _viewModel;
    private readonly ILyricsService _lyricsService;
    private CancellationTokenSource? _lyricsLoad;
    private bool _showingLyrics;
    private int _lyricsLoadedForSongId = -1;

    public PlaylistPlayerPage(
        PlaylistPlayerViewModel viewModel,
        IPlaybackService playbackService,
        IAuthService authService,
        ILyricsService lyricsService)
    {
        _viewModel = viewModel;
        _lyricsService = lyricsService;
        BindingContext = viewModel;

        // Reuse converters defined in SongPlayerPage.xaml.cs (same namespace)
        Resources.Add("DurationConverter", new DurationConverter());
        Resources.Add("SubBadgeBgConverter", new SubBadgeBgConverter());
        Resources.Add("SubBadgeTextConverter", new SubBadgeTextConverter());
        Resources.Add("LikeGlyphConverter", new LikeGlyphConverter());
        Resources.Add("DislikeGlyphConverter", new DislikeGlyphConverter());
        Resources.Add("LikeFillConverter", new LikeFillConverter());
        Resources.Add("DislikeFillConverter", new DislikeFillConverter());

        InitializeComponent();

        NowPlayingBar.Initialize(
            playbackService,
            authService,
            _viewModel.PlayVisibleQueueFromStartAsync,
            "Press Play to queue the tracks in this playlist.");

        LyricsPanel.Initialize(playbackService);

        // Unlike the single-song player, the subject here changes underneath us as the playlist
        // advances - so the lyrics have to be re-fetched per track rather than once per visit.
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private async void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlaylistPlayerViewModel.CurrentSong))
        {
            await LoadLyricsAsync();
        }
    }

    /// <summary>
    /// Fetch the current track's timings, if it has any a listener may see.
    /// </summary>
    /// <remarks>
    /// Guarded on the song id so that a property change which did not actually move to a
    /// different track - a refresh, a re-bind - does not re-download what is already shown.
    /// </remarks>
    private async Task LoadLyricsAsync()
    {
        var song = _viewModel.CurrentSong;
        var songId = song?.Id ?? -1;

        if (songId == _lyricsLoadedForSongId)
        {
            return;
        }

        _lyricsLoadedForSongId = songId;
        _lyricsLoad?.Cancel();
        _lyricsLoad = new CancellationTokenSource();
        var token = _lyricsLoad.Token;

        LyricsToggle.IsVisible = false;
        LyricsPanel.Document = null;

        try
        {
            var document = await _lyricsService.GetTimingsAsync(song, token);
            if (token.IsCancellationRequested || document is null)
            {
                return;
            }

            LyricsPanel.Document = document;
            LyricsToggle.IsVisible = true;
        }
        catch (OperationCanceledException)
        {
            // The track moved on before this finished. The newer load owns the panel now.
        }
    }

    private void OnLyricsToggleTapped(object? sender, TappedEventArgs e)
    {
        _showingLyrics = !_showingLyrics;

        HeroArt.IsVisible = !_showingLyrics;
        LyricsPanelHost.IsVisible = _showingLyrics;
        LyricsToggleLabel.Text = _showingLyrics ? "Art" : "Lyrics";

        if (_showingLyrics)
        {
            LyricsPanel.Activate();
        }
        else
        {
            LyricsPanel.Deactivate();
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        NowPlayingBar.Activate();
        _viewModel.Activate();

        if (_showingLyrics)
        {
            LyricsPanel.Activate();
        }

        await _viewModel.StartSignalRAsync();
        await LoadLyricsAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        NowPlayingBar.Deactivate();
        LyricsPanel.Deactivate();
        _lyricsLoad?.Cancel();
        _viewModel.Cleanup();
    }

    protected override bool OnBackButtonPressed()
    {
        if (NowPlayingBar.CollapseDrawerIfExpanded())
        {
            return true;
        }

        return base.OnBackButtonPressed();
    }
}
