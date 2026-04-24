using System.Net;
using System.Net.Http.Json;
using System.Text;
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
            .ReturnsAsync((true, string.Empty));

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
            .ReturnsAsync((true, string.Empty));

        var endDate = DateTime.UtcNow.AddDays(10);
        SetupMockSubscriptionStatusResponse(hasSubscription: true, billingSource: "GooglePlay", status: "CANCELLED", endDate: endDate);

        await _authService.TryRestoreBillingAsync();

        Assert.That(_authService.HasActiveSubscription, Is.True);
        Assert.That(_authService.SubscriptionStatus, Is.EqualTo("CANCELLED"));
        Assert.That(_authService.SubscriptionEndDate, Is.EqualTo(endDate));
    }

    [Test]
    public async Task TryRestoreBillingAsync_WhenVerificationFails_DoesNotRefreshStatus()
    {
        _mockBillingService.Setup(b => b.RestorePurchaseAsync())
            .ReturnsAsync(BillingPurchaseResult.Succeeded("token", "order"));
        _mockMusicService.Setup(m => m.VerifyGooglePlayPurchaseAsync("token", "order"))
            .ReturnsAsync((false, "Google Play verification failed on the server."));

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

    [Test]
    public async Task LoginAsync_ReturnsRawResponseBody_WhenErrorBodyIsPlainText()
    {
        var messageHandler = new Mock<HttpMessageHandler>();
        messageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("Email and password are required.", Encoding.UTF8, "text/plain")
            });

        var httpClient = new HttpClient(messageHandler.Object)
        {
            BaseAddress = new Uri("https://test.example.com/")
        };

        _mockHttpClientFactory.Setup(f => f.CreateClient("MusicSalesApi")).Returns(httpClient);

        var (success, error) = await _authService.LoginAsync("user@example.com", "bad-password");

        Assert.Multiple(() =>
        {
            Assert.That(success, Is.False);
            Assert.That(error, Does.Contain("Email and password are required."));
        });
    }

    private void SetupMockSubscriptionStatusResponse(bool hasSubscription, string? billingSource, string? status = null, DateTime? endDate = null)
    {
        var messageHandler = new Mock<HttpMessageHandler>();
        messageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { HasSubscription = hasSubscription, BillingSource = billingSource, Status = status, EndDate = endDate })
            });

        var httpClient = new HttpClient(messageHandler.Object)
        {
            BaseAddress = new Uri("https://test.example.com/")
        };

        _mockHttpClientFactory.Setup(f => f.CreateClient("MusicSalesApi")).Returns(httpClient);
    }
}
