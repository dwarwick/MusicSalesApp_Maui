using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.Tests.Services;

[TestFixture]
public class FlyoutMenuVisibilityPolicyTests
{
    [Test]
    public void ShouldShowUploadYourOwnMusic_MatchesCreatorVisibilityRule()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FlyoutMenuVisibilityPolicy.ShouldShowUploadYourOwnMusic(isLoggedIn: false, isCreator: false), Is.True);
            Assert.That(FlyoutMenuVisibilityPolicy.ShouldShowUploadYourOwnMusic(isLoggedIn: true, isCreator: false), Is.True);
            Assert.That(FlyoutMenuVisibilityPolicy.ShouldShowUploadYourOwnMusic(isLoggedIn: true, isCreator: true), Is.False);
        });
    }
}
