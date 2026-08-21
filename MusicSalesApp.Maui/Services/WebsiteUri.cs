#nullable enable
namespace MusicSalesApp.Maui.Services;

/// <summary>
/// Turns a creator-typed website into something safe to hand to the device launcher.
/// </summary>
/// <remarks>
/// A free function rather than a method on the view that uses it, for two reasons: it is pure,
/// and a view is not testable in this repo's test project - which compiles services and view
/// models directly but cannot build XAML-backed types. Parsing that guards what gets opened on
/// someone's phone should not be the part that goes untested.
/// </remarks>
internal static class WebsiteUri
{
    /// <summary>
    /// Parse <paramref name="value"/> into a browsable address, or refuse it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two jobs. A bare host gets <c>https://</c>, because creators type a domain rather than a
    /// URL and the server stores the field with nothing but a Trim - no scheme is added and
    /// nothing is validated on the way in.
    /// </para>
    /// <para>
    /// And the scheme is checked against an allow-list rather than merely parsed. <c>javascript:</c>,
    /// <c>file:</c> and <c>tel:</c> are all perfectly well-formed absolute URIs, which is exactly
    /// why successful parsing is not the test - this value comes from a free-text field and ends
    /// up at the launcher.
    /// </para>
    /// </remarks>
    public static bool TryParse(string? value, out Uri? uri)
    {
        uri = null;

        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return false;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed)
            && !Uri.TryCreate($"https://{trimmed}", UriKind.Absolute, out parsed))
        {
            return false;
        }

        if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        uri = parsed;
        return true;
    }
}
