namespace MusicSalesApp.Maui.Views;

internal sealed class NowPlayingDrawerController
{
    public const double DefaultCollapsedHeight = 44;
    public const double DefaultExpandedHeight = 168;

    public NowPlayingDrawerController(
        double collapsedHeight = DefaultCollapsedHeight,
        double expandedHeight = DefaultExpandedHeight)
    {
        if (collapsedHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(collapsedHeight));
        }

        if (expandedHeight <= collapsedHeight)
        {
            throw new ArgumentOutOfRangeException(nameof(expandedHeight));
        }

        CollapsedHeight = collapsedHeight;
        ExpandedHeight = expandedHeight;
    }

    public double CollapsedHeight { get; }

    public double ExpandedHeight { get; }

    public bool IsExpanded { get; private set; }

    public double Expand()
    {
        IsExpanded = true;
        return ExpandedHeight;
    }

    public double Collapse()
    {
        IsExpanded = false;
        return CollapsedHeight;
    }

    public double Toggle() => IsExpanded ? Collapse() : Expand();

    public double ClampDraggedHeight(double dragStartHeight, double totalDragY)
    {
        var requestedHeight = dragStartHeight - totalDragY;
        return Math.Clamp(requestedHeight, CollapsedHeight, ExpandedHeight);
    }

    public double ResolveSnapHeight(double currentHeight)
    {
        var midpoint = (CollapsedHeight + ExpandedHeight) / 2;
        return currentHeight >= midpoint ? Expand() : Collapse();
    }
}