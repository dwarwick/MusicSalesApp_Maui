using Moq;
using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.Tests.Services;

[TestFixture]
public class TestingServerBannerServiceTests
{
    private Mock<IAppConfig> _mockAppConfig = default!;
    private TestingServerBannerService _service = default!;

    [SetUp]
    public void Setup()
    {
        _mockAppConfig = new Mock<IAppConfig>();
        SetUrls("https://streamtunes.net", "https://streamtunes.net");
        _service = new TestingServerBannerService(_mockAppConfig.Object);
    }

    [Test]
    public void GetBannerInfo_ShowsBanner_WhenResolvedUrlUsesDavidTestHost()
    {
        SetUrls("https://davidtest.dev", "https://davidtest.dev");

        var bannerInfo = _service.GetBannerInfo();

        Assert.Multiple(() =>
        {
            Assert.That(bannerInfo.IsVisible, Is.True);
            Assert.That(bannerInfo.MessagePrefix, Is.EqualTo("Streamtunes Testing - Backend Server is"));
            Assert.That(bannerInfo.Url, Is.EqualTo("https://davidtest.dev"));
            Assert.That(bannerInfo.DisplayText, Is.EqualTo("Streamtunes Testing - Backend Server is https://davidtest.dev"));
        });
    }

    [Test]
    public void GetBannerInfo_UsesApiBaseUrl_WhenWebBaseUrlIsBlank()
    {
        SetUrls("https://davidtest.dev", string.Empty);

        var bannerInfo = _service.GetBannerInfo();

        Assert.Multiple(() =>
        {
            Assert.That(bannerInfo.IsVisible, Is.True);
            Assert.That(bannerInfo.Url, Is.EqualTo("https://davidtest.dev"));
            Assert.That(bannerInfo.DisplayText, Is.EqualTo("Streamtunes Testing - Backend Server is https://davidtest.dev"));
        });
    }

    [TestCase("https://streamtunes.net")]
    [TestCase("https://localhost:7173")]
    [TestCase("http://10.0.2.2:5162")]
    public void GetBannerInfo_HidesBanner_WhenResolvedUrlIsNotDavidTest(string url)
    {
        SetUrls(url, url);

        var bannerInfo = _service.GetBannerInfo();

        Assert.Multiple(() =>
        {
            Assert.That(bannerInfo.IsVisible, Is.False);
            Assert.That(bannerInfo.MessagePrefix, Is.Empty);
            Assert.That(bannerInfo.Url, Is.Empty);
            Assert.That(bannerInfo.DisplayText, Is.Empty);
        });
    }

    private void SetUrls(string apiBaseUrl, string webBaseUrl)
    {
        _mockAppConfig.SetupGet(x => x.ApiBaseUrl).Returns(apiBaseUrl);
        _mockAppConfig.SetupGet(x => x.WebBaseUrl).Returns(webBaseUrl);
    }
}