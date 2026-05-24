using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.Tests.Services;

[TestFixture]
public class NavigationStackReplacementTests
{
    [Test]
    public void FindPageToRemove_ReturnsPreviousCurrentPage_WhenItRemainsUnderNewTopPage()
    {
        var rootPage = new ContentPage();
        var previousCurrentPage = new ContentPage();
        var newTopPage = new ContentPage();

        var pageToRemove = NavigationStackReplacement.FindPageToRemove(
            [rootPage, previousCurrentPage, newTopPage],
            previousCurrentPage);

        Assert.That(pageToRemove, Is.SameAs(previousCurrentPage));
    }

    [Test]
    public void FindPageToRemove_ReturnsNull_WhenPreviousCurrentPageIsStillTopPage()
    {
        var rootPage = new ContentPage();
        var previousCurrentPage = new ContentPage();

        var pageToRemove = NavigationStackReplacement.FindPageToRemove(
            [rootPage, previousCurrentPage],
            previousCurrentPage);

        Assert.That(pageToRemove, Is.Null);
    }

    [Test]
    public void FindPageToRemove_ReturnsNull_WhenPreviousCurrentPageIsNotInStack()
    {
        var rootPage = new ContentPage();
        var previousCurrentPage = new ContentPage();
        var newTopPage = new ContentPage();

        var pageToRemove = NavigationStackReplacement.FindPageToRemove(
            [rootPage, newTopPage],
            previousCurrentPage);

        Assert.That(pageToRemove, Is.Null);
    }

    [Test]
    public void FindPageToRemove_ReturnsNull_WhenPreviousCurrentPageIsNull()
    {
        var rootPage = new ContentPage();
        var newTopPage = new ContentPage();

        var pageToRemove = NavigationStackReplacement.FindPageToRemove(
            [rootPage, newTopPage],
            null);

        Assert.That(pageToRemove, Is.Null);
    }
}