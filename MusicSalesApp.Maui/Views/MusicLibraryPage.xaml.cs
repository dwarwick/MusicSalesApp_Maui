using System.Globalization;
using Microsoft.Extensions.Logging;
using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Views;

public partial class MusicLibraryPage : ContentPage
{
    private readonly MusicLibraryViewModel _viewModel;
    private readonly ILogger<MusicLibraryPage> _logger;
    private readonly IPlaybackService _playbackService;
    private readonly IAutoScrollSettingsService _autoScrollSettingsService;
    private readonly NowPlayingScrollCoordinator _scrollCoordinator;

    public MusicLibraryPage(
        MusicLibraryViewModel viewModel,
        IPlaybackService playbackService,
        ILogger<MusicLibraryPage> logger,
        IAuthService authService,
        IAutoScrollSettingsService autoScrollSettingsService)
    {
        _viewModel = viewModel;
        _logger = logger;
        _playbackService = playbackService;
        _autoScrollSettingsService = autoScrollSettingsService;
        _scrollCoordinator = new NowPlayingScrollCoordinator(
            () => autoScrollSettingsService.ScrollAutomatically);
        BindingContext = viewModel;

        // Add converters to page resources before InitializeComponent
        Resources.Add("PlayPauseGlyphConverter", new PlayPauseGlyphConverter());
        Resources.Add("DurationConverter", new DurationConverter());
        Resources.Add("PillStyleConverter", new PillStyleConverter());
        Resources.Add("PillTextStyleConverter", new PillTextStyleConverter());
        Resources.Add("LikeGlyphConverter", new LikeGlyphConverter());
        Resources.Add("DislikeGlyphConverter", new DislikeGlyphConverter());
        Resources.Add("LikeColorConverter", new LikeColorConverter());
        Resources.Add("DislikeColorConverter", new DislikeColorConverter());
        Resources.Add("LikeFillConverter", new LikeFillConverter());
        Resources.Add("DislikeFillConverter", new DislikeFillConverter());
        Resources.Add("RateableOpacityConverter", new RateableOpacityConverter());

        InitializeComponent();

        // Initialize the reusable NowPlayingView with the playback service
        NowPlayingBar.Initialize(
            playbackService,
            authService,
            _viewModel.PlayVisibleQueueFromStartAsync,
            "Press Play to queue the songs currently visible in Music Library.");

        // Wire RefreshView command in code-behind to avoid MAUIG2045
        SongsRefreshView.Command = _viewModel.LoadSongsCommand;

        NowPlayingBar.SongInfoTapped += OnNowPlayingSongInfoTapped;

        _logger.LogInformation("[Audio] MusicLibraryPage constructed.");
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        NowPlayingBar.Activate();
        _viewModel.Activate();

        // Re-armed per visit: the queue can have advanced while this page was away, and the
        // coordinator would otherwise still consider that song "already scrolled to".
        _scrollCoordinator.Reset();
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _autoScrollSettingsService.Changed += OnAutoScrollSettingChanged;

        if (_viewModel.Songs.Count == 0)
        {
            await _viewModel.LoadSongsCommand.ExecuteAsync(null);
        }

        await _viewModel.LoadStreamQualifyingSecondsAsync();
        await _viewModel.StartSignalRAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        NowPlayingBar.Deactivate();
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _autoScrollSettingsService.Changed -= OnAutoScrollSettingChanged;
        _viewModel.Cleanup();
        // Don't stop playback when navigating away — it keeps playing in background
    }

    protected override bool OnBackButtonPressed()
    {
        if (NowPlayingBar.CollapseDrawerIfExpanded())
        {
            return true;
        }

        return base.OnBackButtonPressed();
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MusicLibraryViewModel.CurrentSong))
        {
            return;
        }

        var songId = _viewModel.CurrentSong?.Id ?? 0;
        if (_scrollCoordinator.ShouldScrollOnTrackChange(songId))
        {
            ScrollToCurrentSong();
        }
    }

    private void OnNowPlayingSongInfoTapped()
    {
        // An explicit request, so it ignores both the setting and any manual-scroll pause.
        if (_scrollCoordinator.ShouldScrollOnRequest(_playbackService.CurrentSong?.Id ?? 0))
        {
            ScrollToCurrentSong();
        }
    }

    private void OnAutoScrollSettingChanged()
    {
        if (!_autoScrollSettingsService.ScrollAutomatically)
        {
            return;
        }

        // Ticking the box acts immediately rather than waiting for the queue to advance.
        if (_scrollCoordinator.ShouldScrollOnRequest(_playbackService.CurrentSong?.Id ?? 0))
        {
            MainThread.BeginInvokeOnMainThread(ScrollToCurrentSong);
        }
    }

    private void OnSongsScrolled(object? sender, ItemsViewScrolledEventArgs e) =>
        _scrollCoordinator.NotifyScrolled();

    /// <summary>
    /// Bring the playing song's card into view.
    /// </summary>
    /// <remarks>
    /// Dispatched rather than called straight through: a track change routinely arrives with a list
    /// rebuild - ApplyFilters raises a single Reset through ReplaceAll - and a ScrollTo issued
    /// before that re-layout lands is silently dropped.
    /// </remarks>
    private void ScrollToCurrentSong() => Dispatcher.Dispatch(() =>
    {
        var index = PlaybackQueueSelection.TryResolveCurrentSongIndex(_playbackService, _viewModel.Songs);
        if (index < 0)
        {
            return;
        }

        try
        {
            _scrollCoordinator.BeginProgrammaticScroll();
            SongsCollectionView.ScrollTo(index, position: ScrollToPosition.Center, animate: true);
        }
        catch (Exception ex)
        {
            // A scroll that cannot complete - the page is being torn down, or the list is not laid
            // out yet - is not worth surfacing. The next change tries again.
            _logger.LogDebug(ex, "[Audio] Could not scroll the library to the playing song.");
        }
    });

    private void OnFilterOptionTapped(object? sender, TappedEventArgs e) => DismissFilterSearchInputs();

    private void OnFilterSearchEntryUnfocused(object? sender, FocusEventArgs e) => DismissFilterSearchInputs();

    private void DismissFilterSearchInputs()
    {
        UnfocusEntry(GenreSearchEntry);
        UnfocusEntry(ArtistSearchEntry);
        UnfocusEntry(TitleSearchEntry);
    }

    private static void UnfocusEntry(Entry? entry)
    {
        if (entry?.IsFocused == true)
        {
            entry.Unfocus();
        }
    }
}

/// <summary>
/// Converts bool IsPlaying to the appropriate play/pause Unicode glyph.
/// </summary>
public class PlayPauseGlyphConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? "\u23F8" : "\u25B6"; // ⏸ or ▶
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// Converts a nullable double (seconds) to a formatted duration string (m:ss or h:mm:ss).
/// </summary>
public class DurationConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double seconds && !double.IsNaN(seconds) && !double.IsInfinity(seconds) && seconds > 0)
        {
            var ts = TimeSpan.FromSeconds(seconds);
            return ts.TotalHours >= 1
                ? ts.ToString(@"h\:mm\:ss")
                : ts.ToString(@"m\:ss");
        }
        return "--:--";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// Picks a filter pill's Style from whether its filter is applied.
/// </summary>
/// <remarks>
/// <para>
/// <b>These return a Style, never a Color, and that is the whole point.</b> The pills used to
/// override BackgroundColor, Border.Stroke and TextColor per instance from converters that read
/// <c>AppColors.Surface</c> / <c>Text2</c> / <c>Line</c>. Those go through
/// <c>AppColors.ForCurrentTheme</c>, which resolves the theme once and freezes it - and a
/// converter runs exactly once, when the binding is first evaluated, whereas an AppThemeBinding
/// re-evaluates on every RequestedThemeChanged.
/// </para>
/// <para>
/// So the pills baked in whatever theme was current while the page was being built. Android reads
/// Dark early in startup and corrects itself once the real theme arrives; every AppThemeBinding in
/// the app followed, and the pills could not - they came up dark-on-light in light mode. iOS reads
/// Light early, froze the right values, and merely looked washed out. Both were the same fault.
/// </para>
/// <para>
/// Handing over a Style instead keeps every colour in an AppThemeBinding setter in Styles.xaml,
/// where it stays live. The converter itself never touches the theme, so there is nothing to
/// freeze.
/// </para>
/// </remarks>
public class PillStyleConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        PillStyles.Resolve(value is true ? "FilterPillActive" : "FilterPill");

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// The label counterpart to <see cref="PillStyleConverter"/>. Split for the same reason the styles
/// are: the fill lives on the Border and the foreground on the Label, and the two move together.
/// </summary>
public class PillTextStyleConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        PillStyles.Resolve(value is true ? "FilterPillTextActive" : "FilterPillText");

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

internal static class PillStyles
{
    /// <summary>
    /// Looks a pill style up in the merged application dictionaries. Returns null rather than
    /// throwing on a miss: null leaves the element on whatever Style it already had, so a renamed
    /// key degrades to an unhighlighted pill instead of taking the library page down.
    /// </summary>
    public static Style? Resolve(string key) =>
        Application.Current?.Resources.TryGetValue(key, out var style) == true
            ? style as Style
            : null;
}
