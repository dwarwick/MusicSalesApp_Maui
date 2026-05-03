using MusicSalesApp.Maui.Views;

namespace MusicSalesApp.Maui.Tests.Views;

[TestFixture]
public class NowPlayingDrawerControllerTests
{
    [Test]
    public void Toggle_SwitchesBetweenCollapsedAndExpandedHeights()
    {
        var controller = new NowPlayingDrawerController(44, 168);

        var expandedHeight = controller.Toggle();
        var collapsedHeight = controller.Toggle();

        Assert.Multiple(() =>
        {
            Assert.That(expandedHeight, Is.EqualTo(168));
            Assert.That(collapsedHeight, Is.EqualTo(44));
            Assert.That(controller.IsExpanded, Is.False);
        });
    }

    [Test]
    public void ClampDraggedHeight_ClampsOutsideBounds()
    {
        var controller = new NowPlayingDrawerController(44, 168);

        var tooSmall = controller.ClampDraggedHeight(44, 80);
        var tooLarge = controller.ClampDraggedHeight(44, -400);

        Assert.Multiple(() =>
        {
            Assert.That(tooSmall, Is.EqualTo(44));
            Assert.That(tooLarge, Is.EqualTo(168));
        });
    }

    [Test]
    public void ResolveSnapHeight_ExpandsWhenPastMidpoint()
    {
        var controller = new NowPlayingDrawerController(44, 168);

        var snappedHeight = controller.ResolveSnapHeight(120);

        Assert.Multiple(() =>
        {
            Assert.That(snappedHeight, Is.EqualTo(168));
            Assert.That(controller.IsExpanded, Is.True);
        });
    }

    [Test]
    public void ResolveSnapHeight_CollapsesWhenBeforeMidpoint()
    {
        var controller = new NowPlayingDrawerController(44, 168);

        var snappedHeight = controller.ResolveSnapHeight(80);

        Assert.Multiple(() =>
        {
            Assert.That(snappedHeight, Is.EqualTo(44));
            Assert.That(controller.IsExpanded, Is.False);
        });
    }
}