using System.Globalization;
using Microsoft.Maui.Controls.Shapes;
using MusicSalesApp.Maui.Resources.Styles;

namespace MusicSalesApp.Maui.Views;

/// <summary>
/// A two-state icon: one Material Design path when the state is on, another when it is off.
/// </summary>
/// <remarks>
/// The like, dislike and follow icons are the same control three times over - a filled glyph and an
/// outlined one, chosen by a bool - and each had its own copy of the parse-and-pick body. Only the
/// two paths and, for dislike, which value counts as "on" actually differ.
///
/// <para>
/// The parsed geometries are cached per converter instance. These converters are declared in
/// resource dictionaries inside DataTemplates, so a virtualised list builds one per card and then
/// rebinds it on every recycle - re-parsing a ~180 character path each time, on the UI thread,
/// during a scroll. Caching per instance rather than statically keeps each Path's Geometry its own
/// object, which is how it behaved before.
/// </para>
/// </remarks>
public abstract class IconGlyphConverter : IValueConverter
{
    private Geometry? _onGeometry;
    private Geometry? _offGeometry;

    /// <summary>The glyph drawn when the state is on.</summary>
    protected abstract string OnPathData { get; }

    /// <summary>The glyph drawn when the state is off.</summary>
    protected abstract string OffPathData { get; }

    /// <summary>
    /// Which bound value counts as "on". True for most; dislike inverts it, because a thumbs-down
    /// is filled when the rating is <c>false</c>.
    /// </summary>
    protected virtual bool IsOn(object? value) => value is true;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        IsOn(value)
            ? _onGeometry ??= Parse(OnPathData)
            : _offGeometry ??= Parse(OffPathData);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static Geometry? Parse(string pathData) =>
        new PathGeometryConverter().ConvertFromInvariantString(pathData) as Geometry;
}

/// <summary>
/// The colour of a two-state icon: an accent when on, muted ink when off.
/// </summary>
/// <remarks>
/// Off is read from the app theme, because these sit on page content that follows it. The player is
/// the exception and says so - see <see cref="FollowBellPlayerFillConverter"/>.
/// </remarks>
public abstract class IconFillConverter : IValueConverter
{
    /// <summary>The colour when the state is on.</summary>
    protected abstract Color OnColor { get; }

    /// <inheritdoc cref="IconGlyphConverter.IsOn"/>
    protected virtual bool IsOn(object? value) => value is true;

    /// <summary>The colour when the state is off.</summary>
    protected virtual Color OffColor =>
        Application.Current?.RequestedTheme == AppTheme.Dark ? AppColors.Text3 : Colors.Black;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        new SolidColorBrush(IsOn(value) ? OnColor : OffColor);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Material Design thumbs up: filled vs outlined (same SVGs as the web app).</summary>
public class LikeGlyphConverter : IconGlyphConverter
{
    protected override string OnPathData => "M1 21h4V9H1v12zm22-11c0-1.1-.9-2-2-2h-6.31l.95-4.57.03-.32c0-.41-.17-.79-.44-1.06L14.17 1 7.59 7.59C7.22 7.95 7 8.45 7 9v10c0 1.1.9 2 2 2h9c.83 0 1.54-.5 1.84-1.22l3.02-7.05c.09-.23.14-.47.14-.73v-2z";
    protected override string OffPathData => "M9 21h9c.83 0 1.54-.5 1.84-1.22l3.02-7.05c.09-.23.14-.47.14-.73v-2c0-1.1-.9-2-2-2h-6.31l.95-4.57.03-.32c0-.41-.17-.79-.44-1.06L14.17 1 7.58 7.59C7.22 7.95 7 8.45 7 9v10c0 1.1.9 2 2 2zM9 9l4.34-4.34L12 10h9v2l-3 7H9V9zM1 9h4v12H1z";
}

/// <summary>Material Design thumbs down: filled vs outlined (same SVGs as the web app).</summary>
public class DislikeGlyphConverter : IconGlyphConverter
{
    protected override string OnPathData => "M15 3H6c-.83 0-1.54.5-1.84 1.22l-3.02 7.05c-.09.23-.14.47-.14.73v2c0 1.1.9 2 2 2h6.31l-.95 4.57-.03.32c0 .41.17.79.44 1.06L9.83 23l6.59-6.59c.36-.36.58-.86.58-1.41V5c0-1.1-.9-2-2-2zm4 0v12h4V3h-4z";
    protected override string OffPathData => "M15 3H6c-.83 0-1.54.5-1.84 1.22l-3.02 7.05c-.09.23-.14.47-.14.73v2c0 1.1.9 2 2 2h6.31l-.95 4.57-.03.32c0 .41.17.79.44 1.06L9.83 23l6.58-6.59c.37-.36.59-.86.59-1.41V5c0-1.1-.9-2-2-2zm0 12l-4.34 4.34L12 14H3v-2l3-7h9v10zm4-12h4v12h-4z";

    /// <summary>A thumbs-down is filled when the rating is <c>false</c>, not true.</summary>
    protected override bool IsOn(object? value) => value is false;
}

/// <summary>
/// The follow bell's outline, filled once the artist is followed.
/// </summary>
/// <remarks>
/// The same two Material Design paths the web app draws, so the two clients cannot drift into
/// different bells. Filled means following - that is the whole state, there is no third value.
/// </remarks>
public class FollowBellGlyphConverter : IconGlyphConverter
{
    protected override string OnPathData => "M12 22c1.1 0 2-.9 2-2h-4c0 1.1.9 2 2 2zm6-6v-5c0-3.07-1.63-5.64-4.5-6.32V4c0-.83-.67-1.5-1.5-1.5s-1.5.67-1.5 1.5v.68C7.64 5.36 6 7.92 6 11v5l-2 2v1h16v-1l-2-2z";
    protected override string OffPathData => "M12 22c1.1 0 2-.9 2-2h-4c0 1.1.9 2 2 2zm6-6v-5c0-3.07-1.64-5.64-4.5-6.32V4c0-.83-.67-1.5-1.5-1.5s-1.5.67-1.5 1.5v.68C7.63 5.36 6 7.92 6 11v5l-2 2v1h16v-1l-2-2zm-2 1H8v-6c0-2.48 1.51-4.5 4-4.5s4 2.02 4 4.5v6z";
}

/// <summary>The thumbs-up colour: the accent once rated up.</summary>
public class LikeFillConverter : IconFillConverter
{
    protected override Color OnColor => AppColors.AccentFill;
}

/// <summary>The thumbs-down colour: the danger tone once rated down.</summary>
public class DislikeFillConverter : IconFillConverter
{
    protected override Color OnColor => AppColors.Danger;

    /// <inheritdoc cref="DislikeGlyphConverter.IsOn"/>
    protected override bool IsOn(object? value) => value is false;
}

/// <summary>
/// The bell's colour: the accent once followed, otherwise the same muted ink the thumbs use.
/// </summary>
/// <remarks>
/// Shares <see cref="IconFillConverter"/> with the thumbs rather than picking its own colours. On
/// the web the bell is styled by JOINING the like/dislike selector groups for the same reason - a
/// bell that looks like a stranger beside the thumbs reads as a different kind of control.
/// </remarks>
public class FollowBellFillConverter : IconFillConverter
{
    protected override Color OnColor => AppColors.AccentFill;
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
public class FollowBellPlayerFillConverter : IconFillConverter
{
    protected override Color OnColor => AppColors.AccentFill;

    /// <summary>Never theme-dependent: the player is dark whatever the device is set to.</summary>
    protected override Color OffColor => AppColors.Text3;
}
