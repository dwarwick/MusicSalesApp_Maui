using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.Tests.Services;

/// <summary>
/// Where the players change between one column and two.
/// </summary>
/// <remarks>
/// The widths below are real devices, because the whole point of the threshold is which side of it
/// each one lands on. A tablet in portrait must stay single column - that is what the web does at
/// that width - while the same tablet in landscape must not.
/// </remarks>
[TestFixture]
public class AdaptiveLayoutTests
{
    [TestCase(360, TestName = "Phone portrait")]
    [TestCase(414, TestName = "Large phone portrait")]
    [TestCase(768, TestName = "Tablet portrait")]
    [TestCase(834, TestName = "Large tablet portrait")]
    [TestCase(991, TestName = "One point below the breakpoint")]
    public void StaysSingleColumn(double width)
    {
        Assert.That(AdaptiveLayout.IsWide(width), Is.False);
    }

    [TestCase(992, TestName = "Exactly at the breakpoint")]
    [TestCase(1024, TestName = "Tablet landscape")]
    [TestCase(1194, TestName = "Large tablet landscape")]
    [TestCase(1600, TestName = "Desktop window")]
    public void GoesTwoColumn(double width)
    {
        Assert.That(AdaptiveLayout.IsWide(width), Is.True);
    }

    /// <summary>
    /// A width that has not been measured yet is not wide.
    /// </summary>
    /// <remarks>
    /// Both platforms report 0 or -1 before the first real measure pass. Treating that as wide
    /// would build the two-column layout and tear it down a frame later, and on the playlist page
    /// that means handing the panels between hosts twice on every launch.
    /// </remarks>
    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(double.NaN)]
    public void AnUnmeasuredWidthIsNotWide(double width)
    {
        Assert.That(AdaptiveLayout.IsWide(width), Is.False);
    }

    [Test]
    public void TheBreakpointMatchesTheWeb()
    {
        // The web's md breakpoint ends at 992px, above which .song-stage and .playlist-stage become
        // two-column. Both apps should change shape in the same place.
        Assert.Multiple(() =>
        {
            Assert.That(AdaptiveLayout.WideBreakpoint, Is.EqualTo(992d));
            Assert.That(AdaptiveLayout.SideColumnWidth, Is.EqualTo(360d));
        });
    }

    // --- Capping the content width ---

    [TestCase(360, TestName = "Phone")]
    [TestCase(800, TestName = "Tablet portrait")]
    [TestCase(1100, TestName = "Exactly at the cap")]
    public void NarrowerThanTheCapIsNotInsetAtAll(double width)
    {
        // Every phone lands here, and must lay out exactly as it did before any of this existed.
        Assert.That(AdaptiveLayout.ContentInset(width), Is.EqualTo(0d));
    }

    [Test]
    public void WiderThanTheCapCentresTheRemainder()
    {
        // 1280 - 1100 = 180 left over, half each side.
        Assert.That(AdaptiveLayout.ContentInset(1280), Is.EqualTo(90d));
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void AnUnmeasuredWidthIsNotInset(double width)
    {
        // A negative inset would push content off the leading edge.
        Assert.That(AdaptiveLayout.ContentInset(width), Is.EqualTo(0d));
    }
}
