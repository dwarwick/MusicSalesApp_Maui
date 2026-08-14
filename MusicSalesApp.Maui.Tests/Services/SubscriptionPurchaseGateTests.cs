using Moq;
using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.Tests.Services;

[TestFixture]
public class SubscriptionPurchaseGateTests
{
    private Mock<IAuthService> _mockAuthService = null!;
    private Mock<IAlertService> _mockAlertService = null!;
    private Mock<INavigationService> _mockNavigationService = null!;

    [SetUp]
    public void SetUp()
    {
        _mockAuthService = new Mock<IAuthService>();
        _mockAlertService = new Mock<IAlertService>();
        _mockNavigationService = new Mock<INavigationService>();
    }

    [Test]
    public async Task EnsureSignedInAsync_WhenSignedIn_AllowsThePurchaseWithoutPrompting()
    {
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(true);

        var canPurchase = await SubscriptionPurchaseGate.EnsureSignedInAsync(
            _mockAuthService.Object, _mockAlertService.Object, _mockNavigationService.Object);

        Assert.That(canPurchase, Is.True);
        _mockAlertService.Verify(
            a => a.ShowConfirmAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    /// <summary>
    /// A purchase made while signed out carries no app account token and cannot be verified, because
    /// the server's verify endpoint is authenticated. The customer is charged and gets nothing.
    /// </summary>
    [Test]
    public async Task EnsureSignedInAsync_WhenSignedOut_RefusesThePurchase()
    {
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(false);
        _mockAlertService
            .Setup(a => a.ShowConfirmAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(false);

        var canPurchase = await SubscriptionPurchaseGate.EnsureSignedInAsync(
            _mockAuthService.Object, _mockAlertService.Object, _mockNavigationService.Object);

        Assert.That(canPurchase, Is.False);
    }

    [Test]
    public async Task EnsureSignedInAsync_WhenSignedOutAndTheyAccept_SendsThemToSignIn()
    {
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(false);
        _mockAlertService
            .Setup(a => a.ShowConfirmAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);

        var canPurchase = await SubscriptionPurchaseGate.EnsureSignedInAsync(
            _mockAuthService.Object, _mockAlertService.Object, _mockNavigationService.Object);

        Assert.That(canPurchase, Is.False, "The purchase must still not proceed on this pass.");
        _mockNavigationService.Verify(
            n => n.GoToAsync(NavigationRoutes.LoginEntry, It.Is<IDictionary<string, object>>(p =>
                p.ContainsKey(NavigationRoutes.ReturnToHomeAfterAuthParameter))),
            Times.Once);
    }

    /// <summary>
    /// Registering instead of signing in has to land in the same place, which it does because the
    /// login, register and verify-email screens hand this flag between themselves.
    /// </summary>
    [Test]
    public async Task GoToSignInAsync_FlagsTheReturnToHomeAfterAuth()
    {
        await SubscriptionPurchaseGate.GoToSignInAsync(_mockNavigationService.Object);

        _mockNavigationService.Verify(
            n => n.GoToAsync(NavigationRoutes.LoginEntry, It.Is<IDictionary<string, object>>(p =>
                p.ContainsKey(NavigationRoutes.ReturnToHomeAfterAuthParameter)
                && Equals(p[NavigationRoutes.ReturnToHomeAfterAuthParameter], true))),
            Times.Once);
    }

    [Test]
    public void PreviewLimitPrompt_WhenSignedOut_AsksForSignInRatherThanOfferingToSubscribe()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SubscriptionPurchaseGate.PreviewLimitAccept(isSignedIn: false),
                Is.EqualTo(SubscriptionPurchaseGate.SignInRequiredAccept));
            Assert.That(SubscriptionPurchaseGate.PreviewLimitMessage(isSignedIn: false),
                Does.Contain("Sign in"));
        });
    }

    [Test]
    public void PreviewLimitPrompt_WhenSignedIn_OffersToSubscribe()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SubscriptionPurchaseGate.PreviewLimitAccept(isSignedIn: true),
                Is.EqualTo(SubscriptionPurchaseGate.PreviewLimitSubscribeAccept));
            Assert.That(SubscriptionPurchaseGate.PreviewLimitMessage(isSignedIn: true),
                Is.EqualTo(SubscriptionPurchaseGate.PreviewLimitSubscribeMessage));
        });
    }

    [Test]
    public async Task EnsureSignedInAsync_WhenSignedOutAndTheyDecline_DoesNotNavigate()
    {
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(false);
        _mockAlertService
            .Setup(a => a.ShowConfirmAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(false);

        await SubscriptionPurchaseGate.EnsureSignedInAsync(
            _mockAuthService.Object, _mockAlertService.Object, _mockNavigationService.Object);

        _mockNavigationService.Verify(n => n.GoToAsync(It.IsAny<string>()), Times.Never);
    }
}
