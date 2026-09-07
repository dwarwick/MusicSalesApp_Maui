using System.Globalization;
using MusicSalesApp.Maui.Resources.Styles;

namespace MusicSalesApp.Maui.Views;

/// <summary>
/// The follow bell's outline, filled once the artist is followed.
/// </summary>
/// <remarks>
/// The same two Material Design paths the web app draws, so the two clients cannot drift into
/// different bells. Filled means following - that is the whole state, there is no third value.
/// </remarks>
public class FollowBellGlyphConverter : IValueConverter
{
    public const string FilledPath = "M12 22c1.1 0 2-.9 2-2h-4c0 1.1.9 2 2 2zm6-6v-5c0-3.07-1.63-5.64-4.5-6.32V4c0-.83-.67-1.5-1.5-1.5s-1.5.67-1.5 1.5v.68C7.64 5.36 6 7.92 6 11v5l-2 2v1h16v-1l-2-2z";
    public const string OutlinedPath = "M12 22c1.1 0 2-.9 2-2h-4c0 1.1.9 2 2 2zm6-6v-5c0-3.07-1.64-5.64-4.5-6.32V4c0-.83-.67-1.5-1.5-1.5s-1.5.67-1.5 1.5v.68C7.63 5.36 6 7.92 6 11v5l-2 2v1h16v-1l-2-2zm-2 1H8v-6c0-2.48 1.51-4.5 4-4.5s4 2.02 4 4.5v6z";

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var pathData = value is true ? FilledPath : OutlinedPath;
        var converter = new Microsoft.Maui.Controls.Shapes.PathGeometryConverter();
        return converter.ConvertFromInvariantString(pathData);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// The bell's colour: the accent once followed, otherwise the same muted ink the thumbs use.
/// </summary>
/// <remarks>
/// Mirrors <c>LikeFillConverter</c> exactly rather than picking its own colours. On the web the
/// bell is styled by JOINING the like/dislike selector groups for the same reason - a bell that
/// looks like a stranger beside the thumbs reads as a different kind of control.
/// </remarks>
public class FollowBellFillConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true)
            return new SolidColorBrush(AppColors.AccentFill);

        return Application.Current?.RequestedTheme == AppTheme.Dark
            ? new SolidColorBrush(AppColors.Text3)
            : new SolidColorBrush(Colors.Black);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// The bell's colour on the PLAYER surface, which is dark in both app themes.
/// </summary>
/// <remarks>
/// A separate converter rather than a parameter on the page one, because the mistake it prevents is
/// silent: <see cref="FollowBellFillConverter"/> asks the app theme, so on a light-themed device it
/// paints the bell black onto a near-black player. The Blazor app hit exactly this - its player
/// bell was rendering with page-content colours - and the fix there was the same, a palette that
/// does not consult the theme.
/// </remarks>
public class FollowBellPlayerFillConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => new SolidColorBrush(value is true ? AppColors.AccentFill : AppColors.Text3);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
