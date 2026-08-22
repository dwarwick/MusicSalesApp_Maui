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
    private bool? _isWideLayout;
    private View? _sidePanels;

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

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PlaylistPlayerViewModel.CurrentSong))
        {
            return;
        }

        // Marshalled rather than awaited here: the track can advance from a playback callback on
        // a thread-pool thread, and everything this ends up touching - the toggle, the panel, the
        // artwork - is a live visual element.
        MainThread.BeginInvokeOnMainThread(async () => await LoadLyricsAsync());
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

        StageSwitch.IsVisible = false;
        LyricsPanel.Document = null;

        try
        {
            var document = await _lyricsService.GetTimingsAsync(song, token);
            if (token.IsCancellationRequested)
            {
                return;
            }

            if (document is null)
            {
                // This track has no lyrics. If the panel was open on the previous one it has to
                // close, or it sits there empty over hidden artwork with nothing coming.
                ShowArt();
                return;
            }

            LyricsPanel.Document = document;
            StageSwitch.IsVisible = true;
        }
        catch (OperationCanceledException)
        {
            // The track moved on before this finished. The newer load owns the panel now.
        }
    }

    /// <summary>The artist's own site, from the hero header.</summary>
    /// <remarks>
    /// Guarded rather than handed straight to the launcher: the server stores this field with
    /// nothing but a Trim, so it may well arrive as a bare host. See <see cref="WebsiteUri"/>.
    /// </remarks>
    private async void OnHeroWebsiteTapped(object? sender, TappedEventArgs e)
    {
        if (!WebsiteUri.TryParse(_viewModel.PersonaWebsiteUrl, out var uri) || uri is null)
        {
            return;
        }

        try
        {
            await Launcher.Default.OpenAsync(uri);
        }
        catch
        {
            // No browser, or the launcher refused. The rest of the page is unaffected.
        }
    }

    private void OnShowLyricsClicked(object? sender, TappedEventArgs e) => ShowLyrics();

    private void OnShowArtClicked(object? sender, TappedEventArgs e) => ShowArt();

    /// <summary>Bring the lyrics up in the stage panel and start following playback.</summary>
    private void ShowLyrics()
    {
        _showingLyrics = true;
        HeroArt.IsVisible = false;
        LyricsPanel.IsVisible = true;
        StageCaption.Text = "LYRICS";
        ApplySegmentState();
        LyricsPanel.Activate();
    }

    /// <summary>
    /// Put the artwork back and stop the panel.
    /// </summary>
    /// <remarks>
    /// Called from the switch AND whenever a track turns out to have no lyrics. The second is the
    /// one that matters: advancing from a song with lyrics to one without used to leave the panel
    /// open and empty over hidden artwork.
    /// </remarks>
    private void ShowArt()
    {
        _showingLyrics = false;
        HeroArt.IsVisible = true;
        LyricsPanel.IsVisible = false;
        StageCaption.Text = "COVER ART";
        ApplySegmentState();
        LyricsPanel.Deactivate();
    }

    /// <summary>
    /// Move the bright fill onto whichever segment is showing.
    /// </summary>
    /// <remarks>
    /// Four assignments, not two: the fill lives on the segment's Border and the label colour on
    /// the Label inside it, and the active state is a background AND a foreground - moving one
    /// without the other is how a segment ends up bright-on-bright, or near-black on the panel.
    /// Looked up off <see cref="Application.Resources"/> rather than the page's own, which does
    /// not contain these: they come from Styles.xaml, merged in at the application level.
    /// </remarks>
    private void ApplySegmentState()
    {
        LyricsSegment.Style = SegmentStyle(_showingLyrics);
        ArtSegment.Style = SegmentStyle(!_showingLyrics);
        LyricsSegmentText.Style = SegmentTextStyle(_showingLyrics);
        ArtSegmentText.Style = SegmentTextStyle(!_showingLyrics);
    }

    private static Style? SegmentStyle(bool active) =>
        LookupStyle(active ? "PlayerSwitchSegmentActive" : "PlayerSwitchSegment");

    private static Style? SegmentTextStyle(bool active) =>
        LookupStyle(active ? "PlayerSwitchSegmentTextActive" : "PlayerSwitchSegmentText");

    private static Style? LookupStyle(string key) =>
        Application.Current?.Resources.TryGetValue(key, out var style) == true
            ? style as Style
            : null;

    /// <summary>
    /// Put the stage and bio panels beside the track list when the window is wide enough, and back
    /// underneath it when it is not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The panels are declared once, inside SidePanelHost, and MOVED - not duplicated. Two copies
    /// would mean two LyricsView instances running highlight loops against the same playback, and
    /// the off-screen one would go on waking the main thread ten times a second.
    /// </para>
    /// <para>
    /// A CollectionView renders header, items, then footer, so on a phone the panels have to sit in
    /// the footer to appear below the rows; on a wide window they belong in a column beside them.
    /// MAUI cannot reparent declaratively, hence the hand-off here. Detach from the old host BEFORE
    /// attaching to the new one - a view with two parents throws.
    /// </para>
    /// </remarks>
    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);

        var wide = AdaptiveLayout.IsWide(width);
        if (wide == _isWideLayout)
        {
            return;
        }

        _isWideLayout = wide;
        ApplyStageLayout(wide);
    }

    private void ApplyStageLayout(bool wide)
    {
        _sidePanels ??= SidePanels;

        if (wide)
        {
            FooterPanelHost.Content = null;
            SidePanelHost.Content = _sidePanels;
            SidePanelHost.IsVisible = true;
            StageShell.ColumnDefinitions = new ColumnDefinitionCollection(
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(new GridLength(AdaptiveLayout.SideColumnWidth)));
            return;
        }

        SidePanelHost.Content = null;
        SidePanelHost.IsVisible = false;
        FooterPanelHost.Content = _sidePanels;
        StageShell.ColumnDefinitions = new ColumnDefinitionCollection(new ColumnDefinition(GridLength.Star));
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
