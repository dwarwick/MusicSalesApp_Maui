using Moq;
using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Tests.ViewModels;

[TestFixture]
public class PolicyViewModelTests
{
    private Mock<IAppConfig> _mockAppConfig;
    private PolicyViewModel _viewModel;

    [SetUp]
    public void Setup()
    {
        _mockAppConfig = new Mock<IAppConfig>();
        _mockAppConfig.Setup(c => c.WebBaseUrl).Returns("https://streamtunes.net");
        _viewModel = new PolicyViewModel(_mockAppConfig.Object);
    }

    [Test]
    public void PolicyUrl_BuiltFromWebBaseUrlAndPath()
    {
        _viewModel.PolicyPath = "/privacy-policy";

        Assert.That(_viewModel.PolicyUrl, Is.EqualTo("https://streamtunes.net/privacy-policy"));
    }

    [Test]
    public void PolicyUrl_TermsOfUse()
    {
        _viewModel.PolicyPath = "/terms-of-use";

        Assert.That(_viewModel.PolicyUrl, Is.EqualTo("https://streamtunes.net/terms-of-use"));
    }

    [Test]
    public void PolicyUrl_RefundPolicy()
    {
        _viewModel.PolicyPath = "/user-refund-policy";

        Assert.That(_viewModel.PolicyUrl, Is.EqualTo("https://streamtunes.net/user-refund-policy"));
    }

    [Test]
    public void PolicyUrl_EmptyWhenPathNotSet()
    {
        Assert.That(_viewModel.PolicyUrl, Is.EqualTo(string.Empty));
    }

    [Test]
    public void PolicyTitle_CanBeSet()
    {
        _viewModel.PolicyTitle = "Privacy Policy";

        Assert.That(_viewModel.PolicyTitle, Is.EqualTo("Privacy Policy"));
    }

    [Test]
    public void PolicyUrl_UsesConfiguredBaseUrl()
    {
        _mockAppConfig.Setup(c => c.WebBaseUrl).Returns("https://davidtest.dev");
        var viewModel = new PolicyViewModel(_mockAppConfig.Object);

        viewModel.PolicyPath = "/terms-of-use";

        Assert.That(viewModel.PolicyUrl, Is.EqualTo("https://davidtest.dev/terms-of-use"));
    }
}
