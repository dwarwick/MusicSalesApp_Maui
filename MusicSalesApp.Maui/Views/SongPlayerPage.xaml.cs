using System.Globalization;
using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.ViewModels;
using MusicSalesApp.Maui.Resources.Styles;

namespace MusicSalesApp.Maui.Views;

public partial class SongPlayerPage : ContentPage
{
    private readonly IPlaybackService _playbackService;
    private readonly SongPlayerViewModel _viewModel;
    private readonly ILyricsService _lyricsService;
    private CancellationTokenSource? _lyricsLoad;
    private bool _showingLyrics;
    private bool? _isWideLayout;

    public SongPlayerPage(
        SongPlayerViewModel viewModel,
        IPlaybackService playbackService,
        IAuthService authService,
        ILyricsService lyricsService)
    {
        _lyricsService = lyricsService;
        _viewModel = viewModel;
        _playbackService = playbackService;
        BindingContext = viewModel;

        // DurationConverter class defined in MusicLibraryPage.xaml.cs (same namespace)
        Resources.Add("DurationConverter", new DurationConverter());
        Resources.Add("SubBadgeBgConverter", new SubBadgeBgConverter());
        Resources.Add("SubBadgeTextConverter", new SubBadgeTextConverter());
        Resources.Add("LikeGlyphConverter", new LikeGlyphConverter());
        Resources.Add("DislikeGlyphConverter", new DislikeGlyphConverter());
        Resources.Add("LikeColorConverter", new LikeColorConverter());
        Resources.Add("DislikeColorConverter", new DislikeColorConverter());
        Resources.Add("LikeFillConverter", new LikeFillConverter());
        Resources.Add("DislikeFillConverter", new DislikeFillConverter());
        Resources.Add("RateableOpacityConverter", new RateableOpacityConverter());

        InitializeComponent();

        NowPlayingBar.Initialize(
            playbackService,
            authService,
            _viewModel.PlayDisplayedSongQueueAsync,
            "Press Play to queue this song.");

        LyricsPanel.Initialize(playbackService);
    }

    /// <summary>
    /// Fetch this song's timings, if it has any a listener may see.
    /// </summary>
    /// <remarks>
    /// The toggle stays hidden until they are actually in hand, so it can never offer a panel
    /// that turns out to be empty. A song without lyrics simply never grows the control.
    /// </remarks>
    private async Task LoadLyricsAsync()
    {
        _lyricsLoad?.Cancel();
        _lyricsLoad = new CancellationTokenSource();
        var token = _lyricsLoad.Token;

        StageSwitch.IsVisible = false;
        LyricsPanel.Document = null;

        try
        {
            var document = await _lyricsService.GetTimingsAsync(_viewModel.Song, token);
            if (token.IsCancellationRequested)
            {
                return;
            }

            if (document is null)
            {
                ShowArt();
                return;
            }

            LyricsPanel.Document = document;
            StageSwitch.IsVisible = true;
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer song. Nothing to clean up.
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

        // The panel only follows playback while it is on screen - a hidden one must not be
        // waking the main thread ten times a second.
        LyricsPanel.Activate();
    }

    /// <summary>Put the artwork back and stop the panel.</summary>
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
    /// </remarks>
    private void ApplySegmentState()
    {
        LyricsSegment.Style = SegmentStyle(_showingLyrics);
        ArtSegment.Style = SegmentStyle(!_showingLyrics);
        LyricsSegmentText.Style = SegmentTextStyle(_showingLyrics);
        ArtSegmentText.Style = SegmentTextStyle(!_showingLyrics);
    }

    /// <summary>
    /// Look a segment style up out of the app's merged dictionaries.
    /// </summary>
    /// <remarks>
    /// Off <see cref="Application.Resources"/> rather than the page's own, which does not contain
    /// these - the styles come from Styles.xaml, merged in at the application level, and only
    /// Application.Resources searches merged dictionaries for them.
    /// </remarks>
    private static Style? SegmentStyle(bool active) =>
        LookupStyle(active ? "PlayerSwitchSegmentActive" : "PlayerSwitchSegment");

    private static Style? SegmentTextStyle(bool active) =>
        LookupStyle(active ? "PlayerSwitchSegmentTextActive" : "PlayerSwitchSegmentText");

    private static Style? LookupStyle(string key) =>
        Application.Current?.Resources.TryGetValue(key, out var style) == true
            ? style as Style
            : null;

    /// <summary>
    /// Move the artist panel beside the stage once the window is wide enough, and back beneath it
    /// when it is not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Done here rather than with AdaptiveTrigger and visual states, because the setters that would
    /// be needed - a string converted to a ColumnDefinitionCollection, and attached Grid.Row and
    /// Grid.Column inside a VisualState - compile whether or not they resolve, so a mistake shows
    /// up as a wrong layout on a device rather than as a build error. This form is checkable, and
    /// the decision itself is a tested function.
    /// </para>
    /// <para>
    /// Guarded on the previous answer: OnSizeAllocated fires on every layout pass, and rebuilding
    /// the grid each time would re-measure both panels continually.
    /// </para>
    /// </remarks>
    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);

        ApplyContentWidth(width);

        var wide = AdaptiveLayout.IsWide(width);
        if (wide == _isWideLayout)
        {
            return;
        }

        _isWideLayout = wide;
        ApplyStageLayout(wide);
    }

    /// <summary>
    /// Hold the content to a readable width, centred, once the window is wider than that.
    /// </summary>
    /// <remarks>
    /// Done with margins rather than MaximumWidthRequest because the two do not compose: a stack
    /// set to Center sizes to its content and never reaches the cap, while one set to Fill reaches
    /// it but sits against the leading edge. Margins give both.
    /// </remarks>
    private void ApplyContentWidth(double width)
    {
        var inset = AdaptiveLayout.ContentInset(width);
        ContentColumn.Margin = new Thickness(inset, 0, inset, 0);
    }

    private void ApplyStageLayout(bool wide)
    {
        if (wide)
        {
            StageRow.RowDefinitions = new RowDefinitionCollection(new RowDefinition(GridLength.Auto));
            StageRow.ColumnDefinitions = new ColumnDefinitionCollection(
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(new GridLength(AdaptiveLayout.SideColumnWidth)));
            Grid.SetRow(ArtistColumn, 0);
            Grid.SetColumn(ArtistColumn, 1);
            return;
        }

        StageRow.RowDefinitions = new RowDefinitionCollection(
            new RowDefinition(GridLength.Auto),
            new RowDefinition(GridLength.Auto));
        StageRow.ColumnDefinitions = new ColumnDefinitionCollection(new ColumnDefinition(GridLength.Star));
        Grid.SetRow(ArtistColumn, 1);
        Grid.SetColumn(ArtistColumn, 0);
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

// --- Value Converters for SongPlayerPage ---

public class SubBadgeBgConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true
            ? AppColors.BlueBright   // subscribed
            : AppColors.Amber;       // preview limited - a warning, never brand colour
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class SubBadgeTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? "Unlimited Access" : "Preview Only (60 seconds)";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class LikeColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true)
            return AppColors.AccentFill; // liked

        return Application.Current?.RequestedTheme == AppTheme.Dark
            ? AppColors.Text3
            : Colors.Black;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class DislikeColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is false)
            return AppColors.Danger; // disliked

        return Application.Current?.RequestedTheme == AppTheme.Dark
            ? AppColors.Text3
            : Colors.Black;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Dims the thumbs when the user has not streamed the song yet and so may not rate it.
///
/// Opacity rather than IsEnabled on purpose: a disabled control swallows the tap silently, which reads
/// as a broken button. Dimmed-but-live lets the tap through so the ViewModel can say why.
/// </summary>
public class RateableOpacityConverter : IValueConverter
{
    private const double BlockedOpacity = 0.35;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is false ? BlockedOpacity : 1.0;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
