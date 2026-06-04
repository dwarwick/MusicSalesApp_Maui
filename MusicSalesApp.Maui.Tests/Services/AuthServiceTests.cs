using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Maui.Authentication;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using MusicSalesApp.Common.Helpers;
using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Tests.Services;

[TestFixture]
public class AuthServiceTests
{
    private Mock<IHttpClientFactory> _mockHttpClientFactory;
    private Mock<IConfiguration> _mockConfiguration;
    private Mock<ILogger<AuthService>> _mockLogger;
    private Mock<IWebAuthenticatorService> _mockWebAuthenticatorService;
    private Mock<IBillingService> _mockBillingService;
    private Mock<IMusicService> _mockMusicService;
    private AuthService _authService;

    [SetUp]
    public void Setup()
    {
        _mockHttpClientFactory = new Mock<IHttpClientFactory>();
        _mockConfiguration = new Mock<IConfiguration>();
        _mockLogger = new Mock<ILogger<AuthService>>();
        _mockWebAuthenticatorService = new Mock<IWebAuthenticatorService>();
        _mockBillingService = new Mock<IBillingService>();
        _mockMusicService = new Mock<IMusicService>();
        _mockConfiguration.Setup(c => c["MobileExternalAuth:CallbackUrl"]).Returns("streamtunes://auth");

        _authService = new AuthService(
            _mockHttpClientFactory.Object,
            _mockConfiguration.Object,
            _mockLogger.Object,
            _mockWebAuthenticatorService.Object,
            _mockBillingService.Object,
            _mockMusicService.Object);
    }

    [Test]
    public async Task TryRestoreBillingAsync_WhenNoPurchaseFound_DoesNotVerify()
    {
        _mockBillingService.Setup(b => b.RestorePurchaseAsync())
            .ReturnsAsync((BillingPurchaseResult?)null);

        await _authService.TryRestoreBillingAsync();

        _mockMusicService.Verify(m => m.VerifySubscriptionPurchaseAsync(It.IsAny<BillingPurchaseVerificationRequest>()), Times.Never);
    }

    [Test]
    public async Task TryRestoreBillingAsync_WhenRestoreReturnsFailed_DoesNotVerify()
    {
        _mockBillingService.Setup(b => b.RestorePurchaseAsync())
            .ReturnsAsync(BillingPurchaseResult.Failed("No purchases found"));

        await _authService.TryRestoreBillingAsync();

        _mockMusicService.Verify(m => m.VerifySubscriptionPurchaseAsync(It.IsAny<BillingPurchaseVerificationRequest>()), Times.Never);
    }

    [Test]
    public async Task TryRestoreBillingAsync_WhenPurchaseFound_VerifiesWithServer()
    {
        var purchaseToken = "test-purchase-token";
        var orderId = "GPA.1234-5678";

        _mockBillingService.Setup(b => b.RestorePurchaseAsync())
            .ReturnsAsync(BillingPurchaseResult.Succeeded(purchaseToken, orderId));
        _mockMusicService.Setup(m => m.VerifySubscriptionPurchaseAsync(It.Is<BillingPurchaseVerificationRequest>(r =>
                r.Provider == BillingProviders.GooglePlay &&
                r.PurchaseToken == purchaseToken &&
                r.OrderId == orderId)))
            .ReturnsAsync((true, string.Empty));

        // Mock HttpClient for RefreshUserStatusAsync
        SetupMockSubscriptionStatusResponse(hasSubscription: true, billingSource: "GooglePlay");

        await _authService.TryRestoreBillingAsync();

        _mockMusicService.Verify(m => m.VerifySubscriptionPurchaseAsync(It.Is<BillingPurchaseVerificationRequest>(r =>
            r.Provider == BillingProviders.GooglePlay &&
            r.PurchaseToken == purchaseToken &&
            r.OrderId == orderId)), Times.Once);
    }

    [Test]
    public async Task TryRestoreBillingAsync_WhenVerificationSucceeds_RefreshesSubscriptionStatus()
    {
        _mockBillingService.Setup(b => b.RestorePurchaseAsync())
            .ReturnsAsync(BillingPurchaseResult.Succeeded("token", "order"));
        _mockMusicService.Setup(m => m.VerifySubscriptionPurchaseAsync(It.Is<BillingPurchaseVerificationRequest>(r =>
                r.Provider == BillingProviders.GooglePlay &&
                r.PurchaseToken == "token" &&
                r.OrderId == "order")))
            .ReturnsAsync((true, string.Empty));

        var endDate = DateTime.UtcNow.AddDays(10);
        SetupMockSubscriptionStatusResponse(hasSubscription: true, billingSource: "GooglePlay", status: "CANCELLED", endDate: endDate);

        await _authService.TryRestoreBillingAsync();

        Assert.That(_authService.HasActiveSubscription, Is.True);
        Assert.That(_authService.SubscriptionStatus, Is.EqualTo("CANCELLED"));
        Assert.That(_authService.SubscriptionEndDate, Is.EqualTo(endDate));
    }

    [Test]
    public async Task RefreshUserStatusAsync_MapsTrialFields()
    {
        var trialEnd = DateTime.UtcNow.AddDays(3);
        SetupMockSubscriptionStatusResponse(
            hasSubscription: true,
            billingSource: "GooglePlay",
            status: "ACTIVE",
            endDate: trialEnd,
            isOnTrial: true,
            trialEndDate: trialEnd);

        await _authService.RefreshUserStatusAsync();

        Assert.Multiple(() =>
        {
            Assert.That(_authService.HasActiveSubscription, Is.True);
            Assert.That(_authService.IsOnTrial, Is.True);
            Assert.That(_authService.TrialEndDate, Is.EqualTo(trialEnd));
        });
    }

    [Test]
    public async Task RefreshUserStatusAsync_WhenSubscriptionStateChanges_RaisesAuthStateChanged()
    {
        SetBackingField(nameof(AuthService.HasActiveSubscription), true);
        SetBackingField(nameof(AuthService.SubscriptionStatus), "ACTIVE");
        SetBackingField(nameof(AuthService.BillingSource), "GooglePlay");
        var eventCount = 0;
        _authService.AuthStateChanged += () => eventCount++;
        SetupMockSubscriptionStatusResponse(hasSubscription: false, billingSource: "GooglePlay", status: "EXPIRED");

        await _authService.RefreshUserStatusAsync();

        Assert.That(eventCount, Is.EqualTo(1));
    }

    [Test]
    public async Task RefreshUserStatusAsync_WhenSubscriptionStateUnchanged_DoesNotRaiseAuthStateChanged()
    {
        var endDate = DateTime.UtcNow.AddDays(2);
        SetBackingField(nameof(AuthService.HasActiveSubscription), true);
        SetBackingField(nameof(AuthService.SubscriptionStatus), "ACTIVE");
        SetBackingField(nameof(AuthService.SubscriptionEndDate), endDate);
        SetBackingField(nameof(AuthService.BillingSource), "GooglePlay");
        var eventCount = 0;
        _authService.AuthStateChanged += () => eventCount++;
        SetupMockSubscriptionStatusResponse(hasSubscription: true, billingSource: "GooglePlay", status: "ACTIVE", endDate: endDate);

        await _authService.RefreshUserStatusAsync();

        Assert.That(eventCount, Is.EqualTo(0));
    }

    [Test]
    public void ApplyLoginResponse_StoresSubscriptionHistoryFields()
    {
        var endDate = DateTime.UtcNow.AddDays(-1);
        var trialEndDate = DateTime.UtcNow.AddDays(-2);
        var response = new LoginResponseDto
        {
            Token = "token",
            UserId = 42,
            Email = "user@test.com",
            Roles = [Roles.User],
            EmailConfirmed = true,
            HasActiveSubscription = false,
            SubscriptionStatus = SubscriptionStatuses.Expired,
            SubscriptionEndDate = endDate,
            IsOnTrial = false,
            TrialEndDate = trialEndDate,
            BillingSource = BillingSources.GooglePlay
        };

        _authService.ApplyLoginResponse(response);

        Assert.Multiple(() =>
        {
            Assert.That(_authService.IsLoggedIn, Is.True);
            Assert.That(_authService.HasActiveSubscription, Is.False);
            Assert.That(_authService.SubscriptionStatus, Is.EqualTo(SubscriptionStatuses.Expired));
            Assert.That(_authService.SubscriptionEndDate, Is.EqualTo(endDate));
            Assert.That(_authService.TrialEndDate, Is.EqualTo(trialEndDate));
            Assert.That(_authService.BillingSource, Is.EqualTo(BillingSources.GooglePlay));
        });
    }

    [Test]
    public void ApplyLoginResponse_WhenActiveWithoutStatus_UsesActiveStatusFallback()
    {
        var response = new LoginResponseDto
        {
            Token = "token",
            UserId = 42,
            Email = "user@test.com",
            Roles = [Roles.User],
            EmailConfirmed = true,
            HasActiveSubscription = true
        };

        _authService.ApplyLoginResponse(response);

        Assert.That(_authService.SubscriptionStatus, Is.EqualTo(SubscriptionStatuses.Active));
    }

    [Test]
    public async Task TryRestoreBillingAsync_WhenVerificationFails_DoesNotRefreshStatus()
    {
        _mockBillingService.Setup(b => b.RestorePurchaseAsync())
            .ReturnsAsync(BillingPurchaseResult.Succeeded("token", "order"));
        _mockMusicService.Setup(m => m.VerifySubscriptionPurchaseAsync(It.Is<BillingPurchaseVerificationRequest>(r =>
                r.Provider == BillingProviders.GooglePlay &&
                r.PurchaseToken == "token" &&
                r.OrderId == "order")))
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
        _mockMusicService.Setup(m => m.VerifySubscriptionPurchaseAsync(It.IsAny<BillingPurchaseVerificationRequest>()))
            .ThrowsAsync(new HttpRequestException("Network error"));

        Assert.DoesNotThrowAsync(() => _authService.TryRestoreBillingAsync());
    }

    [Test]
    public async Task LogoutAsync_ClearsPendingStreamRecords()
    {
        try
        {
            await _authService.LogoutAsync();
        }
        catch (Exception ex) when (ex.GetType().Name == "NotImplementedInReferenceAssemblyException")
        {
        }

        _mockMusicService.Verify(m => m.ClearPendingStreamRecordsAsync(), Times.Once);
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

    [Test]
    public async Task AuthenticateWithGoogleAsync_ReturnsPendingRegistration_WhenCallbackRequiresRegistration()
    {
        var httpClient = new HttpClient(new Mock<HttpMessageHandler>().Object)
        {
            BaseAddress = new Uri("https://test.example.com/")
        };

        _mockHttpClientFactory.Setup(f => f.CreateClient("MusicSalesApi")).Returns(httpClient);
        _mockWebAuthenticatorService.Setup(w => w.AuthenticateAsync(
                It.IsAny<Uri>(),
                It.IsAny<Uri>()))
            .ReturnsAsync(new WebAuthenticatorResult(new Dictionary<string, string>
            {
                ["pendingRegistrationToken"] = "pending-token",
                ["email"] = "new-google@test.com"
            }));

        var result = await _authService.AuthenticateWithGoogleAsync();

        Assert.That(result.Success, Is.False);
        Assert.That(result.RequiresRegistration, Is.True);
        Assert.That(result.PendingRegistrationToken, Is.EqualTo("pending-token"));
        Assert.That(result.Email, Is.EqualTo("new-google@test.com"));
    }

    [Test]
    public async Task AuthenticateWithGoogleAsync_ReturnsError_WhenCallbackContainsError()
    {
        var httpClient = new HttpClient(new Mock<HttpMessageHandler>().Object)
        {
            BaseAddress = new Uri("https://test.example.com/")
        };

        _mockHttpClientFactory.Setup(f => f.CreateClient("MusicSalesApi")).Returns(httpClient);
        _mockWebAuthenticatorService.Setup(w => w.AuthenticateAsync(
                It.IsAny<Uri>(),
                It.IsAny<Uri>()))
            .ReturnsAsync(new WebAuthenticatorResult(new Dictionary<string, string>
            {
                ["error"] = "Google sign-in was cancelled."
            }));

        var result = await _authService.AuthenticateWithGoogleAsync();

        Assert.That(result.Success, Is.False);
        Assert.That(result.RequiresRegistration, Is.False);
        Assert.That(result.ErrorMessage, Is.EqualTo("Google sign-in was cancelled."));
    }

    [Test]
    public async Task CompleteGoogleRegistrationAsync_ReturnsServerMessage_OnServerError()
    {
        var messageHandler = new Mock<HttpMessageHandler>();
        messageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("Policies must be accepted.", Encoding.UTF8, "text/plain")
            });

        var httpClient = new HttpClient(messageHandler.Object)
        {
            BaseAddress = new Uri("https://test.example.com/")
        };

        _mockHttpClientFactory.Setup(f => f.CreateClient("MusicSalesApi")).Returns(httpClient);

        var (success, error) = await _authService.CompleteGoogleRegistrationAsync("pending-token", true, true, true);

        Assert.That(success, Is.False);
        Assert.That(error, Does.Contain("Policies must be accepted."));
    }

    [Test]
    public void IsValidatedUser_WhenLoggedInConfirmedAndUserRole_ReturnsTrue()
    {
        SetBackingField(nameof(AuthService.IsLoggedIn), true);
        SetBackingField(nameof(AuthService.EmailConfirmed), true);
        SetBackingField(nameof(AuthService.Roles), new List<string> { "User" });

        Assert.That(_authService.IsValidatedUser, Is.True);
    }

    [Test]
    public void IsValidatedUser_WhenUserRoleMissing_ReturnsFalse()
    {
        SetBackingField(nameof(AuthService.IsLoggedIn), true);
        SetBackingField(nameof(AuthService.EmailConfirmed), true);
        SetBackingField(nameof(AuthService.Roles), new List<string> { "NonValidatedUser" });

        Assert.That(_authService.IsValidatedUser, Is.False);
    }

    private void SetBackingField(string propertyName, object value)
    {
        var field = typeof(AuthService).GetField($"<{propertyName}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        field!.SetValue(_authService, value);
    }

    private void SetupMockSubscriptionStatusResponse(
        bool hasSubscription,
        string? billingSource,
        string? status = null,
        DateTime? endDate = null,
        bool isOnTrial = false,
        DateTime? trialEndDate = null)
    {
        var messageHandler = new Mock<HttpMessageHandler>();
        messageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    HasSubscription = hasSubscription,
                    BillingSource = billingSource,
                    Status = status,
                    EndDate = endDate,
                    IsOnTrial = isOnTrial,
                    TrialEndDate = trialEndDate
                })
            });

        var httpClient = new HttpClient(messageHandler.Object)
        {
            BaseAddress = new Uri("https://test.example.com/")
        };

        _mockHttpClientFactory.Setup(f => f.CreateClient("MusicSalesApi")).Returns(httpClient);
    }
}
