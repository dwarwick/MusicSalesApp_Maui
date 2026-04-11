using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Services;

public interface IAuthService
{
    /// <summary>Raised when login/logout state changes.</summary>
    event Action? AuthStateChanged;

    bool IsLoggedIn { get; }
    int? UserId { get; }
    string? Email { get; }
    bool EmailConfirmed { get; }
    bool HasActiveSubscription { get; }
    bool IsCreator { get; }
    int? CreatorId { get; }
    IReadOnlyList<string> Roles { get; }
    string? Token { get; }

    /// <summary>Whether biometric credentials have been saved previously.</summary>
    bool IsBiometricEnabled { get; }

    Task<(bool Success, string Error)> LoginAsync(string email, string password);
    Task<(bool Success, string Error, int UserId)> RegisterAsync(string email, string password);
    Task<(bool Success, string Error, LoginResponseDto? LoginData)> VerifyCodeAsync(int userId, string code);
    Task<(bool Success, string Error)> ResendCodeAsync(int userId);
    Task<(bool Success, string Error)> ChangeEmailAsync(int userId, string newEmail);
    Task<(bool Success, string Error, int UserId)> ForgotPasswordAsync(string email);
    Task<(bool Success, string Error)> ResetPasswordAsync(int userId, string code, string newPassword);
    Task LogoutAsync();

    /// <summary>Restore session from SecureStorage on app startup.</summary>
    Task TryRestoreSessionAsync();

    /// <summary>Store credentials encrypted for biometric re-login.</summary>
    Task EnableBiometricLoginAsync(string email, string password);
    Task DisableBiometricLoginAsync();

    /// <summary>Retrieve stored credentials after biometric prompt and re-login.</summary>
    Task<(bool Success, string Error)> BiometricLoginAsync();
}
