#nullable enable
namespace MusicSalesApp.Maui.Resources.Styles;

/// <summary>
/// The C# doorway into the colour tokens declared in <c>Resources/Styles/Colors.xaml</c>.
///
/// <para>
/// <b>Not every surface in this app is XAML, and that is why this exists.</b> The equalizer play
/// button is drawn with SkiaSharp, two dialogs are built entirely in C#, and several converters
/// return a <see cref="Color"/> per row. Every one of them used to carry its own hex literals -
/// 67 of them - which meant a XAML-only restyle silently missed them and the app shipped two
/// palettes at once. Reading the tokens here keeps those surfaces on the same palette as the
/// markup without duplicating the values.
/// </para>
///
/// <para>
/// <b>Prefer <see cref="Themed"/> over <see cref="Get"/> where the caller can use it.</b> A pair
/// resolved through <c>SetAppThemeColor</c> follows the OS theme for the lifetime of the element;
/// a single colour read at construction time freezes whatever the theme was when the page was
/// built, which is the bug behind screens that only pick up dark mode after a relaunch.
/// </para>
/// </summary>
internal static class AppColors
{
    /// <summary>One token by key, or <paramref name="fallback"/> if the dictionary has no such key.</summary>
    /// <remarks>
    /// The fallback exists for the window before <c>Application.Current</c> has its resources -
    /// early startup, and unit tests that construct a view without an Application. It is not a
    /// place to keep a second opinion about the palette: it should always be the same value the
    /// token holds, so that a miss is invisible rather than off-brand.
    /// </remarks>
    public static Color Get(string key, string fallback) =>
        Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Color color
            ? color
            : Color.FromArgb(fallback);

    /// <summary>The light/dark pair for a token, for handing to <c>SetAppThemeColor</c>.</summary>
    public static (Color Light, Color Dark) Themed(
        string lightKey, string darkKey, string lightFallback, string darkFallback) =>
        (Get(lightKey, lightFallback), Get(darkKey, darkFallback));

    /// <summary>Resolve a pair against the CURRENT theme. Frozen at the moment it is called.</summary>
    /// <remarks>
    /// Only for callers that cannot use <c>SetAppThemeColor</c> - a converter returning a value,
    /// or a Skia paint. Anything attached to a visual element should use the pair instead.
    /// </remarks>
    public static Color ForCurrentTheme(
        string lightKey, string darkKey, string lightFallback, string darkFallback) =>
        Application.Current?.RequestedTheme == AppTheme.Dark
            ? Get(darkKey, darkFallback)
            : Get(lightKey, lightFallback);

    // Brand ---------------------------------------------------------------------------------
    public static Color Blue => Get("StBlue", "#0186FD");
    public static Color BlueBright => Get("StBlueBright", "#02B8FD");
    public static Color BlueDeep => Get("StBlueDeep", "#0166D6");
    public static Color Amber => Get("StAmber", "#FFA500");

    // Player - flat, dark in both themes ------------------------------------------------------
    public static Color PlayerText3 => Get("PlayerText3", "#8BA3C7");

    /// <summary>The players' page background. Used for the shell chrome on those pages too.</summary>
    public static Color PlayerBg => Get("PlayerBg", "#070D16");

    /// <summary>Primary text on the player surface.</summary>
    public static Color PlayerText => Get("PlayerText", "#FFFFFF");

    /// <summary>The navigation bar in dark theme, restored when leaving a player page.</summary>
    public static Color NavBarDark => Get("NavBarDark", "#0D1727");

    /// <summary>Secondary text on the player surface.</summary>
    public static Color PlayerText2 => Get("PlayerText2", "#C8EAFD");

    // The now-playing bar paints itself in code, so it needs these by name --------------------
    public static Color PlayerBarDark => Get("PlayerBarDark", "#0D1727");
    public static Color ProgressTrack => Get("ProgressTrack", "#29FFFFFF");
    public static Color TimeText => Get("TimeText", "#8BA3C7");
    public static Color Gray100 => Get("Gray100", "#E1E1E1");
    public static Color Gray200 => Get("Gray200", "#C8C8C8");
    public static Color Gray300 => Get("Gray300", "#ACACAC");
    public static Color Gray500 => Get("Gray500", "#6E6E6E");
    public static Color Gray600 => Get("Gray600", "#404040");

    /// <summary>
    /// The accent used as a FILL, resolved for the current theme.
    /// </summary>
    /// <remarks>
    /// Dark keeps the bright fill rather than dimming it; the foreground goes near-black instead.
    /// Pair this with <see cref="OnAccent"/>, never with white.
    /// </remarks>
    public static Color AccentFill =>
        ForCurrentTheme("AccentFillLight", "AccentFillDark", "#0166D6", "#02B8FD");

    /// <summary>The only foreground allowed on <see cref="AccentFill"/>.</summary>
    public static Color OnAccent =>
        ForCurrentTheme("OnAccentLight", "OnAccentDark", "#FFFFFF", "#04121F");

    public static Color Accent =>
        ForCurrentTheme("AccentLight", "AccentDark", "#0166D6", "#02B8FD");

    // Text ------------------------------------------------------------------------------------
    public static Color Text => ForCurrentTheme("TextLight", "TextDark", "#0F1B2D", "#E9EEF5");
    public static Color Text2 => ForCurrentTheme("Text2Light", "Text2Dark", "#4A5B70", "#A8B6C8");
    public static Color Text3 => ForCurrentTheme("Text3Light", "Text3Dark", "#5E6F85", "#8BA3C7");

    // Surfaces --------------------------------------------------------------------------------
    public static Color Surface => ForCurrentTheme("SurfaceLight", "SurfaceDark", "#FFFFFF", "#2A323D");
    public static Color SurfaceHover =>
        ForCurrentTheme("SurfaceHoverLight", "SurfaceHoverDark", "#F1F4F8", "#333C49");
    public static Color Line => ForCurrentTheme("LineLight", "LineDark", "#DEE2E6", "#1AFFFFFF");

    // Status ----------------------------------------------------------------------------------
    public static Color Danger => ForCurrentTheme("DangerLight", "DangerDark", "#B3261E", "#FFB4AB");
    public static Color DangerSoft =>
        ForCurrentTheme("DangerSoftLight", "DangerSoftDark", "#FFF3F3", "#3A1A1A");
    public static Color Genre => ForCurrentTheme("GenreLight", "GenreDark", "#7D3C98", "#A594F6");

    /// <summary>A translucent wash of the accent, for the surface behind a selected option.</summary>
    public static Color AccentSurface =>
        Application.Current?.RequestedTheme == AppTheme.Dark
            ? AccentFill.WithAlpha(0.28f)
            : AccentFill.WithAlpha(0.12f);
}
