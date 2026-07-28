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
    private readonly ISecureStorage _secureStorage;
    private readonly SemaphoreSlim _biometricStateLock = new(1, 1);
    private int _biometricCredentialsState = -1;

    private const string TokenStorageKey = "auth_token";
    private const string EmailStorageKey = "auth_email";
    private const string EmailConfirmedStorageKey = "auth_email_confirmed";
    private const string IsCreatorStorageKey = "auth_is_creator";
    private const string CreatorIdStorageKey = "auth_creator_id";
    private const string BioEmailKey = "bio_email";
    private const string BioPasswordKey = "bio_password";

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
        IBillingService billingService, IMusicService musicService, ISecureStorage secureStorage)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
        _webAuthenticatorService = webAuthenticatorService;
        _billingService = billingService;
        _musicService = musicService;
        _secureStorage = secureStorage;
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

    public async Task<GoogleAuthResultDto> AuthenticateWithGoogleAsync()
    {
        var client = _httpClientFactory.CreateClient("MusicSalesApi");
        try
        {
            if (client.BaseAddress == null)
            {
                return new GoogleAuthResultDto { ErrorMessage = "Google sign-in is not configured." };
            }

            var callbackUrl = _configuration["MobileExternalAuth:CallbackUrl"] ?? "streamtunes://auth";
            var authResult = await _webAuthenticatorService.AuthenticateAsync(
                new Uri(client.BaseAddress, "api/mobile-auth/google/start"),
                new Uri(callbackUrl));

            if (authResult.Properties.TryGetValue("error", out var error) && !string.IsNullOrWhiteSpace(error))
            {
                return new GoogleAuthResultDto { ErrorMessage = error };
            }

            if (authResult.Properties.TryGetValue("pendingRegistrationToken", out var pendingToken) &&
                !string.IsNullOrWhiteSpace(pendingToken))
            {
                authResult.Properties.TryGetValue("email", out var pendingEmail);
                return new GoogleAuthResultDto
                {
                    RequiresRegistration = true,
                    PendingRegistrationToken = pendingToken,
                    Email = pendingEmail ?? string.Empty
                };
            }

            if (!authResult.Properties.TryGetValue("exchangeToken", out var exchangeToken) ||
                string.IsNullOrWhiteSpace(exchangeToken))
            {
                return new GoogleAuthResultDto { ErrorMessage = "Google sign-in returned an invalid response." };
            }

            var response = await client.PostAsJsonAsync("api/mobile-auth/google/exchange",
                new GoogleExchangeRequestDto { ExchangeToken = exchangeToken });
            if (!response.IsSuccessStatusCode)
            {
                var exchangeError = await ReadErrorMessageAsync(response);
                return new GoogleAuthResultDto { ErrorMessage = exchangeError };
            }

            var data = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
            if (data == null)
            {
                return new GoogleAuthResultDto { ErrorMessage = "Invalid server response." };
            }

            await StoreSessionAsync(data);
            return new GoogleAuthResultDto
            {
                Success = true,
                Email = data.Email
            };
        }
        catch (TaskCanceledException)
        {
            return new GoogleAuthResultDto { ErrorMessage = "Google sign-in was cancelled." };
        }
        catch (Exception ex) when (ex is NotSupportedException or PlatformNotSupportedException)
        {
            _logger.LogWarning(ex, "Google sign-in is not supported on this platform");
            return new GoogleAuthResultDto { ErrorMessage = "Google sign-in is not available on this platform yet." };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Google sign-in failed");
            return new GoogleAuthResultDto { ErrorMessage = "Unable to connect to server. Please check your internet connection." };
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

            // Update locally stored email
            Email = newEmail;
            await _secureStorage.SetAsync(EmailStorageKey, newEmail);
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

    public async Task<(bool Success, string Error)> ResetPasswordAsync(int userId, string code, string newPassword)
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
        _secureStorage.Remove(TokenStorageKey);
        _secureStorage.Remove(AuthStorageKeys.UserId);
        _secureStorage.Remove(EmailStorageKey);
        _secureStorage.Remove(EmailConfirmedStorageKey);
        _secureStorage.Remove(IsCreatorStorageKey);
        _secureStorage.Remove(CreatorIdStorageKey);
        ClearState();
        NotifyAuthStateChanged();
    }

    public async Task TryRestoreSessionAsync()
    {
        try
        {
            var token = await _secureStorage.GetAsync(TokenStorageKey);
            if (string.IsNullOrEmpty(token))
                return;

            // Validate token expiry
            var handler = new JwtSecurityTokenHandler();
            if (!handler.CanReadToken(token))
            {
                await LogoutAsync();
                return;
            }

            var jwt = handler.ReadJwtToken(token);
            if (jwt.ValidTo < DateTime.UtcNow)
            {
                _logger.LogInformation("Stored JWT token has expired, clearing session");
                await LogoutAsync();
                return;
            }

            // Restore state from token claims
            Token = token;
            var claims = jwt.Claims.ToList();
            UserId = int.TryParse(claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier || c.Type == "nameid")?.Value, out var uid) ? uid : null;
            Email = claims.FirstOrDefault(c => c.Type == ClaimTypes.Email || c.Type == "email")?.Value;
            Roles = claims.Where(c => c.Type == ClaimTypes.Role || c.Type == "role").Select(c => c.Value).ToList();
            IsLoggedIn = true;

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
            await LogoutAsync();
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

    public async Task DisableBiometricLoginAsync()
    {
        await _biometricStateLock.WaitAsync().ConfigureAwait(false);
        try
        {
            _secureStorage.Remove(BioEmailKey);
            _secureStorage.Remove(BioPasswordKey);
            Volatile.Write(ref _biometricCredentialsState, 0);
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

    private static async Task<(bool Success, string Error)> PromptBiometricAsync()
    {
#if ANDROID
        return await Platforms.Android.BiometricHelper.AuthenticateAsync();
#else
        await Task.CompletedTask;
        return (false, "Biometric authentication is not supported on this platform.");
#endif
    }

    // --- Private helpers ---

    private async Task StoreSessionAsync(LoginResponseDto data)
    {
        ApplyLoginResponse(data);

        await _secureStorage.SetAsync(TokenStorageKey, data.Token);
        await _secureStorage.SetAsync(AuthStorageKeys.UserId, data.UserId.ToString());
        await _secureStorage.SetAsync(EmailStorageKey, data.Email);
        await _secureStorage.SetAsync(EmailConfirmedStorageKey, data.EmailConfirmed.ToString());
        await StoreCreatorStatusAsync(data.IsCreator, data.CreatorId);

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
        IsCreator = data.IsCreator;
        CreatorId = data.CreatorId;
        IsLoggedIn = true;
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
            HasActiveSubscription = response?.HasSubscription ?? false;
            SubscriptionStatus = response?.Status;
            SubscriptionEndDate = response?.EndDate;
            IsOnTrial = response?.IsOnTrial ?? false;
            TrialEndDate = response?.TrialEndDate;
            BillingSource = response?.BillingSource;

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
            _logger.LogDebug(ex, "Could not refresh subscription status on session restore");
        }
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
            _logger.LogDebug(ex, "Could not restore subscription purchases");
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
