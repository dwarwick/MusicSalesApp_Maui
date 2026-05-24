using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.Tests.Services;

[TestFixture]
public class NavigationRoutesTests
{
    [Test]
    public void LoginEntry_IsAnchoredUnderMusicLibraryRoot()
    {
        Assert.Multiple(() =>
        {
            Assert.That(NavigationRoutes.LoginEntry, Is.EqualTo("//MusicLibrary/login"));
            Assert.That(NavigationRoutes.LoginEntry, Does.StartWith(NavigationRoutes.MusicLibraryRoot + "/"));
            Assert.That(NavigationRoutes.LoginEntry, Is.Not.EqualTo(NavigationRoutes.Login));
        });
    }
}