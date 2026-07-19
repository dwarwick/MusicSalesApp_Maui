using System.Globalization;
using Microsoft.Extensions.Logging;
using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Views;

public partial class MusicLibraryPage : ContentPage
{
    private readonly MusicLibraryViewModel _viewModel;
    private readonly ILogger<MusicLibraryPage> _logger;

    public MusicLibraryPage(MusicLibraryViewModel viewModel, IPlaybackService playbackService, ILogger<MusicLibraryPage> logger, IAuthService authService)
    {
        _viewModel = viewModel;
        _logger = logger;
        BindingContext = viewModel;

        // Add converters to page resources before InitializeComponent
        Resources.Add("PlayPauseGlyphConverter", new PlayPauseGlyphConverter());
        Resources.Add("DurationConverter", new DurationConverter());
        Resources.Add("ActivePillBgConverter", new ActivePillBgConverter());
        Resources.Add("ActivePillTextConverter", new ActivePillTextConverter());
        Resources.Add("LikeGlyphConverter", new LikeGlyphConverter());
        Resources.Add("DislikeGlyphConverter", new DislikeGlyphConverter());
        Resources.Add("LikeColorConverter", new LikeColorConverter());
        Resources.Add("DislikeColorConverter", new DislikeColorConverter());
        Resources.Add("LikeFillConverter", new LikeFillConverter());
        Resources.Add("DislikeFillConverter", new DislikeFillConverter());

        InitializeComponent();

        // Initialize the reusable NowPlayingView with the playback service
        NowPlayingBar.Initialize(
            playbackService,
            authService,
            _viewModel.PlayVisibleQueueFromStartAsync,
            "Press Play to queue the songs currently visible in Music Library.");

        // Wire RefreshView command in code-behind to avoid MAUIG2045
        SongsRefreshView.Command = _viewModel.LoadSongsCommand;

        _logger.LogInformation("[Audio] MusicLibraryPage constructed.");
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        NowPlayingBar.Activate();
        _viewModel.Activate();

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

    private void OnFilterChromeClicked(object? sender, EventArgs e) => DismissFilterSearchInputs();

    private void OnFilterOptionTapped(object? sender, TappedEventArgs e) => DismissFilterSearchInputs();

    private void OnFilterSearchEntryUnfocused(object? sender, FocusEventArgs e) => DismissFilterSearchInputs();

    private void DismissFilterSearchInputs()
    {
        UnfocusEntry(GenreSearchEntry);
        UnfocusEntry(ArtistSearchEntry);
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
/// Converts bool (isActive) to pill background color using the StreamTunes green CTA treatment.
/// </summary>
public class ActivePillBgConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true)
            return Color.FromArgb("#1ED760");

        return Color.FromArgb("#1DB954");
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// Converts bool (isActive) to pill text color using white for the green CTA pill treatment.
/// </summary>
public class ActivePillTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return Colors.White;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
