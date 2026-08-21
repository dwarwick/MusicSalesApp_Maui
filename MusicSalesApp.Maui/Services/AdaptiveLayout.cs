#nullable enable
namespace MusicSalesApp.Maui.Services;

/// <summary>
/// Where the players change shape between one column and two.
/// </summary>
/// <remarks>
/// <para>
/// Keyed on WINDOW WIDTH, never on device type. A tablet in portrait is around 768-834 points and
/// should stay single column - which is what the web does at that width - while the same tablet in
/// landscape should not, and either one running in split view is narrower again. Device idiom
/// cannot distinguish any of those, so it would hand a portrait tablet a layout built for
/// landscape.
/// </para>
/// <para>
/// The threshold is the web's own: its md breakpoint ends at 992px, above which .song-stage and
/// .playlist-stage become two-column. Sharing the number means both apps change shape at the same
/// place instead of at two arbitrary ones.
/// </para>
/// </remarks>
internal static class AdaptiveLayout
{
    /// <summary>The width at or above which the players lay out in two columns.</summary>
    public const double WideBreakpoint = 992d;

    /// <summary>
    /// The widest the content is allowed to get before it stops growing and starts centring.
    /// </summary>
    /// <remarks>
    /// Applied as side margins, not as MaximumWidthRequest. The two do not compose in MAUI: a
    /// layout set to Center sizes to its content and never reaches the cap at all, while one set to
    /// Fill reaches it but sits against the leading edge.
    /// </remarks>
    public const double ReadableWidth = 1100d;

    /// <summary>How wide the trailing column is when there are two.</summary>
    public const double SideColumnWidth = 360d;

    /// <summary>
    /// Whether a window this wide should use the two-column layout.
    /// </summary>
    /// <remarks>
    /// A width of zero or less is NOT wide. Both platforms report 0 or -1 before the first real
    /// measure pass, and treating that as wide would build the two-column layout, then tear it
    /// down a frame later when the true width arrived.
    /// </remarks>
    public static bool IsWide(double width) => width >= WideBreakpoint;

    /// <summary>
    /// How much to inset each side so content stops growing at <see cref="ReadableWidth"/> and
    /// centres beyond it.
    /// </summary>
    /// <remarks>
    /// Never negative. A window narrower than the cap - every phone - gets zero, which leaves the
    /// layout exactly as it was before any of this existed.
    /// </remarks>
    public static double ContentInset(double width) =>
        width <= ReadableWidth ? 0d : (width - ReadableWidth) / 2d;
}
