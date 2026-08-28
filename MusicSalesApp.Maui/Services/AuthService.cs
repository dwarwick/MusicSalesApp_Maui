using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Authentication;
using Microsoft.Maui.Storage;
using MusicSalesApp.Common.Helpers;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Services;

public class AuthService : IAuthService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthService> _logger;
    private readonly IWebAuthenticatorService _webAuthenticatorService;
    private readonly IBillingService _billingService;
    private readonly IMusicService _musicService;
    private readonly IUserStreamedSongStore? _userStreamedSongStore;
    private readonly ISecureStorage _secureStorage;
    private readonly IBiometricAuthenticator _biometrics;
    private readonly IAppleSignInService _appleSignIn;
    private readonly SemaphoreSlim _biometricStateLock = new(1, 1);
    private int _biometricCredentialsState = -1;

    /// <summary>
    /// Set when a purchase restore could not reach the platform store, so the answer it would have
    /// given is still outstanding. See <see cref="RetryPendingBillingRestoreAsync"/>.
    /// </summary>
    private bool _billingRestorePending;

    /// <summary>
    /// Set by the paths in <see cref="TryRestoreSessionAsync"/> that end a session without the user
    /// asking, and cleared when they sign back in.
    ///
    /// Deliberately not touched by <see cref="ClearState"/>: those paths set it and then call
    /// <see cref="LogoutAsync"/>, so clearing it there would erase the notice on the way out.
    ///
    /// Volatile because the write happens on whatever thread the startup restore resumes on, while
    /// the reads and the login-time clear come from the UI thread.
    /// </summary>
    private SessionExpiryNotice? _pendingSessionExpiryNotice;

    private const string TokenStorageKey = "auth_token";
    private const string EmailStorageKey = "auth_email";
    private const string EmailConfirmedStorageKey = "auth_email_confirmed";
    private const string IsCreatorStorageKey = "auth_is_creator";
    private const string CreatorIdStorageKey = "auth_creator_id";
    private const string SubscriptionStatusStorageKey = "auth_subscription_status";
    private const string BioEmailKey = "bio_email";
    private const string BioPasswordKey = "bio_password";

    private readonly IOfflinePlaylistStore? _offlinePlaylistStore;
    private readonly IOfflineSongCatalogStore? _offlineSongCatalogStore;

    public event Action? AuthStateChanged;

    public bool IsLoggedIn { get; private set; }
    public int? UserId { get; private set; }
    public string? Email { get; private set; }
    public bool EmailConfirmed { get; private set; }
    public bool IsValidatedUser => IsLoggedIn && EmailConfirmed && Roles.Contains("User");
    public bool HasActiveSubscription { get; private set; }
    public string? SubscriptionStatus { get; private set; }
    public DateTime? SubscriptionEndDate { get; private set; }
    public bool IsOnTrial { get; private set; }
    public DateTime? TrialEndDate { get; private set; }
    public string? BillingSource { get; private set; }

    /// <summary>
    /// Whether the entitlement above was confirmed by the server this session, is standing on a
    /// cached snapshot, or could not be established at all. Drives the explanation on the account
    /// screen so an offline subscriber is not silently shown the free tier.
    /// </summary>
    public SubscriptionVerificationState SubscriptionVerification { get; private set; }
        = SubscriptionVerificationState.Unverified;
    // Creator status has no JWT claim — it comes from the Creators table, not a role — so it is
    // persisted alongside the token and restored with it. Without this a creator who relaunches the
    // app loses their own-song playback bypass until they log in again.
    public bool IsCreator { get; private set; }
    public int? CreatorId { get; private set; }

    // Roles come from the login response and are re-parsed from the JWT role claims on session
    // restore, so admin status survives app restarts without an extra server round trip.
    // Fully qualified: the Roles property below shadows MusicSalesApp.Common.Helpers.Roles.
    public bool IsAdmin => IsLoggedIn && Roles.Contains(Common.Helpers.Roles.Admin);
    public IReadOnlyList<string> Roles { get; private set; } = [];
    public string? Token { get; private set; }

    public AuthService(IHttpClientFactory httpClientFactory, IConfiguration configuration,
        ILogger<AuthService> logger, IWebAuthenticatorService webAuthenticatorService,
        IBillingService billingService, IMusicService musicService, ISecureStorage secureStorage,
        IBiometricAuthenticator biometrics,
        IOfflinePlaylistStore? offlinePlaylistStore = null,
        IOfflineSongCatalogStore? offlineSongCatalogStore = null,
        IUserStreamedSongStore? userStreamedSongStore = null,
        IAppleSignInService? appleSignIn = null)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
        _webAuthenticatorService = webAuthenticatorService;
        _billingService = billingService;
        _musicService = musicService;
        _secureStorage = secureStorage;
        _biometrics = biometrics;
        _offlinePlaylistStore = offlinePlaylistStore;
        _offlineSongCatalogStore = offlineSongCatalogStore;
        _userStreamedSongStore = userStreamedSongStore;
        _appleSignIn = appleSignIn ?? new UnsupportedAppleSignInService();

        // The store can hand the app a purchase nobody asked for - an interrupted one replayed at
        // launch. Only this service can record it, and the billing service cannot depend on it
        // without closing a cycle, so it borrows the verification path through this callback.
        _billingService.UnverifiedPurchaseHandler = RecordUnverifiedPurchaseAsync;
    }

    /// <summary>
    /// Records a purchase the store delivered without anyone waiting for it. Returns true once the
    /// server has it, which is the billing service's signal that the transaction is safe to finish.
    /// </summary>
    private async Task<bool> RecordUnverifiedPurchaseAsync(BillingPurchaseVerificationRequest request)
    {
        if (!IsLoggedIn)
        {
            // Nothing to attach it to. Reported as not recorded so the transaction stays queued for
            // a launch where somebody is signed in.
            _logger.LogInformation(
                "An unsolicited {Provider} purchase arrived while signed out and was left for a later session",
                request.Provider);
            return false;
        }

        try
        {
            var verificationResult = await _musicService.VerifySubscriptionPurchaseAsync(request);
            if (!verificationResult.Success)
            {
                _logger.LogWarning(
                    "Server verification of an unsolicited {Provider} purchase failed: {Error}",
                    request.Provider,
                    verificationResult.ErrorMessage);
                return false;
            }

            _logger.LogInformation("Recorded an unsolicited {Provider} purchase with the server", request.Provider);
            await RefreshUserStatusAsync();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not record an unsolicited {Provider} purchase", request.Provider);
            return false;
        }
    }

    public async Task<bool> HasBiometricCredentialsAsync(CancellationToken cancellationToken = default)
    {
        var cachedState = Volatile.Read(ref _biometricCredentialsState);
        if (cachedState >= 0)
        {
            return cachedState == 1;
        }

        await _biometricStateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cachedState = Volatile.Read(ref _biometricCredentialsState);
            if (cachedState >= 0)
            {
                return cachedState == 1;
            }

            var email = await _secureStorage.GetAsync(BioEmailKey)
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            var password = await _secureStorage.GetAsync(BioPasswordKey)
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            var hasCredentials = !string.IsNullOrWhiteSpace(email) && !string.IsNullOrEmpty(password);
            Volatile.Write(ref _biometricCredentialsState, hasCredentials ? 1 : 0);
            return hasCredentials;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read biometric login state from secure storage");
            return false;
        }
        finally
        {
            _biometricStateLock.Release();
        }
    }

    public async Task<(bool Success, string Error)> LoginAsync(string email, string password)
    {
        var client = _httpClientFactory.CreateClient("MusicSalesApi");
        try
        {
            var response = await client.PostAsJsonAsync("api/mobile-auth/login", new LoginRequestDto { Email = email, Password = password });
            if (!response.IsSuccessStatusCode)
            {
                var error = await ReadErrorMessageAsync(response);
                return (false, error);
            }

            var data = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
            if (data == null)
                return (false, "Invalid server response.");

            await StoreSessionAsync(data);
            return (true, string.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Login failed");
            return (false, "Unable to connect to server. Please check your internet connection.");
        }
    }

    public async Task<ExternalAuthResultDto> AuthenticateWithGoogleAsync()
    {
        var client = _httpClientFactory.CreateClient("MusicSalesApi");
        try
        {
            if (client.BaseAddress == null)
            {
                return new ExternalAuthResultDto { ErrorMessage = "Google sign-in is not configured." };
            }

            var callbackUrl = _configuration["MobileExternalAuth:CallbackUrl"] ?? "streamtunes://auth";
            var authResult = await _webAuthenticatorService.AuthenticateAsync(
                new Uri(client.BaseAddress, "api/mobile-auth/google/start"),
                new Uri(callbackUrl));

            if (authResult.Properties.TryGetValue("error", out var error) && !string.IsNullOrWhiteSpace(error))
            {
                return new ExternalAuthResultDto { ErrorMessage = error };
            }

            if (authResult.Properties.TryGetValue("pendingRegistrationToken", out var pendingToken) &&
                !string.IsNullOrWhiteSpace(pendingToken))
            {
                authResult.Properties.TryGetValue("email", out var pendingEmail);
                return new ExternalAuthResultDto
                {
                    RequiresRegistration = true,
                    PendingRegistrationToken = pendingToken,
                    Email = pendingEmail ?? string.Empty
                };
            }

            if (!authResult.Properties.TryGetValue("exchangeToken", out var exchangeToken) ||
                string.IsNullOrWhiteSpace(exchangeToken))
            {
                return new ExternalAuthResultDto { ErrorMessage = "Google sign-in returned an invalid response." };
            }

            var response = await client.PostAsJsonAsync("api/mobile-auth/google/exchange",
                new GoogleExchangeRequestDto { ExchangeToken = exchangeToken });
            if (!response.IsSuccessStatusCode)
            {
                var exchangeError = await ReadErrorMessageAsync(response);
                return new ExternalAuthResultDto { ErrorMessage = exchangeError };
            }

            var data = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
            if (data == null)
            {
                return new ExternalAuthResultDto { ErrorMessage = "Invalid server response." };
            }

            await StoreSessionAsync(data);
            return new ExternalAuthResultDto
            {
                Success = true,
                Email = data.Email
            };
        }
        catch (TaskCanceledException)
        {
            // Dismissing the web sheet is a decision, not a fault - same as Apple.
            return new ExternalAuthResultDto { WasCancelled = true };
        }
        catch (Exception ex) when (ex is NotSupportedException or PlatformNotSupportedException)
        {
            _logger.LogWarning(ex, "Google sign-in is not supported on this platform");
            return new ExternalAuthResultDto { ErrorMessage = "Google sign-in is not available on this platform yet." };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Google sign-in failed");
            return new ExternalAuthResultDto { ErrorMessage = "Unable to connect to server. Please check your internet connection." };
        }
    }

    public bool IsAppleSignInSupported => _appleSignIn.IsSupported;

    public async Task<ExternalAuthResultDto> AuthenticateWithAppleAsync()
    {
        var client = _httpClientFactory.CreateClient("MusicSalesApi");
        try
        {
            if (client.BaseAddress == null)
            {
                return new ExternalAuthResultDto { ErrorMessage = "Sign in with Apple is not configured." };
            }

            var appleResult = await _appleSignIn.AuthenticateAsync();

            // Dismissing the native sheet is a decision, not a fault - say nothing.
            if (appleResult.WasCancelled)
            {
                return new ExternalAuthResultDto { WasCancelled = true };
            }

            if (!appleResult.Success)
            {
                return new ExternalAuthResultDto
                {
                    ErrorMessage = string.IsNullOrWhiteSpace(appleResult.ErrorMessage)
                        ? "Apple sign-in failed."
                        : appleResult.ErrorMessage
                };
            }

            var response = await client.PostAsJsonAsync("api/mobile-auth/apple/token", new AppleTokenRequestDto
            {
                IdentityToken = appleResult.IdentityToken,
                AuthorizationCode = appleResult.AuthorizationCode,
                Email = appleResult.Email,
                FullName = appleResult.FullName
            });

            if (!response.IsSuccessStatusCode)
            {
                return new ExternalAuthResultDto { ErrorMessage = await ReadErrorMessageAsync(response) };
            }

            var data = await response.Content.ReadFromJsonAsync<AppleTokenResponseDto>();
            if (data == null)
            {
                return new ExternalAuthResultDto { ErrorMessage = "Invalid server response." };
            }

            if (data.RequiresRegistration)
            {
                return new ExternalAuthResultDto
                {
                    RequiresRegistration = true,
                    PendingRegistrationToken = data.PendingRegistrationToken,
                    Email = data.Email
                };
            }

            if (data.Login == null)
            {
                return new ExternalAuthResultDto { ErrorMessage = "Invalid server response." };
            }

            await StoreSessionAsync(data.Login);
            return new ExternalAuthResultDto
            {
                Success = true,
                Email = data.Login.Email
            };
        }
        catch (TaskCanceledException ex)
        {
            // NOT a dismissed sheet - that arrives as AppleSignInResult.Cancelled() and is handled
            // above. Reaching here means the call to the server timed out, which the user has to be
            // told about rather than left staring at an idle page.
            _logger.LogWarning(ex, "Apple sign-in timed out talking to the server");
            return new ExternalAuthResultDto
            {
                ErrorMessage = "Unable to connect to server. Please check your internet connection."
            };
        }
        catch (Exception ex) when (ex is NotSupportedException or PlatformNotSupportedException)
        {
            _logger.LogWarning(ex, "Sign in with Apple is not supported on this platform");
            return new ExternalAuthResultDto { ErrorMessage = "Sign in with Apple is not available on this platform." };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Apple sign-in failed");
            return new ExternalAuthResultDto { ErrorMessage = "Unable to connect to server. Please check your internet connection." };
        }
    }

    public async Task<(bool Success, string Error)> CompleteAppleRegistrationAsync(string pendingRegistrationToken,
        bool acceptTermsOfUse, bool acceptPrivacyPolicy, bool acceptRefundPolicy)
    {
        var client = _httpClientFactory.CreateClient("MusicSalesApi");
        try
        {
            var response = await client.PostAsJsonAsync("api/mobile-auth/apple/register", new AppleRegisterRequestDto
            {
                PendingRegistrationToken = pendingRegistrationToken,
                AcceptTermsOfUse = acceptTermsOfUse,
                AcceptPrivacyPolicy = acceptPrivacyPolicy,
                AcceptRefundPolicy = acceptRefundPolicy
            });
            if (!response.IsSuccessStatusCode)
            {
                var error = await ReadErrorMessageAsync(response);
                return (false, error);
            }

            var data = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
            if (data == null)
            {
                return (false, "Invalid server response.");
            }

            await StoreSessionAsync(data);
            return (true, string.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Apple registration failed");
            return (false, "Unable to connect to server. Please check your internet connection.");
        }
    }

    public async Task<(bool Success, string Error)> CompleteGoogleRegistrationAsync(string pendingRegistrationToken,
        bool acceptTermsOfUse, bool acceptPrivacyPolicy, bool acceptRefundPolicy)
    {
        var client = _httpClientFactory.CreateClient("MusicSalesApi");
        try
        {
            var response = await client.PostAsJsonAsync("api/mobile-auth/google/register", new GoogleRegisterRequestDto
            {
                PendingRegistrationToken = pendingRegistrationToken,
                AcceptTermsOfUse = acceptTermsOfUse,
                AcceptPrivacyPolicy = acceptPrivacyPolicy,
                AcceptRefundPolicy = acceptRefundPolicy
            });
            if (!response.IsSuccessStatusCode)
            {
                var error = await ReadErrorMessageAsync(response);
                return (false, error);
            }

            var data = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
            if (data == null)
            {
                return (false, "Invalid server response.");
            }

            await StoreSessionAsync(data);
            return (true, string.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Google registration failed");
            return (false, "Unable to connect to server. Please check your internet connection.");
        }
    }

    public async Task<(bool Success, string Error, int UserId)> RegisterAsync(string email, string password)
    {
        var client = _httpClientFactory.CreateClient("MusicSalesApi");
        try
        {
            var response = await client.PostAsJsonAsync("api/mobile-auth/register", new RegisterRequestDto { Email = email, Password = password });
            if (!response.IsSuccessStatusCode)
            {
                var error = await ReadErrorMessageAsync(response);
                return (false, error, 0);
            }

            var data = await response.Content.ReadFromJsonAsync<RegisterResponseDto>();
            return (true, string.Empty, data?.UserId ?? 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Registration failed");
            return (false, "Unable to connect to server. Please check your internet connection.", 0);
        }
    }

    public async Task<(bool Success, string Error, LoginResponseDto? LoginData)> VerifyCodeAsync(int userId, string code)
    {
        var client = _httpClientFactory.CreateClient("MusicSalesApi");
        try
        {
            var response = await client.PostAsJsonAsync("api/mobile-auth/verify-code", new VerifyCodeRequestDto { UserId = userId, Code = code });
            if (!response.IsSuccessStatusCode)
            {
                var error = await ReadErrorMessageAsync(response);
                return (false, error, null);
            }

            var data = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
            if (data != null)
                await StoreSessionAsync(data);

            return (true, string.Empty, data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Code verification failed");
            return (false, "Unable to connect to server. Please check your internet connection.", null);
        }
    }

    public async Task<(bool Success, string Error)> ResendCodeAsync(int userId)
    {
        var client = _httpClientFactory.CreateClient("MusicSalesApi");
        try
        {
            var response = await client.PostAsJsonAsync("api/mobile-auth/resend-code", new ResendCodeRequestDto { UserId = userId });
            if (!response.IsSuccessStatusCode)
            {
                var error = await ReadErrorMessageAsync(response);
                return (false, error);
            }
            return (true, string.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Resend code failed");
            return (false, "Unable to connect to server.");
        }
    }

    public async Task<(bool Success, string Error)> ChangeEmailAsync(int userId, string newEmail)
    {
        var client = _httpClientFactory.CreateClient("MusicSalesApi");
        try
        {
            var response = await client.PostAsJsonAsync("api/mobile-auth/change-email",
                new ChangeEmailRequestDto { UserId = userId, NewEmail = newEmail });
            if (!response.IsSuccessStatusCode)
            {
                var error = await ReadErrorMessageAsync(response);
                return (false, error);
            }

            // Captured before the assignment below overwrites it: the biometric pair is only this
            // user's to rewrite if it was saved under the address they are changing away from.
            var previousEmail = Email;

            // Update locally stored email
            Email = newEmail;
            await _secureStorage.SetAsync(EmailStorageKey, newEmail);
            // The biometric pair is a second copy of the login, and only this one was being kept in
            // step. A stale bio_email meant the fingerprint replayed the old address at the server.
            await UpdateBiometricEmailAsync(previousEmail, newEmail);
            NotifyAuthStateChanged();

            return (true, string.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Change email failed");
            return (false, "Unable to connect to server.");
        }
    }

    public async Task<(bool Success, string Error, int UserId)> ForgotPasswordAsync(string email)
    {
        var client = _httpClientFactory.CreateClient("MusicSalesApi");
        try
        {
            var response = await client.PostAsJsonAsync("api/mobile-auth/forgot-password", new ForgotPasswordRequestDto { Email = email });
            if (!response.IsSuccessStatusCode)
            {
                var error = await ReadErrorMessageAsync(response);
                return (false, error, 0);
            }

            var data = await response.Content.ReadFromJsonAsync<ForgotPasswordResponseDto>();
            return (true, string.Empty, data?.UserId ?? 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Forgot password failed");
            return (false, "Unable to connect to server.", 0);
        }
    }

    public async Task<(bool Success, string Error)> ResetPasswordAsync(int userId, string code, string newPassword, string email)
    {
        var client = _httpClientFactory.CreateClient("MusicSalesApi");
        try
        {
            var response = await client.PostAsJsonAsync("api/mobile-auth/reset-password",
                new ResetPasswordRequestDto { UserId = userId, Code = code, NewPassword = newPassword });
            if (!response.IsSuccessStatusCode)
            {
                var error = await ReadErrorMessageAsync(response);
                return (false, error);
            }

            // The saved biometric password is now the old one. Left in place it produces the worst
            // kind of failure: a successful fingerprint prompt followed by a rejected login, which
            // reads as the biometrics being broken rather than the credential being stale.
            //
            // Only for the account that was actually reset. This flow is reachable from the login
            // screen with no session, so on a shared device it can just as easily be someone else
            // resetting their own password, and their reset must not withdraw this device's owner's
            // fingerprint sign-in.
            await DisableBiometricLoginForAsync(email);

            return (true, string.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reset password failed");
            return (false, "Unable to connect to server.");
        }
    }

    public async Task LogoutAsync()
    {
        await _musicService.ClearPendingStreamRecordsAsync();
        // Queued like/dislike intents belong to the outgoing user - replaying them under the next
        // account would attribute their opinions to someone else.
        await _musicService.ClearPendingLikeStatesAsync();
        // Same reasoning for what the outgoing user has listened to: it is what entitles them to rate a
        // song, so leaving it behind would hand the next account those ratings.
        _userStreamedSongStore?.Clear();
        await ClearOfflineSnapshotsAsync();
        _secureStorage.Remove(TokenStorageKey);
        _secureStorage.Remove(AuthStorageKeys.UserId);
        _secureStorage.Remove(EmailStorageKey);
        _secureStorage.Remove(EmailConfirmedStorageKey);
        _secureStorage.Remove(IsCreatorStorageKey);
        _secureStorage.Remove(CreatorIdStorageKey);
        // Written by the same login that issued this token, so it must not outlive it — otherwise
        // the next account to sign in on this device could inherit the outgoing user's entitlement.
        _secureStorage.Remove(SubscriptionStatusStorageKey);
        ClearState();
        NotifyAuthStateChanged();
    }

    /// <summary>
    /// Clears the outgoing user's data out of the offline snapshots.
    ///
    /// Neither store is namespaced by account, so without this the next person to sign in would see the
    /// previous user's playlists and votes while offline. The two are treated differently on purpose:
    /// playlists are wholly personal and go entirely, whereas the song catalog is public and only the
    /// thumbs-up/down state on it is personal. Deleting the catalog would take offline playback away
    /// too - including on the session-expiry logout that can fire at startup with no network - so only
    /// the votes are stripped.
    /// </summary>
    private async Task ClearOfflineSnapshotsAsync()
    {
        try
        {
            if (_offlinePlaylistStore != null)
                await _offlinePlaylistStore.ClearAsync();

            if (_offlineSongCatalogStore != null)
                await _offlineSongCatalogStore.ClearUserLikeStatesAsync();
        }
        catch (Exception ex)
        {
            // Never block a logout on a file delete; the next successful load overwrites both stores.
            _logger.LogWarning(ex, "Failed to clear the offline snapshots during logout");
        }
    }

    public async Task TryRestoreSessionAsync()
    {
        try
        {
            var token = await _secureStorage.GetAsync(TokenStorageKey);
            if (string.IsNullOrEmpty(token))
                return;

            // Every sign-out from here on is one the user did not ask for, so each of the three ways
            // out leaves a notice. Whichever one fires, the visible symptom is identical — the app
            // simply comes up logged out — and an unexplained sign-out is the whole thing the notice
            // exists to prevent. Only the reasons differ, and none of them is the user's doing.
            var handler = new JwtSecurityTokenHandler();
            if (!handler.CanReadToken(token))
            {
                _logger.LogWarning("The stored token could not be read, clearing session");
                await LogoutWithExpiryNoticeAsync();
                return;
            }

            var jwt = handler.ReadJwtToken(token);
            if (jwt.ValidTo < DateTime.UtcNow)
            {
                _logger.LogInformation("Stored JWT token has expired, clearing session");
                await LogoutWithExpiryNoticeAsync();
                return;
            }

            // Restore state from token claims
            Token = token;
            var claims = jwt.Claims.ToList();
            UserId = int.TryParse(claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier || c.Type == "nameid")?.Value, out var uid) ? uid : null;
            Email = claims.FirstOrDefault(c => c.Type == ClaimTypes.Email || c.Type == "email")?.Value;
            Roles = claims.Where(c => c.Type == ClaimTypes.Role || c.Type == "role").Select(c => c.Value).ToList();
            IsLoggedIn = true;

            // A restore that succeeds answers any notice left by an earlier one, the same way a login
            // does. This path does not go through ApplyLoginResponse, so it clears the notice itself.
            Volatile.Write(ref _pendingSessionExpiryNotice, null);

            // Restore EmailConfirmed from SecureStorage (defaults to false if not stored)
            var storedEmailConfirmed = await _secureStorage.GetAsync(EmailConfirmedStorageKey);
            EmailConfirmed = string.Equals(storedEmailConfirmed, "true", StringComparison.OrdinalIgnoreCase);

            // Creator status is not carried in the token, so it comes back from SecureStorage. It
            // was written by the same login that issued this token and is cleared with it, so it
            // can never outlive the session it describes.
            var storedIsCreator = await _secureStorage.GetAsync(IsCreatorStorageKey);
            IsCreator = string.Equals(storedIsCreator, "true", StringComparison.OrdinalIgnoreCase);
            var storedCreatorId = await _secureStorage.GetAsync(CreatorIdStorageKey);
            CreatorId = int.TryParse(storedCreatorId, out var creatorId) ? creatorId : null;

            // Apply the cached entitlement first so a server that cannot be reached leaves a paying
            // subscriber on their subscription rather than silently on the free tier. A successful
            // refresh below overwrites all of it.
            await RestoreSubscriptionStatusAsync();

            // Refresh subscription status from server
            await RefreshUserStatusAsync();

            // Restore any unverified Google Play purchases
            if (!HasActiveSubscription)
                await TryRestoreBillingAsync();

            await _musicService.FlushPendingStreamRecordsAsync();
            NotifyAuthStateChanged();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to restore session");
            await LogoutWithExpiryNoticeAsync();
        }
    }

    public async Task EnableBiometricLoginAsync(string email, string password)
    {
        await _biometricStateLock.WaitAsync().ConfigureAwait(false);
        try
        {
            Volatile.Write(ref _biometricCredentialsState, -1);
            try
            {
                await _secureStorage.SetAsync(BioEmailKey, email).ConfigureAwait(false);
                await _secureStorage.SetAsync(BioPasswordKey, password).ConfigureAwait(false);
                Volatile.Write(ref _biometricCredentialsState, 1);
            }
            catch
            {
                _secureStorage.Remove(BioEmailKey);
                _secureStorage.Remove(BioPasswordKey);
                Volatile.Write(ref _biometricCredentialsState, 0);
                throw;
            }
        }
        finally
        {
            _biometricStateLock.Release();
        }
    }

    /// <summary>
    /// Keeps the saved biometric email in step with an email change, but only when the saved pair is
    /// the one being changed.
    ///
    /// The pair deliberately outlives a logout, so the account signed in now is not necessarily the
    /// account it belongs to. Rewriting it unconditionally would pair one person's address with
    /// another's password — a credential that passes the fingerprint prompt every time and is
    /// rejected by the server every time, with no way back except turning the feature off.
    ///
    /// Also a no-op when biometric login was never enabled: writing the address on its own would
    /// leave half a credential behind. Reads the keys directly rather than calling
    /// <see cref="HasBiometricCredentialsAsync"/>, which takes the same lock.
    /// </summary>
    private async Task UpdateBiometricEmailAsync(string? previousEmail, string newEmail)
    {
        await _biometricStateLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var storedEmail = await _secureStorage.GetAsync(BioEmailKey).ConfigureAwait(false);
            var storedPassword = await _secureStorage.GetAsync(BioPasswordKey).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(storedEmail)
                || string.IsNullOrEmpty(storedPassword)
                || !string.Equals(storedEmail, previousEmail, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await _secureStorage.SetAsync(BioEmailKey, newEmail).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // An email change that succeeded on the server must not report failure over this.
            _logger.LogWarning(ex, "Could not update the saved biometric email after an email change");
        }
        finally
        {
            _biometricStateLock.Release();
        }
    }

    /// <summary>
    /// Withdraws the saved credentials only when they belong to <paramref name="email"/>. For flows
    /// that can run against an account other than the one this device saved — a password reset from
    /// the login screen, with no session at all — where clearing unconditionally would take away a
    /// bystander's fingerprint sign-in over someone else's password change.
    /// </summary>
    private async Task DisableBiometricLoginForAsync(string email)
    {
        string? storedEmail;
        await _biometricStateLock.WaitAsync().ConfigureAwait(false);
        try
        {
            storedEmail = await _secureStorage.GetAsync(BioEmailKey).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read the saved biometric email");
            return;
        }
        finally
        {
            _biometricStateLock.Release();
        }

        if (!string.IsNullOrWhiteSpace(storedEmail)
            && string.Equals(storedEmail, email, StringComparison.OrdinalIgnoreCase))
        {
            await DisableBiometricLoginAsync().ConfigureAwait(false);
        }
    }

    public async Task DisableBiometricLoginAsync()
    {
        await _biometricStateLock.WaitAsync().ConfigureAwait(false);
        try
        {
            _secureStorage.Remove(BioEmailKey);
            _secureStorage.Remove(BioPasswordKey);
            Volatile.Write(ref _biometricCredentialsState, 0);
        }
        catch (Exception ex)
        {
            // A damaged keystore must not take the app down from a settings tap or an account
            // deletion. The state is left unknown rather than claimed clear, so the next
            // HasBiometricCredentialsAsync re-reads instead of trusting a removal that did not happen.
            _logger.LogWarning(ex, "Could not remove the saved biometric credentials");
            Volatile.Write(ref _biometricCredentialsState, -1);
        }
        finally
        {
            _biometricStateLock.Release();
        }
    }

    public async Task<(bool Success, string Error)> BiometricLoginAsync()
    {
        string? email;
        string? password;
        await _biometricStateLock.WaitAsync().ConfigureAwait(false);
        try
        {
            email = await _secureStorage.GetAsync(BioEmailKey).ConfigureAwait(false);
            password = await _secureStorage.GetAsync(BioPasswordKey).ConfigureAwait(false);
            Volatile.Write(
                ref _biometricCredentialsState,
                !string.IsNullOrWhiteSpace(email) && !string.IsNullOrEmpty(password) ? 1 : 0);
        }
        finally
        {
            _biometricStateLock.Release();
        }

        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
        {
            return (false, "No saved credentials. Please log in with your password first.");
        }

        // Prompt for biometric authentication before using stored credentials
        var biometricResult = await PromptBiometricAsync();
        if (!biometricResult.Success)
            return (false, biometricResult.Error);

        return await LoginAsync(email, password);
    }

    public Task<BiometricAvailability> GetBiometricAvailabilityAsync() => _biometrics.GetAvailabilityAsync();

    /// <summary>
    /// Shows the device's biometric prompt.
    /// </summary>
    /// <remarks>
    /// This was <c>#if ANDROID</c> around a static call, with a hard-coded "not supported on this
    /// platform" everywhere else. Routing it through <see cref="IBiometricAuthenticator"/> is what
    /// lets iOS answer, and what lets <see cref="BiometricLoginAsync"/> be tested at all - the
    /// compile-time branch left no seam to stand a double in.
    /// </remarks>
    private Task<(bool Success, string Error)> PromptBiometricAsync() => _biometrics.AuthenticateAsync();

    // --- Private helpers ---

    private async Task StoreSessionAsync(LoginResponseDto data)
    {
        ApplyLoginResponse(data);

        await _secureStorage.SetAsync(TokenStorageKey, data.Token);
        await _secureStorage.SetAsync(AuthStorageKeys.UserId, data.UserId.ToString());
        await _secureStorage.SetAsync(EmailStorageKey, data.Email);
        await _secureStorage.SetAsync(EmailConfirmedStorageKey, data.EmailConfirmed.ToString());
        await StoreCreatorStatusAsync(data.IsCreator, data.CreatorId);
        await StoreSubscriptionStatusAsync();

        // Restore any unverified Google Play purchases after login
        if (!HasActiveSubscription)
            await TryRestoreBillingAsync();

        await _musicService.FlushPendingStreamRecordsAsync();
        NotifyAuthStateChanged();
    }

    private async Task StoreCreatorStatusAsync(bool isCreator, int? creatorId)
    {
        await _secureStorage.SetAsync(IsCreatorStorageKey, isCreator.ToString());

        if (creatorId.HasValue)
        {
            await _secureStorage.SetAsync(CreatorIdStorageKey, creatorId.Value.ToString());
        }
        else
        {
            // A stale id left over from a previous account would outlive the flag that gates it.
            _secureStorage.Remove(CreatorIdStorageKey);
        }
    }

    internal void ApplyLoginResponse(LoginResponseDto data)
    {
        Token = data.Token;
        UserId = data.UserId;
        Email = data.Email;
        Roles = data.Roles;
        EmailConfirmed = data.EmailConfirmed;
        HasActiveSubscription = data.HasActiveSubscription;
        SubscriptionStatus = data.SubscriptionStatus ?? (data.HasActiveSubscription ? SubscriptionStatuses.Active : null);
        SubscriptionEndDate = data.SubscriptionEndDate;
        IsOnTrial = data.IsOnTrial;
        TrialEndDate = data.TrialEndDate;
        BillingSource = data.BillingSource;
        // The login response came from the server, so this entitlement is as verified as a status
        // refresh. Caching it here means a user who logs in and immediately goes offline still has
        // a snapshot to fall back on at the next launch.
        SubscriptionVerification = SubscriptionVerificationState.Verified;
        IsCreator = data.IsCreator;
        CreatorId = data.CreatorId;
        IsLoggedIn = true;

        // Whatever the previous session's expiry still had to say, signing in is the answer to it.
        Volatile.Write(ref _pendingSessionExpiryNotice, null);
    }

    private void ClearState()
    {
        Token = null;
        UserId = null;
        Email = null;
        EmailConfirmed = false;
        HasActiveSubscription = false;
        SubscriptionStatus = null;
        SubscriptionEndDate = null;
        IsOnTrial = false;
        TrialEndDate = null;
        BillingSource = null;
        IsCreator = false;
        CreatorId = null;
        Roles = [];
        IsLoggedIn = false;
        SubscriptionVerification = SubscriptionVerificationState.Unverified;

        // A restore owed to the signed-out user must not be retried against whoever signs in next.
        _billingRestorePending = false;
    }

    public async Task RefreshUserStatusAsync()
    {
        try
        {
            var previousHasActiveSubscription = HasActiveSubscription;
            var previousSubscriptionStatus = SubscriptionStatus;
            var previousSubscriptionEndDate = SubscriptionEndDate;
            var previousIsOnTrial = IsOnTrial;
            var previousTrialEndDate = TrialEndDate;
            var previousBillingSource = BillingSource;
            var previousIsCreator = IsCreator;
            var previousCreatorId = CreatorId;

            var client = _httpClientFactory.CreateClient("MusicSalesApi");
            if (!string.IsNullOrEmpty(Token))
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Token);

            var response = await client.GetFromJsonAsync<SubscriptionStatusDto>("api/subscription/status");

            // A null body — a 204, or a literal JSON null, neither of which throws — says nothing
            // about the subscription. Treating it as an authoritative "no subscription" would mark
            // the session Verified and write an empty snapshot over a good cached one, silently
            // dropping a paying subscriber to the free tier with the banner suppressed and nothing
            // left to restore on the next offline launch.
            if (response is null)
            {
                _logger.LogInformation(
                    "The subscription status endpoint returned no content; keeping the last known entitlement");
                return;
            }

            HasActiveSubscription = response?.HasSubscription ?? false;
            SubscriptionStatus = response?.Status;
            SubscriptionEndDate = response?.EndDate;
            IsOnTrial = response?.IsOnTrial ?? false;
            TrialEndDate = response?.TrialEndDate;
            BillingSource = response?.BillingSource;
            SubscriptionVerification = SubscriptionVerificationState.Verified;

            // Write through on every answer, including a negative one — otherwise a subscription
            // that genuinely lapsed would be resurrected by the old cache on the next offline launch.
            await StoreSubscriptionStatusAsync();

            // Creator status is cached in secure storage so it survives app restarts, which means a
            // deactivation on the web would otherwise go unnoticed until the JWT expired. Only apply
            // it when the server actually reported it: an absent field means the server is older
            // than this feature (or was rolled back), which must never revoke a creator's
            // own-song playback - and would persist that revocation across restarts.
            if (response?.IsCreator is bool serverIsCreator)
            {
                IsCreator = serverIsCreator;
                CreatorId = response.CreatorId;

                if (previousIsCreator != IsCreator || previousCreatorId != CreatorId)
                {
                    // Persisting is best-effort. A keystore failure must not cost us the state
                    // change notification below, or the UI would render stale entitlements for the
                    // rest of the session.
                    try
                    {
                        await StoreCreatorStatusAsync(IsCreator, CreatorId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Could not persist refreshed creator status");
                    }
                }
            }

            if (previousHasActiveSubscription != HasActiveSubscription ||
                previousSubscriptionStatus != SubscriptionStatus ||
                previousSubscriptionEndDate != SubscriptionEndDate ||
                previousIsOnTrial != IsOnTrial ||
                previousTrialEndDate != TrialEndDate ||
                previousBillingSource != BillingSource ||
                previousIsCreator != IsCreator ||
                previousCreatorId != CreatorId)
            {
                NotifyAuthStateChanged();
            }
        }
        catch (Exception ex)
        {
            // Information, not Debug: the file logger's floor is Information, and a status refresh
            // that failed is exactly what you need to see when entitlement looks wrong offline.
            // Not a Warning — going offline is a supported state in this app, not a fault.
            _logger.LogInformation(ex, "Could not refresh subscription status from the server; keeping the last known entitlement");
        }

        // Deliberately not awaited. A status refresh is a cheap call that UI sits in front of —
        // AccountSettingsViewModel awaits it before rendering — and the retry reaches the platform
        // store, which can cost the connection timeout plus the query timeout on a wedged device.
        // Joining the two turned every refresh into a call that could stall a page for ~25s.
        _ = RetryPendingBillingRestoreAsync();
    }

    /// <summary>
    /// Persists the server's latest subscription answer so an offline launch has something to fall
    /// back on. Best-effort: a keystore failure must never cost the caller its refreshed state.
    /// </summary>
    private async Task StoreSubscriptionStatusAsync()
    {
        try
        {
            var snapshot = new CachedSubscriptionStatus
            {
                HasActiveSubscription = HasActiveSubscription,
                SubscriptionStatus = SubscriptionStatus,
                SubscriptionEndDate = SubscriptionEndDate,
                IsOnTrial = IsOnTrial,
                TrialEndDate = TrialEndDate,
                BillingSource = BillingSource,
                CachedAtUtc = DateTime.UtcNow
            };

            await _secureStorage.SetAsync(SubscriptionStatusStorageKey, snapshot.Serialize());
            _logger.LogInformation(
                "Cached subscription status for offline use. HasActiveSubscription={HasActiveSubscription}; Status={Status}; EndDate={EndDate}; IsOnTrial={IsOnTrial}; TrialEndDate={TrialEndDate}",
                snapshot.HasActiveSubscription,
                snapshot.SubscriptionStatus,
                snapshot.SubscriptionEndDate,
                snapshot.IsOnTrial,
                snapshot.TrialEndDate);
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "Could not cache the subscription status for offline use");
        }
    }

    /// <summary>
    /// Applies the cached subscription answer during session restore, before the server is asked.
    /// A successful refresh immediately overwrites whatever this sets, so the only time it decides
    /// anything is when the server cannot be reached — which is exactly the case it exists for.
    /// </summary>
    private async Task RestoreSubscriptionStatusAsync()
    {
        SubscriptionVerification = SubscriptionVerificationState.Unverified;

        try
        {
            var stored = await _secureStorage.GetAsync(SubscriptionStatusStorageKey);
            if (string.IsNullOrWhiteSpace(stored))
            {
                // Distinguished from the cases below on purpose: "nothing was ever cached" and
                // "what was cached is no longer good" call for completely different investigations.
                _logger.LogInformation("No cached subscription status is stored for this session");
                return;
            }

            if (!CachedSubscriptionStatus.TryParse(stored, out var snapshot) || snapshot is null)
            {
                _logger.LogInformation("The cached subscription status could not be read and was ignored");
                return;
            }

            if (!snapshot.IsUsableAt(DateTime.UtcNow))
            {
                // Expired or too stale to trust. Leaving the defaults in place drops the user to the
                // free tier, which is the safe direction to fail in.
                _logger.LogInformation(
                    "The cached subscription status is no longer usable and was ignored. HasActiveSubscription={HasActiveSubscription}; EndDate={EndDate}; IsOnTrial={IsOnTrial}; TrialEndDate={TrialEndDate}; CachedAtUtc={CachedAtUtc}",
                    snapshot.HasActiveSubscription,
                    snapshot.SubscriptionEndDate,
                    snapshot.IsOnTrial,
                    snapshot.TrialEndDate,
                    snapshot.CachedAtUtc);
                return;
            }

            HasActiveSubscription = snapshot.HasActiveSubscription;
            SubscriptionStatus = snapshot.SubscriptionStatus;
            SubscriptionEndDate = snapshot.SubscriptionEndDate;
            IsOnTrial = snapshot.IsOnTrial;
            TrialEndDate = snapshot.TrialEndDate;
            BillingSource = snapshot.BillingSource;
            SubscriptionVerification = SubscriptionVerificationState.Cached;
            _logger.LogInformation(
                "Applied the cached subscription status. HasActiveSubscription={HasActiveSubscription}; Status={Status}; CachedAtUtc={CachedAtUtc}",
                snapshot.HasActiveSubscription,
                snapshot.SubscriptionStatus,
                snapshot.CachedAtUtc);
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "Could not read the cached subscription status");
        }
    }

    public SessionExpiryNotice? PendingSessionExpiryNotice => Volatile.Read(ref _pendingSessionExpiryNotice);

    /// <summary>
    /// Records why a session ended without the user asking, from the same snapshot
    /// <see cref="RestoreSubscriptionStatusAsync"/> reads, and then hands the caller the logout it
    /// still has to perform.
    ///
    /// The snapshot is judged by age alone, not by <see cref="CachedSubscriptionStatus.IsUsableAt"/>,
    /// and the difference matters: that method also demands unexpired entitlement, because it decides
    /// what the user may *do*. This decides only what the user is *told*, and a subscriber whose
    /// access lapsed while the token sat expired is precisely the person with something to hear. The
    /// age guards still apply, so nobody is reminded of a subscription from months ago, and a snapshot
    /// stamped in the future by a wound-back clock is refused rather than read as brand new.
    /// </summary>
    private async Task LogoutWithExpiryNoticeAsync()
    {
        Volatile.Write(ref _pendingSessionExpiryNotice, await BuildSessionExpiryNoticeAsync());

        // Set before the logout, because LogoutAsync ends by raising AuthStateChanged and the handler
        // on the other side is what reads it.
        await LogoutAsync();
    }

    private async Task<SessionExpiryNotice> BuildSessionExpiryNoticeAsync()
    {
        try
        {
            var stored = await _secureStorage.GetAsync(SubscriptionStatusStorageKey);
            if (!CachedSubscriptionStatus.TryParse(stored, out var snapshot) || snapshot is null
                || !snapshot.IsFreshEnoughToDescribeAt(DateTime.UtcNow))
            {
                return new SessionExpiryNotice(HadConfirmedEntitlement: false, EntitlementEndDate: null);
            }

            return new SessionExpiryNotice(
                snapshot.HasActiveSubscription || snapshot.IsOnTrial,
                snapshot.SubscriptionEndDate ?? snapshot.TrialEndDate);
        }
        catch (Exception ex)
        {
            // The sign-out still has to be explained, just without the entitlement half of it.
            _logger.LogInformation(ex, "Could not read the cached subscription status for the session-expiry notice");
            return new SessionExpiryNotice(HadConfirmedEntitlement: false, EntitlementEndDate: null);
        }
    }

    /// <summary>
    /// Re-runs a purchase restore that could not reach the store the first time.
    ///
    /// A restore that the store answered is final, but one that never got to ask leaves the user
    /// looking unsubscribed — and, without this, would stay that way until the app was next
    /// launched. Every surface that shows entitlement already refreshes status through
    /// <see cref="RefreshUserStatusAsync"/>, so hanging the retry off that gets the user their
    /// trial or subscription back as soon as the store becomes reachable.
    /// </summary>
    private async Task RetryPendingBillingRestoreAsync()
    {
        // The server is authoritative: if it now reports a subscription there is nothing to repair.
        if (!_billingRestorePending || HasActiveSubscription)
            return;

        // Cleared before re-entering, so the success path inside TryRestoreBillingAsync — which
        // calls back into RefreshUserStatusAsync — cannot start this retry a second time.
        _billingRestorePending = false;
        await TryRestoreBillingAsync();
    }

    private void NotifyAuthStateChanged()
    {
        var handler = AuthStateChanged;
        if (handler == null)
        {
            return;
        }

        try
        {
            if (MainThread.IsMainThread)
            {
                handler();
                return;
            }

            MainThread.BeginInvokeOnMainThread(handler);
        }
        catch (Exception ex) when (ex is NotImplementedException || ex.GetType().Name == "NotImplementedInReferenceAssemblyException")
        {
            handler();
        }
    }

    /// <summary>
    /// Silently restores any unverified Google Play purchases (e.g., after reinstall).
    /// Verifies with the server and refreshes subscription status if a purchase is found.
    /// </summary>
    internal async Task TryRestoreBillingAsync()
    {
        try
        {
            var result = await _billingService.RestorePurchaseAsync();

            // "Could not reach the store" carries no information about what the user owns, so it
            // must not be accepted as "owns nothing". Remembering it is what lets the next status
            // refresh ask again instead of writing the answer off until the next app launch.
            _billingRestorePending = result is { BillingUnavailable: true };
            if (_billingRestorePending)
            {
                _logger.LogInformation(
                    "Could not reach the store to restore purchases ({ErrorMessage}); will retry on the next status refresh",
                    result?.ErrorMessage);
            }

            if (result is not { Success: true })
                return;

            var verificationResult = await _musicService.VerifySubscriptionPurchaseAsync(result.ToVerificationRequest());
            if (verificationResult.Success)
            {
                await RefreshUserStatusAsync();
                _logger.LogInformation("Successfully restored subscription purchase for provider {Provider}", result.Provider);
            }
            else
            {
                _logger.LogWarning(
                    "Restored subscription purchase for provider {Provider} but server verification failed: {ErrorMessage}",
                    result.Provider,
                    verificationResult.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            // Same reasoning as the status refresh above — at Debug this was below the file
            // logger's floor, so a restore that threw left no trace at all.
            _logger.LogInformation(ex, "Could not restore subscription purchases");
        }
    }

    public async Task<(bool Success, string Error)> DeleteAccountAsync()
    {
        var client = _httpClientFactory.CreateClient("MusicSalesApi");
        try
        {
            var response = await client.DeleteAsync("api/mobile-auth/account");
            if (!response.IsSuccessStatusCode)
            {
                var error = await ReadErrorMessageAsync(response);
                return (false, error);
            }

            // Biometric credentials deliberately outlive a logout, so a fingerprint can get the user
            // straight back in. There is nothing to get back into once the account is gone, and
            // leaving them would keep offering a fingerprint button that replays a deleted login.
            await DisableBiometricLoginAsync();
            await LogoutAsync();
            return (true, string.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Account deletion failed");
            return (false, "Unable to connect to server. Please check your internet connection.");
        }
    }

    private static async Task<string> ReadErrorMessageAsync(HttpResponseMessage response)
    {
        return await ApiErrorMessageFormatter.ReadDisplayMessageAsync(response);
    }
}
