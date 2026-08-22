using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using MusicSalesApp.Common.Helpers;
using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.Tests.Services;

/// <summary>
/// The credential-lifetime rules asserted over the *contents* of secure storage rather than over
/// calls to a mock, using <see cref="InMemorySecureStorage"/>.
///
/// <see cref="AuthServiceTests"/> already covers these paths with <c>Verify(Remove(key))</c>, which
/// answers "was the instruction issued". These answer "what is left afterwards" — the question a
/// mock cannot, because it neither honours a removal nor notices a later write putting the value
/// back. That gap is where the cross-account credential bugs lived: a `SetAsync` that a loose mock
/// happily accepts leaves a real store holding one account's email beside another's password.
///
/// This is the closest a unit test gets to the on-device check, which diffed the encrypted
/// shared_prefs entries before and after each operation.
/// </summary>
[TestFixture]
public class AuthServiceCredentialLifetimeTests
{
    private const string TokenKey = "auth_token";
    private const string UserIdKey = "auth_user_id";
    private const string EmailKey = "auth_email";
    private const string EmailConfirmedKey = "auth_email_confirmed";
    private const string IsCreatorKey = "auth_is_creator";
    private const string SubscriptionCacheKey = "auth_subscription_status";
    private const string BioEmailKey = "bio_email";
    private const string BioPasswordKey = "bio_password";

    private const string OwnerEmail = "owner@test.com";
    private const string OwnerPassword = "owner-password";

    private InMemorySecureStorage _storage;
    private Mock<IHttpClientFactory> _mockHttpClientFactory;
    private Mock<IMusicService> _mockMusicService;
    private AuthService _authService;

    [SetUp]
    public void Setup()
    {
        _storage = new InMemorySecureStorage();
        _mockHttpClientFactory = new Mock<IHttpClientFactory>();
        _mockMusicService = new Mock<IMusicService>();

        var configuration = new Mock<IConfiguration>();
        configuration.Setup(c => c["MobileExternalAuth:CallbackUrl"]).Returns("streamtunes://auth");

        _authService = new AuthService(
            _mockHttpClientFactory.Object,
            configuration.Object,
            new Mock<ILogger<AuthService>>().Object,
            new Mock<IWebAuthenticatorService>().Object,
            new Mock<IBillingService>().Object,
            _mockMusicService.Object,
            _storage,
            new Mock<IBiometricAuthenticator>().Object,
            new Mock<IOfflinePlaylistStore>().Object,
            new Mock<IOfflineSongCatalogStore>().Object);
    }

    /// <summary>Biometric credentials belonging to the device's owner, saved by an earlier session.</summary>
    private void GiveTheDeviceOwnerSavedCredentials()
    {
        _storage.Seed(BioEmailKey, OwnerEmail);
        _storage.Seed(BioPasswordKey, OwnerPassword);
    }

    private void AssertOwnerCredentialsIntact()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_storage.Peek(BioEmailKey), Is.EqualTo(OwnerEmail));
            Assert.That(_storage.Peek(BioPasswordKey), Is.EqualTo(OwnerPassword));
        });
    }

    // --- Logout keeps the pair; everything session-shaped goes ---

    [Test]
    public async Task LogoutAsync_LeavesExactlyTheBiometricPairBehind()
    {
        GiveTheDeviceOwnerSavedCredentials();
        SetupLoginResponse();
        await _authService.LoginAsync(OwnerEmail, OwnerPassword);

        await _authService.LogoutAsync();

        // The device check counted 4 entries after logout: the two keysets plus this pair.
        Assert.That(_storage.Keys, Is.EquivalentTo(new[] { BioEmailKey, BioPasswordKey }));
    }

    [Test]
    public async Task DeleteAccountAsync_LeavesNothingBehind()
    {
        GiveTheDeviceOwnerSavedCredentials();
        SetupLoginResponse();
        await _authService.LoginAsync(OwnerEmail, OwnerPassword);
        SetupResponse(HttpStatusCode.OK);

        await _authService.DeleteAccountAsync();

        Assert.That(_storage.Keys, Is.Empty);
    }

    // --- A reset belongs to one account, and only that account's credentials answer for it ---

    [Test]
    public async Task ResetPasswordAsync_ForABystander_LeavesTheOwnersPasswordUsable()
    {
        GiveTheDeviceOwnerSavedCredentials();
        SetupResponse(HttpStatusCode.OK);

        await _authService.ResetPasswordAsync(99, "123456", "NewPassword1!", "bystander@test.com");

        AssertOwnerCredentialsIntact();
    }

    [Test]
    public async Task ResetPasswordAsync_ForTheOwner_LeavesNoStaleCredential()
    {
        GiveTheDeviceOwnerSavedCredentials();
        SetupResponse(HttpStatusCode.OK);

        await _authService.ResetPasswordAsync(1, "123456", "NewPassword1!", OwnerEmail);

        Assert.Multiple(() =>
        {
            Assert.That(_storage.Contains(BioEmailKey), Is.False);
            Assert.That(_storage.Contains(BioPasswordKey), Is.False);
        });
    }

    [Test]
    public async Task ResetPasswordAsync_MatchesTheOwnerCaseInsensitively()
    {
        // The address is round-tripped through a form field and the server; casing is not stable.
        GiveTheDeviceOwnerSavedCredentials();
        SetupResponse(HttpStatusCode.OK);

        await _authService.ResetPasswordAsync(1, "123456", "NewPassword1!", OwnerEmail.ToUpperInvariant());

        Assert.That(_storage.Contains(BioPasswordKey), Is.False);
    }

    // --- An email change must never pair one account's address with another's password ---

    [Test]
    public async Task ChangeEmailAsync_ByADifferentAccount_LeavesTheStoredPairCoherent()
    {
        // The exact shape of the bug: the owner's credentials survive a logout, someone else signs in
        // and changes their own email. Rewriting bio_email here would leave the owner's password
        // filed under the other person's address — a credential that clears the fingerprint prompt
        // and is rejected by the server every time, unrecoverable except by turning the feature off.
        GiveTheDeviceOwnerSavedCredentials();
        SetupLoginResponse(email: "bystander@test.com");
        await _authService.LoginAsync("bystander@test.com", "their-password");
        SetupResponse(HttpStatusCode.OK);

        await _authService.ChangeEmailAsync(42, "bystander-new@test.com");

        AssertOwnerCredentialsIntact();
    }

    [Test]
    public async Task ChangeEmailAsync_ByTheOwner_MovesTheAddressAndKeepsThePassword()
    {
        GiveTheDeviceOwnerSavedCredentials();
        SetupLoginResponse(email: OwnerEmail);
        await _authService.LoginAsync(OwnerEmail, OwnerPassword);
        SetupResponse(HttpStatusCode.OK);

        await _authService.ChangeEmailAsync(42, "owner-new@test.com");

        Assert.Multiple(() =>
        {
            Assert.That(_storage.Peek(BioEmailKey), Is.EqualTo("owner-new@test.com"));
            Assert.That(_storage.Peek(BioPasswordKey), Is.EqualTo(OwnerPassword));
        });
    }

    [Test]
    public async Task ChangeEmailAsync_WhenTheServerRejectsIt_ChangesNothing()
    {
        GiveTheDeviceOwnerSavedCredentials();
        SetupLoginResponse(email: OwnerEmail);
        await _authService.LoginAsync(OwnerEmail, OwnerPassword);
        SetupResponse(HttpStatusCode.BadRequest);

        await _authService.ChangeEmailAsync(42, "owner-new@test.com");

        AssertOwnerCredentialsIntact();
    }

    // --- A refused removal must not be reported as a completed one ---

    [Test]
    public async Task DisableBiometricLoginAsync_WhenTheKeystoreRefuses_DoesNotReportTheCredentialsAsGone()
    {
        GiveTheDeviceOwnerSavedCredentials();
        _storage.FailingKeys.Add(BioEmailKey);

        await _authService.DisableBiometricLoginAsync();

        Assert.That(_storage.Contains(BioEmailKey), Is.True, "arrange: the removal should have been refused");

        // The cached answer must not claim otherwise. Marking the state unknown forces the next read
        // back to storage; marking it "gone" would have Account Settings show a switch reading "off"
        // over a credential still sitting in the keystore.
        _storage.FailingKeys.Clear();
        Assert.That(await _authService.HasBiometricCredentialsAsync(), Is.True);
    }

    // --- The session snapshot is not namespaced by account, so it must not outlive the session ---

    [Test]
    public async Task LogoutAsync_RemovesEverySessionScopedKey()
    {
        GiveTheDeviceOwnerSavedCredentials();
        SetupLoginResponse();
        await _authService.LoginAsync(OwnerEmail, OwnerPassword);
        Assume.That(_storage.Contains(TokenKey), Is.True, "arrange: the login should have stored a session");

        await _authService.LogoutAsync();

        Assert.Multiple(() =>
        {
            foreach (var key in new[]
                     { TokenKey, UserIdKey, EmailKey, EmailConfirmedKey, IsCreatorKey, SubscriptionCacheKey })
            {
                Assert.That(_storage.Contains(key), Is.False, $"{key} survived logout");
            }
        });
    }

    // --- Helpers ---

    private static string CreateJwt(int userId, string email)
    {
        var handler = new JwtSecurityTokenHandler();
        return handler.WriteToken(handler.CreateJwtSecurityToken(
            issuer: "MusicSalesApp",
            audience: "MusicSalesApp.Maui",
            subject: new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Email, email),
                new Claim(ClaimTypes.Role, Roles.User)
            ]),
            expires: DateTime.UtcNow.AddDays(1)));
    }

    private void SetupLoginResponse(string email = OwnerEmail)
        => SetupResponse(HttpStatusCode.OK, new
        {
            Token = CreateJwt(42, email),
            UserId = 42,
            Email = email,
            Roles = new[] { Roles.User },
            EmailConfirmed = true,
            HasActiveSubscription = false,
            IsCreator = false,
            CreatorId = (int?)null
        });

    private void SetupResponse(HttpStatusCode statusCode, object? jsonBody = null)
    {
        var messageHandler = new Mock<HttpMessageHandler>();
        messageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => new HttpResponseMessage(statusCode)
            {
                Content = jsonBody is null
                    ? new StringContent(string.Empty, Encoding.UTF8, "application/json")
                    : JsonContent.Create(jsonBody)
            });

        _mockHttpClientFactory.Setup(f => f.CreateClient("MusicSalesApi")).Returns(
            new HttpClient(messageHandler.Object) { BaseAddress = new Uri("https://test.example.com/") });
    }
}
