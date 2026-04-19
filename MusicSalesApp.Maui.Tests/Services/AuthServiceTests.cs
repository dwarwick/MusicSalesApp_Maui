using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.Tests.Services;

[TestFixture]
public class AuthServiceTests
{
    private Mock<IHttpClientFactory> _mockHttpClientFactory;
    private Mock<ILogger<AuthService>> _mockLogger;
    private Mock<IBillingService> _mockBillingService;
    private Mock<IMusicService> _mockMusicService;
    private AuthService _authService;

    [SetUp]
    public void Setup()
    {
        _mockHttpClientFactory = new Mock<IHttpClientFactory>();
        _mockLogger = new Mock<ILogger<AuthService>>();
        _mockBillingService = new Mock<IBillingService>();
        _mockMusicService = new Mock<IMusicService>();

        _authService = new AuthService(
            _mockHttpClientFactory.Object,
            _mockLogger.Object,
            _mockBillingService.Object,
            _mockMusicService.Object);
    }

    [Test]
    public async Task TryRestoreBillingAsync_WhenNoPurchaseFound_DoesNotVerify()
    {
        _mockBillingService.Setup(b => b.RestorePurchaseAsync())
            .ReturnsAsync((BillingPurchaseResult?)null);

        await _authService.TryRestoreBillingAsync();

        _mockMusicService.Verify(m => m.VerifyGooglePlayPurchaseAsync(It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    [Test]
    public async Task TryRestoreBillingAsync_WhenRestoreReturnsFailed_DoesNotVerify()
    {
        _mockBillingService.Setup(b => b.RestorePurchaseAsync())
            .ReturnsAsync(BillingPurchaseResult.Failed("No purchases found"));

        await _authService.TryRestoreBillingAsync();

        _mockMusicService.Verify(m => m.VerifyGooglePlayPurchaseAsync(It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    [Test]
    public async Task TryRestoreBillingAsync_WhenPurchaseFound_VerifiesWithServer()
    {
        var purchaseToken = "test-purchase-token";
        var orderId = "GPA.1234-5678";

        _mockBillingService.Setup(b => b.RestorePurchaseAsync())
            .ReturnsAsync(BillingPurchaseResult.Succeeded(purchaseToken, orderId));
        _mockMusicService.Setup(m => m.VerifyGooglePlayPurchaseAsync(purchaseToken, orderId))
            .ReturnsAsync(true);

        // Mock HttpClient for RefreshUserStatusAsync
        SetupMockSubscriptionStatusResponse(hasSubscription: true, billingSource: "GooglePlay");

        await _authService.TryRestoreBillingAsync();

        _mockMusicService.Verify(m => m.VerifyGooglePlayPurchaseAsync(purchaseToken, orderId), Times.Once);
    }

    [Test]
    public async Task TryRestoreBillingAsync_WhenVerificationSucceeds_RefreshesSubscriptionStatus()
    {
        _mockBillingService.Setup(b => b.RestorePurchaseAsync())
            .ReturnsAsync(BillingPurchaseResult.Succeeded("token", "order"));
        _mockMusicService.Setup(m => m.VerifyGooglePlayPurchaseAsync("token", "order"))
            .ReturnsAsync(true);

        SetupMockSubscriptionStatusResponse(hasSubscription: true, billingSource: "GooglePlay");

        await _authService.TryRestoreBillingAsync();

        Assert.That(_authService.HasActiveSubscription, Is.True);
    }

    [Test]
    public async Task TryRestoreBillingAsync_WhenVerificationFails_DoesNotRefreshStatus()
    {
        _mockBillingService.Setup(b => b.RestorePurchaseAsync())
            .ReturnsAsync(BillingPurchaseResult.Succeeded("token", "order"));
        _mockMusicService.Setup(m => m.VerifyGooglePlayPurchaseAsync("token", "order"))
            .ReturnsAsync(false);

        await _authService.TryRestoreBillingAsync();

        // RefreshUserStatusAsync was NOT called, so HttpClientFactory should not have been used
        _mockHttpClientFactory.Verify(f => f.CreateClient(It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task TryRestoreBillingAsync_WhenBillingServiceThrows_DoesNotThrow()
    {
        _mockBillingService.Setup(b => b.RestorePurchaseAsync())
            .ThrowsAsync(new Exception("Billing unavailable"));

        // Should not throw — errors are logged and swallowed
        Assert.DoesNotThrowAsync(() => _authService.TryRestoreBillingAsync());
    }

    [Test]
    public async Task TryRestoreBillingAsync_WhenMusicServiceThrows_DoesNotThrow()
    {
        _mockBillingService.Setup(b => b.RestorePurchaseAsync())
            .ReturnsAsync(BillingPurchaseResult.Succeeded("token", "order"));
        _mockMusicService.Setup(m => m.VerifyGooglePlayPurchaseAsync(It.IsAny<string>(), It.IsAny<string?>()))
            .ThrowsAsync(new HttpRequestException("Network error"));

        Assert.DoesNotThrowAsync(() => _authService.TryRestoreBillingAsync());
    }

    private void SetupMockSubscriptionStatusResponse(bool hasSubscription, string? billingSource)
    {
        var messageHandler = new Mock<HttpMessageHandler>();
        messageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { HasSubscription = hasSubscription, BillingSource = billingSource })
            });

        var httpClient = new HttpClient(messageHandler.Object)
        {
            BaseAddress = new Uri("https://test.example.com/")
        };

        _mockHttpClientFactory.Setup(f => f.CreateClient("MusicSalesApi")).Returns(httpClient);
    }
}
