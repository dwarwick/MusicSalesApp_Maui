using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.Tests.Services;

[TestFixture]
public class ExternalWebLinksTests
{
    [Test]
    public void UploadYourOwnMusicUrl_UsesProductionLearnMorePage()
    {
        Assert.That(ExternalWebLinks.UploadYourOwnMusicUrl, Is.EqualTo("https://streamtunes.net/LearnMore"));
    }
}
