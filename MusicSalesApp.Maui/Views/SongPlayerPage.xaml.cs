using System.Globalization;
using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Views;

public partial class SongPlayerPage : ContentPage
{
    private readonly IPlaybackService _playbackService;

    public SongPlayerPage(SongPlayerViewModel viewModel, IPlaybackService playbackService, IAuthService authService)
    {
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

        InitializeComponent();

        NowPlayingBar.Initialize(playbackService, authService);
    }
}

// --- Value Converters for SongPlayerPage ---

public class SubBadgeBgConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true
            ? Color.FromArgb("#1DB954") // green for subscriber
            : Color.FromArgb("#FFA500"); // orange for preview
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

public class LikeGlyphConverter : IValueConverter
{
    // Material Design thumbs up: filled vs outlined (same SVGs as web app)
    public const string FilledPath = "M1 21h4V9H1v12zm22-11c0-1.1-.9-2-2-2h-6.31l.95-4.57.03-.32c0-.41-.17-.79-.44-1.06L14.17 1 7.59 7.59C7.22 7.95 7 8.45 7 9v10c0 1.1.9 2 2 2h9c.83 0 1.54-.5 1.84-1.22l3.02-7.05c.09-.23.14-.47.14-.73v-2z";
    public const string OutlinedPath = "M9 21h9c.83 0 1.54-.5 1.84-1.22l3.02-7.05c.09-.23.14-.47.14-.73v-2c0-1.1-.9-2-2-2h-6.31l.95-4.57.03-.32c0-.41-.17-.79-.44-1.06L14.17 1 7.58 7.59C7.22 7.95 7 8.45 7 9v10c0 1.1.9 2 2 2zM9 9l4.34-4.34L12 10h9v2l-3 7H9V9zM1 9h4v12H1z";

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var pathData = value is true ? FilledPath : OutlinedPath;
        var converter = new Microsoft.Maui.Controls.Shapes.PathGeometryConverter();
        return converter.ConvertFromInvariantString(pathData);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class DislikeGlyphConverter : IValueConverter
{
    // Material Design thumbs down: filled vs outlined (same SVGs as web app)
    public const string FilledPath = "M15 3H6c-.83 0-1.54.5-1.84 1.22l-3.02 7.05c-.09.23-.14.47-.14.73v2c0 1.1.9 2 2 2h6.31l-.95 4.57-.03.32c0 .41.17.79.44 1.06L9.83 23l6.59-6.59c.36-.36.58-.86.58-1.41V5c0-1.1-.9-2-2-2zm4 0v12h4V3h-4z";
    public const string OutlinedPath = "M15 3H6c-.83 0-1.54.5-1.84 1.22l-3.02 7.05c-.09.23-.14.47-.14.73v2c0 1.1.9 2 2 2h6.31l-.95 4.57-.03.32c0 .41.17.79.44 1.06L9.83 23l6.58-6.59c.37-.36.59-.86.59-1.41V5c0-1.1-.9-2-2-2zm0 12l-4.34 4.34L12 14H3v-2l3-7h9v10zm4-12h4v12h-4z";

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var pathData = value is false ? FilledPath : OutlinedPath;
        var converter = new Microsoft.Maui.Controls.Shapes.PathGeometryConverter();
        return converter.ConvertFromInvariantString(pathData);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class LikeFillConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true)
            return new SolidColorBrush(Color.FromArgb("#1DB954"));

        return Application.Current?.RequestedTheme == AppTheme.Dark
            ? new SolidColorBrush(Color.FromArgb("#B3B3B3"))
            : new SolidColorBrush(Colors.Black);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class DislikeFillConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is false)
            return new SolidColorBrush(Color.FromArgb("#E74C3C"));

        return Application.Current?.RequestedTheme == AppTheme.Dark
            ? new SolidColorBrush(Color.FromArgb("#B3B3B3"))
            : new SolidColorBrush(Colors.Black);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class LikeColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true)
            return Color.FromArgb("#1DB954"); // green when liked

        return Application.Current?.RequestedTheme == AppTheme.Dark
            ? Color.FromArgb("#B3B3B3")
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
            return Color.FromArgb("#E74C3C"); // red when disliked

        return Application.Current?.RequestedTheme == AppTheme.Dark
            ? Color.FromArgb("#B3B3B3")
            : Colors.Black;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
