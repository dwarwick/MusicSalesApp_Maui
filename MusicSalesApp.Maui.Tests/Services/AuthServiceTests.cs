using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Maui.Authentication;
using Microsoft.Maui.Storage;
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
    private Mock<ISecureStorage> _mockSecureStorage;
    private Mock<IOfflinePlaylistStore> _mockOfflinePlaylistStore;
    private Mock<IOfflineSongCatalogStore> _mockOfflineSongCatalogStore;
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
        _mockSecureStorage = new Mock<ISecureStorage>();
        _mockSecureStorage.Setup(storage => storage.GetAsync(It.IsAny<string>())).ReturnsAsync((string?)null);
        _mockSecureStorage.Setup(storage => storage.SetAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        _mockSecureStorage.Setup(storage => storage.Remove(It.IsAny<string>())).Returns(true);
        _mockConfiguration.Setup(c => c["MobileExternalAuth:CallbackUrl"]).Returns("streamtunes://auth");

        _mockOfflinePlaylistStore = new Mock<IOfflinePlaylistStore>();
        _mockOfflineSongCatalogStore = new Mock<IOfflineSongCatalogStore>();

        _authService = new AuthService(
            _mockHttpClientFactory.Object,
            _mockConfiguration.Object,
            _mockLogger.Object,
            _mockWebAuthenticatorService.Object,
            _mockBillingService.Object,
            _mockMusicService.Object,
            _mockSecureStorage.Object,
            _mockOfflinePlaylistStore.Object,
            _mockOfflineSongCatalogStore.Object);
    }

    // --- Logout clears the offline snapshots ---

    [Test]
    public async Task LogoutAsync_ClearsTheOfflinePlaylistSnapshot()
    {
        // Neither snapshot is namespaced by account, so leaving it would show the outgoing user's
        // playlists to whoever logs in next while offline.
        await _authService.LogoutAsync();

        _mockOfflinePlaylistStore.Verify(store => store.ClearAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task LogoutAsync_StripsTheUserVotesFromTheOfflineSongCatalog()
    {
        // The catalog entries carry the user's own thumbs-up/down state.
        await _authService.LogoutAsync();

        _mockOfflineSongCatalogStore.Verify(
            store => store.ClearUserLikeStatesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task LogoutAsync_KeepsTheOfflineSongCatalogItself()
    {
        // Deleting it would take offline playback away as well - and this path also runs on the
        // session-expiry logout that can fire at startup with no network to reload from.
        await _authService.LogoutAsync();

        _mockOfflineSongCatalogStore.Verify(store => store.ClearAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public void LogoutAsync_WhenClearingTheSnapshotsFails_StillCompletes()
    {
        _mockOfflinePlaylistStore.Setup(store => store.ClearAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("file locked"));

        Assert.That(async () => await _authService.LogoutAsync(), Throws.Nothing);
    }

    [Test]
    public async Task LogoutAsync_WithoutTheOfflineStores_StillClearsTheSession()
    {
        // Trailing-optional injection: a call site that omits them must keep working.
        var authService = new AuthService(
            _mockHttpClientFactory.Object,
            _mockConfiguration.Object,
            _mockLogger.Object,
            _mockWebAuthenticatorService.Object,
            _mockBillingService.Object,
            _mockMusicService.Object,
            _mockSecureStorage.Object);

        await authService.LogoutAsync();

        Assert.That(authService.IsLoggedIn, Is.False);
    }

    [Test]
    public async Task HasBiometricCredentialsAsync_WhenBothValuesExist_ReturnsTrueAndCachesResult()
    {
        _mockSecureStorage.Setup(storage => storage.GetAsync("bio_email")).ReturnsAsync("user@test.com");
        _mockSecureStorage.Setup(storage => storage.GetAsync("bio_password")).ReturnsAsync("secret");

        var firstResult = await _authService.HasBiometricCredentialsAsync();
        var cachedResult = await _authService.HasBiometricCredentialsAsync();

        Assert.Multiple(() =>
        {
            Assert.That(firstResult, Is.True);
            Assert.That(cachedResult, Is.True);
        });
        _mockSecureStorage.Verify(storage => storage.GetAsync("bio_email"), Times.Once);
        _mockSecureStorage.Verify(storage => storage.GetAsync("bio_password"), Times.Once);
    }

    [Test]
    public async Task HasBiometricCredentialsAsync_WhenAValueIsMissing_ReturnsFalse()
    {
        _mockSecureStorage.Setup(storage => storage.GetAsync("bio_email")).ReturnsAsync("user@test.com");
        _mockSecureStorage.Setup(storage => storage.GetAsync("bio_password")).ReturnsAsync((string?)null);

        var result = await _authService.HasBiometricCredentialsAsync();

        Assert.That(result, Is.False);
    }

    [Test]
    public async Task HasBiometricCredentialsAsync_WhenSecureStorageFails_ReturnsFalseWithoutCachingFailure()
    {
        _mockSecureStorage.SetupSequence(storage => storage.GetAsync("bio_email"))
            .ThrowsAsync(new InvalidOperationException("Secure storage unavailable"))
            .ReturnsAsync("user@test.com");
        _mockSecureStorage.Setup(storage => storage.GetAsync("bio_password")).ReturnsAsync("secret");

        var failedResult = await _authService.HasBiometricCredentialsAsync();
        var recoveredResult = await _authService.HasBiometricCredentialsAsync();

        Assert.Multiple(() =>
        {
            Assert.That(failedResult, Is.False);
            Assert.That(recoveredResult, Is.True);
        });
    }

    [Test]
    public async Task EnableAndDisableBiometricLogin_UpdateCachedState()
    {
        await _authService.EnableBiometricLoginAsync("user@test.com", "secret");
        var enabled = await _authService.HasBiometricCredentialsAsync();

        await _authService.DisableBiometricLoginAsync();
        var disabled = await _authService.HasBiometricCredentialsAsync();

        Assert.Multiple(() =>
        {
            Assert.That(enabled, Is.True);
            Assert.That(disabled, Is.False);
        });
        _mockSecureStorage.Verify(storage => storage.GetAsync(It.IsAny<string>()), Times.Never);
        _mockSecureStorage.Verify(storage => storage.Remove("bio_email"), Times.Once);
        _mockSecureStorage.Verify(storage => storage.Remove("bio_password"), Times.Once);
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

    // --- An unreachable store leaves the restore outstanding, so it is retried ---

    [Test]
    public async Task RefreshUserStatusAsync_AfterTheStoreCouldNotBeReached_AsksItAgain()
    {
        // "Could not ask the store" says nothing about what the user owns. Accepting it as
        // "owns nothing" would leave a subscriber looking unsubscribed until the next app launch.
        _mockBillingService.Setup(b => b.RestorePurchaseAsync())
            .ReturnsAsync(BillingPurchaseResult.Unavailable("Could not connect to Google Play Billing."));
        SetupMockSubscriptionStatusResponse(hasSubscription: false, billingSource: null);

        await _authService.TryRestoreBillingAsync();
        await _authService.RefreshUserStatusAsync();

        _mockBillingService.Verify(b => b.RestorePurchaseAsync(), Times.Exactly(2));
    }

    [Test]
    public async Task RefreshUserStatusAsync_WhenTheStoreSaidNothingIsOwned_DoesNotAskAgain()
    {
        // The store answered. That answer is final, so re-asking on every status refresh would be
        // pure noise.
        _mockBillingService.Setup(b => b.RestorePurchaseAsync())
            .ReturnsAsync((BillingPurchaseResult?)null);
        SetupMockSubscriptionStatusResponse(hasSubscription: false, billingSource: null);

        await _authService.TryRestoreBillingAsync();
        await _authService.RefreshUserStatusAsync();

        _mockBillingService.Verify(b => b.RestorePurchaseAsync(), Times.Once);
    }

    [Test]
    public async Task RefreshUserStatusAsync_WhenTheServerNowReportsASubscription_DoesNotAskAgain()
    {
        // The server is authoritative: if it reports a subscription there is nothing left to repair.
        _mockBillingService.Setup(b => b.RestorePurchaseAsync())
            .ReturnsAsync(BillingPurchaseResult.Unavailable("Could not connect to Google Play Billing."));

        await _authService.TryRestoreBillingAsync();

        SetupMockSubscriptionStatusResponse(hasSubscription: true, billingSource: "GooglePlay");
        await _authService.RefreshUserStatusAsync();

        _mockBillingService.Verify(b => b.RestorePurchaseAsync(), Times.Once);
    }

    [Test]
    public async Task RefreshUserStatusAsync_WhenTheRetryReachesTheStore_VerifiesThePurchase()
    {
        // The whole point of retrying: a purchase the store knew about all along reaches the
        // server on this launch instead of the next one.
        _mockBillingService.SetupSequence(b => b.RestorePurchaseAsync())
            .ReturnsAsync(BillingPurchaseResult.Unavailable("Could not connect to Google Play Billing."))
            .ReturnsAsync(BillingPurchaseResult.Succeeded("token", "order"));
        _mockMusicService.Setup(m => m.VerifySubscriptionPurchaseAsync(It.IsAny<BillingPurchaseVerificationRequest>()))
            .ReturnsAsync((true, string.Empty));
        SetupMockSubscriptionStatusResponse(hasSubscription: false, billingSource: null);

        await _authService.TryRestoreBillingAsync();
        await _authService.RefreshUserStatusAsync();

        Assert.Multiple(() =>
        {
            _mockMusicService.Verify(
                m => m.VerifySubscriptionPurchaseAsync(It.Is<BillingPurchaseVerificationRequest>(r => r.PurchaseToken == "token")),
                Times.Once);
            // The successful restore refreshes status again; that must not start another retry.
            _mockBillingService.Verify(b => b.RestorePurchaseAsync(), Times.Exactly(2));
        });
    }

    [Test]
    public async Task LogoutAsync_DiscardsARestoreOwedToTheSignedOutUser()
    {
        // Retrying it after a different account signs in would attach the outgoing user's purchase
        // to the incoming one.
        _mockBillingService.Setup(b => b.RestorePurchaseAsync())
            .ReturnsAsync(BillingPurchaseResult.Unavailable("Could not connect to Google Play Billing."));
        SetupMockSubscriptionStatusResponse(hasSubscription: false, billingSource: null);

        await _authService.TryRestoreBillingAsync();
        await _authService.LogoutAsync();
        await _authService.RefreshUserStatusAsync();

        _mockBillingService.Verify(b => b.RestorePurchaseAsync(), Times.Once);
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

    [Test]
    public void IsAdmin_WhenLoggedInWithAdminRole_ReturnsTrue()
    {
        SetBackingField(nameof(AuthService.IsLoggedIn), true);
        SetBackingField(nameof(AuthService.Roles), new List<string> { Roles.User, Roles.Admin });

        Assert.That(_authService.IsAdmin, Is.True);
    }

    [Test]
    public void IsAdmin_WhenAdminRoleMissing_ReturnsFalse()
    {
        SetBackingField(nameof(AuthService.IsLoggedIn), true);
        SetBackingField(nameof(AuthService.Roles), new List<string> { Roles.User });

        Assert.That(_authService.IsAdmin, Is.False);
    }

    [Test]
    public void IsAdmin_WhenNotLoggedIn_ReturnsFalse()
    {
        SetBackingField(nameof(AuthService.Roles), new List<string> { Roles.Admin });

        Assert.That(_authService.IsAdmin, Is.False);
    }

    [Test]
    public async Task TryRestoreSessionAsync_RestoresCreatorStatusFromSecureStorage()
    {
        _mockSecureStorage.Setup(storage => storage.GetAsync("auth_token")).ReturnsAsync(CreateUnexpiredJwt(userId: 42));
        _mockSecureStorage.Setup(storage => storage.GetAsync("auth_is_creator")).ReturnsAsync(bool.TrueString);
        _mockSecureStorage.Setup(storage => storage.GetAsync("auth_creator_id")).ReturnsAsync("7");
        // The status refresh must not be what satisfies this assertion, or the test would pass even
        // if the secure-storage restore were deleted outright.
        SetupUnreachableServer();

        await _authService.TryRestoreSessionAsync();

        Assert.Multiple(() =>
        {
            Assert.That(_authService.IsLoggedIn, Is.True);
            Assert.That(_authService.IsCreator, Is.True);
            Assert.That(_authService.CreatorId, Is.EqualTo(7));
        });
    }

    [Test]
    public async Task TryRestoreSessionAsync_RestoredCreatorHearsOwnSongInFull()
    {
        _mockSecureStorage.Setup(storage => storage.GetAsync("auth_token")).ReturnsAsync(CreateUnexpiredJwt(userId: 42));
        _mockSecureStorage.Setup(storage => storage.GetAsync("auth_is_creator")).ReturnsAsync(bool.TrueString);
        SetupUnreachableServer();

        await _authService.TryRestoreSessionAsync();

        var ownSong = new SongDto
        {
            Id = 1,
            SongTitle = "Test",
            CreatorUserId = 42,
            StreamUrl = "https://test.com/song.mp3"
        };

        Assert.That(PreviewAccessPolicy.ShouldLimitPreview(_authService, ownSong), Is.False);
    }

    [Test]
    public async Task TryRestoreSessionAsync_WhenCreatorStatusWasNeverStored_LeavesCreatorFalse()
    {
        // Sessions stored before creator status was persisted have neither key.
        _mockSecureStorage.Setup(storage => storage.GetAsync("auth_token")).ReturnsAsync(CreateUnexpiredJwt(userId: 42));
        SetupUnreachableServer();

        await _authService.TryRestoreSessionAsync();

        Assert.Multiple(() =>
        {
            Assert.That(_authService.IsLoggedIn, Is.True);
            Assert.That(_authService.IsCreator, Is.False);
            Assert.That(_authService.CreatorId, Is.Null);
        });
    }

    [Test]
    public async Task LoginAsync_PersistsCreatorStatusForTheNextRestore()
    {
        SetupMockLoginResponse(isCreator: true, creatorId: 7);

        await _authService.LoginAsync("creator@test.com", "secret");

        _mockSecureStorage.Verify(storage => storage.SetAsync("auth_is_creator", bool.TrueString), Times.Once);
        _mockSecureStorage.Verify(storage => storage.SetAsync("auth_creator_id", "7"), Times.Once);
    }

    [Test]
    public async Task LoginAsync_WhenNotACreator_ClearsAnyStoredCreatorId()
    {
        SetupMockLoginResponse(isCreator: false, creatorId: null);

        await _authService.LoginAsync("listener@test.com", "secret");

        _mockSecureStorage.Verify(storage => storage.SetAsync("auth_is_creator", bool.FalseString), Times.Once);
        _mockSecureStorage.Verify(storage => storage.Remove("auth_creator_id"), Times.Once);
    }

    [Test]
    public async Task LogoutAsync_RemovesStoredCreatorStatus()
    {
        await _authService.LogoutAsync();

        _mockSecureStorage.Verify(storage => storage.Remove("auth_is_creator"), Times.Once);
        _mockSecureStorage.Verify(storage => storage.Remove("auth_creator_id"), Times.Once);
    }

    [Test]
    public async Task RefreshUserStatusAsync_WhenServerRevokesCreatorStatus_ClearsCachedStatus()
    {
        // Without this the cached flag would survive until the JWT expired (7 days), letting a
        // deactivated creator keep unlimited playback of their own songs.
        SetBackingField(nameof(AuthService.IsCreator), true);
        SetBackingField(nameof(AuthService.CreatorId), 7);
        SetupMockSubscriptionStatusResponse(hasSubscription: false, billingSource: null, isCreator: false);

        await _authService.RefreshUserStatusAsync();

        Assert.Multiple(() =>
        {
            Assert.That(_authService.IsCreator, Is.False);
            Assert.That(_authService.CreatorId, Is.Null);
        });
        _mockSecureStorage.Verify(storage => storage.SetAsync("auth_is_creator", bool.FalseString), Times.Once);
        _mockSecureStorage.Verify(storage => storage.Remove("auth_creator_id"), Times.Once);
    }

    [Test]
    public async Task RefreshUserStatusAsync_WhenServerGrantsCreatorStatus_PersistsItForTheNextRestore()
    {
        SetupMockSubscriptionStatusResponse(
            hasSubscription: false,
            billingSource: null,
            isCreator: true,
            creatorId: 7);

        await _authService.RefreshUserStatusAsync();

        Assert.Multiple(() =>
        {
            Assert.That(_authService.IsCreator, Is.True);
            Assert.That(_authService.CreatorId, Is.EqualTo(7));
        });
        _mockSecureStorage.Verify(storage => storage.SetAsync("auth_is_creator", bool.TrueString), Times.Once);
        _mockSecureStorage.Verify(storage => storage.SetAsync("auth_creator_id", "7"), Times.Once);
    }

    [Test]
    public async Task RefreshUserStatusAsync_WhenCreatorStatusUnchanged_DoesNotRewriteSecureStorage()
    {
        SetBackingField(nameof(AuthService.IsCreator), true);
        SetBackingField(nameof(AuthService.CreatorId), 7);
        SetupMockSubscriptionStatusResponse(
            hasSubscription: false,
            billingSource: null,
            isCreator: true,
            creatorId: 7);

        await _authService.RefreshUserStatusAsync();

        _mockSecureStorage.Verify(
            storage => storage.SetAsync("auth_is_creator", It.IsAny<string>()),
            Times.Never);
        _mockSecureStorage.Verify(
            storage => storage.SetAsync("auth_creator_id", It.IsAny<string>()),
            Times.Never);
    }

    [Test]
    public async Task RefreshUserStatusAsync_WhenCreatorStatusChanges_RaisesAuthStateChanged()
    {
        SetBackingField(nameof(AuthService.IsCreator), true);
        SetBackingField(nameof(AuthService.CreatorId), 7);
        var eventCount = 0;
        _authService.AuthStateChanged += () => eventCount++;
        SetupMockSubscriptionStatusResponse(hasSubscription: false, billingSource: null, isCreator: false);

        await _authService.RefreshUserStatusAsync();

        Assert.That(eventCount, Is.EqualTo(1));
    }

    [Test]
    public async Task RefreshUserStatusAsync_WhenServerUnreachable_KeepsCachedCreatorStatus()
    {
        // Offline is "no data", not "not a creator" - the cached status must survive so creators
        // keep working on a plane.
        SetBackingField(nameof(AuthService.IsCreator), true);
        SetBackingField(nameof(AuthService.CreatorId), 7);
        SetupUnreachableServer();

        await _authService.RefreshUserStatusAsync();

        Assert.Multiple(() =>
        {
            Assert.That(_authService.IsCreator, Is.True);
            Assert.That(_authService.CreatorId, Is.EqualTo(7));
        });
        _mockSecureStorage.Verify(
            storage => storage.SetAsync("auth_is_creator", It.IsAny<string>()),
            Times.Never);
    }

    [Test]
    public async Task RefreshUserStatusAsync_WhenServerOmitsCreatorFields_KeepsCachedCreatorStatus()
    {
        // Version skew: the app can ship before the server does, or the server can be rolled back.
        // A non-nullable bool would deserialize the absent field to false and persist a demotion
        // that survives restarts, costing every creator full-length playback of their own songs.
        SetBackingField(nameof(AuthService.IsCreator), true);
        SetBackingField(nameof(AuthService.CreatorId), 7);
        SetupMockSubscriptionStatusResponse(
            hasSubscription: false,
            billingSource: null,
            includeCreatorFields: false);

        await _authService.RefreshUserStatusAsync();

        Assert.Multiple(() =>
        {
            Assert.That(_authService.IsCreator, Is.True);
            Assert.That(_authService.CreatorId, Is.EqualTo(7));
        });
        _mockSecureStorage.Verify(
            storage => storage.SetAsync("auth_is_creator", It.IsAny<string>()),
            Times.Never);
        _mockSecureStorage.Verify(storage => storage.Remove("auth_creator_id"), Times.Never);
    }

    [Test]
    public async Task TryRestoreSessionAsync_WhenServerHasRevokedCreatorStatus_ClearsRestoredStatus()
    {
        // End-to-end shape of the actual bug: the cached flag is restored on launch, then corrected
        // by the very next status refresh instead of surviving for the JWT's remaining lifetime.
        _mockSecureStorage.Setup(storage => storage.GetAsync("auth_token")).ReturnsAsync(CreateUnexpiredJwt(userId: 42));
        _mockSecureStorage.Setup(storage => storage.GetAsync("auth_is_creator")).ReturnsAsync(bool.TrueString);
        _mockSecureStorage.Setup(storage => storage.GetAsync("auth_creator_id")).ReturnsAsync("7");
        SetupMockSubscriptionStatusResponse(hasSubscription: false, billingSource: null, isCreator: false);

        await _authService.TryRestoreSessionAsync();

        Assert.Multiple(() =>
        {
            Assert.That(_authService.IsLoggedIn, Is.True);
            Assert.That(_authService.IsCreator, Is.False);
            Assert.That(_authService.CreatorId, Is.Null);
        });
        _mockSecureStorage.Verify(storage => storage.SetAsync("auth_is_creator", bool.FalseString), Times.Once);
    }

    [Test]
    public async Task RefreshUserStatusAsync_WhenPersistingCreatorStatusFails_StillNotifiesAuthStateChanged()
    {
        SetBackingField(nameof(AuthService.IsCreator), true);
        SetBackingField(nameof(AuthService.CreatorId), 7);
        _mockSecureStorage
            .Setup(storage => storage.SetAsync("auth_is_creator", It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("keystore unavailable"));
        var eventCount = 0;
        _authService.AuthStateChanged += () => eventCount++;
        SetupMockSubscriptionStatusResponse(hasSubscription: false, billingSource: null, isCreator: false);

        await _authService.RefreshUserStatusAsync();

        Assert.Multiple(() =>
        {
            Assert.That(_authService.IsCreator, Is.False);
            Assert.That(eventCount, Is.EqualTo(1));
        });
    }

    private static string CreateUnexpiredJwt(int userId)
    {
        var handler = new JwtSecurityTokenHandler();
        var token = handler.CreateJwtSecurityToken(
            issuer: "MusicSalesApp",
            audience: "MusicSalesApp.Maui",
            subject: new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Email, "user@test.com"),
                new Claim(ClaimTypes.Role, Roles.User)
            ]),
            expires: DateTime.UtcNow.AddDays(1));

        return handler.WriteToken(token);
    }

    private void SetupMockLoginResponse(bool isCreator, int? creatorId)
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
                    Token = CreateUnexpiredJwt(userId: 42),
                    UserId = 42,
                    Email = "user@test.com",
                    Roles = new[] { Roles.User },
                    EmailConfirmed = true,
                    HasActiveSubscription = true,
                    IsCreator = isCreator,
                    CreatorId = creatorId
                })
            });

        var httpClient = new HttpClient(messageHandler.Object)
        {
            BaseAddress = new Uri("https://test.example.com/")
        };

        _mockHttpClientFactory.Setup(f => f.CreateClient("MusicSalesApi")).Returns(httpClient);
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
        DateTime? trialEndDate = null,
        bool isCreator = false,
        int? creatorId = null,
        bool includeCreatorFields = true)
    {
        object payload = includeCreatorFields
            ? new
            {
                HasSubscription = hasSubscription,
                BillingSource = billingSource,
                Status = status,
                EndDate = endDate,
                IsOnTrial = isOnTrial,
                TrialEndDate = trialEndDate,
                IsCreator = isCreator,
                CreatorId = creatorId
            }
            // Mirrors a server that predates creator status on this endpoint.
            : new
            {
                HasSubscription = hasSubscription,
                BillingSource = billingSource,
                Status = status,
                EndDate = endDate,
                IsOnTrial = isOnTrial,
                TrialEndDate = trialEndDate
            };

        var messageHandler = new Mock<HttpMessageHandler>();
        messageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(payload)
            });

        var httpClient = new HttpClient(messageHandler.Object)
        {
            BaseAddress = new Uri("https://test.example.com/")
        };

        _mockHttpClientFactory.Setup(f => f.CreateClient("MusicSalesApi")).Returns(httpClient);
    }

    /// <summary>
    /// Makes the status endpoint throw, so only state that survived without the network can satisfy
    /// an assertion.
    /// </summary>
    private void SetupUnreachableServer()
    {
        var messageHandler = new Mock<HttpMessageHandler>();
        messageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("offline"));

        _mockHttpClientFactory
            .Setup(factory => factory.CreateClient("MusicSalesApi"))
            .Returns(new HttpClient(messageHandler.Object)
            {
                BaseAddress = new Uri("https://test.example.com/")
            });
    }
}
