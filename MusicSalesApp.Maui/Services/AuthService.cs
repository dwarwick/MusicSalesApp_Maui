using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.Extensions.Logging;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Services;

public class AuthService : IAuthService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AuthService> _logger;

    private const string TokenStorageKey = "auth_token";
    private const string UserIdStorageKey = "auth_user_id";
    private const string EmailStorageKey = "auth_email";
    private const string EmailConfirmedStorageKey = "auth_email_confirmed";
    private const string BioEmailKey = "bio_email";
    private const string BioPasswordKey = "bio_password";

    public event Action? AuthStateChanged;

    public bool IsLoggedIn { get; private set; }
    public int? UserId { get; private set; }
    public string? Email { get; private set; }
    public bool EmailConfirmed { get; private set; }
    public bool HasActiveSubscription { get; private set; }
    public bool IsCreator { get; private set; }
    public int? CreatorId { get; private set; }
    public IReadOnlyList<string> Roles { get; private set; } = [];
    public string? Token { get; private set; }
    public bool IsBiometricEnabled => SecureStorage.Default.GetAsync(BioEmailKey).GetAwaiter().GetResult() != null;

    public AuthService(IHttpClientFactory httpClientFactory, ILogger<AuthService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
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
            await SecureStorage.Default.SetAsync(EmailStorageKey, newEmail);
            AuthStateChanged?.Invoke();

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
        SecureStorage.Default.Remove(TokenStorageKey);
        SecureStorage.Default.Remove(UserIdStorageKey);
        SecureStorage.Default.Remove(EmailStorageKey);
        SecureStorage.Default.Remove(EmailConfirmedStorageKey);
        ClearState();
        await Task.CompletedTask;
        AuthStateChanged?.Invoke();
    }

    public async Task TryRestoreSessionAsync()
    {
        try
        {
            var token = await SecureStorage.Default.GetAsync(TokenStorageKey);
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
            var storedEmailConfirmed = await SecureStorage.Default.GetAsync(EmailConfirmedStorageKey);
            EmailConfirmed = string.Equals(storedEmailConfirmed, "true", StringComparison.OrdinalIgnoreCase);

            // Refresh subscription/creator status from server
            await RefreshUserStatusAsync();
            AuthStateChanged?.Invoke();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to restore session");
            await LogoutAsync();
        }
    }

    public async Task EnableBiometricLoginAsync(string email, string password)
    {
        await SecureStorage.Default.SetAsync(BioEmailKey, email);
        await SecureStorage.Default.SetAsync(BioPasswordKey, password);
    }

    public async Task DisableBiometricLoginAsync()
    {
        SecureStorage.Default.Remove(BioEmailKey);
        SecureStorage.Default.Remove(BioPasswordKey);
        await Task.CompletedTask;
    }

    public async Task<(bool Success, string Error)> BiometricLoginAsync()
    {
        var email = await SecureStorage.Default.GetAsync(BioEmailKey);
        var password = await SecureStorage.Default.GetAsync(BioPasswordKey);

        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            return (false, "No saved credentials. Please log in with your password first.");

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
        Token = data.Token;
        UserId = data.UserId;
        Email = data.Email;
        Roles = data.Roles;
        EmailConfirmed = data.EmailConfirmed;
        HasActiveSubscription = data.HasActiveSubscription;
        IsCreator = data.IsCreator;
        CreatorId = data.CreatorId;
        IsLoggedIn = true;

        await SecureStorage.Default.SetAsync(TokenStorageKey, data.Token);
        await SecureStorage.Default.SetAsync(UserIdStorageKey, data.UserId.ToString());
        await SecureStorage.Default.SetAsync(EmailStorageKey, data.Email);
        await SecureStorage.Default.SetAsync(EmailConfirmedStorageKey, data.EmailConfirmed.ToString());

        AuthStateChanged?.Invoke();
    }

    private void ClearState()
    {
        Token = null;
        UserId = null;
        Email = null;
        EmailConfirmed = false;
        HasActiveSubscription = false;
        IsCreator = false;
        CreatorId = null;
        Roles = [];
        IsLoggedIn = false;
    }

    private async Task RefreshUserStatusAsync()
    {
        try
        {
            var client = _httpClientFactory.CreateClient("MusicSalesApi");
            // Add auth header manually for this one-off call
            if (!string.IsNullOrEmpty(Token))
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Token);

            var response = await client.GetFromJsonAsync<SubscriptionStatusDto>("api/subscription/status");
            HasActiveSubscription = response?.HasSubscription ?? false;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not refresh subscription status on session restore");
        }
    }

    private static async Task<string> ReadErrorMessageAsync(HttpResponseMessage response)
    {
        try
        {
            var error = await response.Content.ReadFromJsonAsync<ApiMessageResponse>();
            return error?.Message ?? $"Request failed ({(int)response.StatusCode}).";
        }
        catch
        {
            return $"Request failed ({(int)response.StatusCode}).";
        }
    }

    private sealed record SubscriptionStatusDto(bool HasSubscription);
}
