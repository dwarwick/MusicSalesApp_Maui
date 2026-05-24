namespace MusicSalesApp.Maui.Services;

public static class NavigationStackReplacement
{
    public static Page? FindPageToRemove(IReadOnlyList<Page> navigationStack, Page? previousCurrentPage)
    {
        if (previousCurrentPage == null || navigationStack.Count < 2)
        {
            return null;
        }

        var currentTopPage = navigationStack[^1];
        if (ReferenceEquals(currentTopPage, previousCurrentPage))
        {
            return null;
        }

        for (var i = navigationStack.Count - 2; i >= 0; i--)
        {
            if (ReferenceEquals(navigationStack[i], previousCurrentPage))
            {
                return navigationStack[i];
            }
        }

        return null;
    }
}