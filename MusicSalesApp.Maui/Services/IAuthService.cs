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
    bool IsValidatedUser { get; }
    bool HasActiveSubscription { get; }
    string? SubscriptionStatus { get; }
    DateTime? SubscriptionEndDate { get; }
    bool IsOnTrial { get; }
    DateTime? TrialEndDate { get; }
    string? BillingSource { get; }

    /// <summary>
    /// Whether the subscription fields above were confirmed by the server this session, are
    /// standing on a cached snapshot, or could not be established at all.
    /// </summary>
    SubscriptionVerificationState SubscriptionVerification { get; }
    bool IsCreator { get; }
    int? CreatorId { get; }

    /// <summary>Whether the signed-in user holds the Admin role (full playback access, same as the web app).</summary>
    bool IsAdmin { get; }
    IReadOnlyList<string> Roles { get; }
    string? Token { get; }

    /// <summary>Whether both biometric login credentials have been saved previously.</summary>
    Task<bool> HasBiometricCredentialsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The standing explanation for a session that ended without the user asking. Null when the
    /// session ended some other way — an explicit logout, or a first launch with nothing stored.
    ///
    /// Readable rather than read-once on purpose: the home screen is transient and rebuilt from a
    /// Shell <c>DataTemplate</c>, so a consuming read could be taken by an off-screen instance the
    /// user never returns to, and the explanation would vanish. Signing in clears it instead, which
    /// is the only event that actually resolves it.
    /// </summary>
    SessionExpiryNotice? PendingSessionExpiryNotice { get; }

    Task<(bool Success, string Error)> LoginAsync(string email, string password);
    Task<GoogleAuthResultDto> AuthenticateWithGoogleAsync();
    Task<(bool Success, string Error)> CompleteGoogleRegistrationAsync(string pendingRegistrationToken,
        bool acceptTermsOfUse, bool acceptPrivacyPolicy, bool acceptRefundPolicy);
    Task<(bool Success, string Error, int UserId)> RegisterAsync(string email, string password);
    Task<(bool Success, string Error, LoginResponseDto? LoginData)> VerifyCodeAsync(int userId, string code);
    Task<(bool Success, string Error)> ResendCodeAsync(int userId);
    Task<(bool Success, string Error)> ChangeEmailAsync(int userId, string newEmail);
    Task<(bool Success, string Error, int UserId)> ForgotPasswordAsync(string email);
    /// <summary>
    /// <paramref name="email"/> is the account being reset, which is not necessarily the account
    /// whose credentials are saved for biometric login — the forgot-password flow is reachable from
    /// the login screen with no session at all. It is used to decide whether the saved password has
    /// just been invalidated, so one person's reset cannot wipe another's fingerprint sign-in.
    /// </summary>
    Task<(bool Success, string Error)> ResetPasswordAsync(int userId, string code, string newPassword, string email);
    Task LogoutAsync();

    /// <summary>Restore session from SecureStorage on app startup.</summary>
    Task TryRestoreSessionAsync();

    /// <summary>Store credentials encrypted for biometric re-login.</summary>
    Task EnableBiometricLoginAsync(string email, string password);
    Task DisableBiometricLoginAsync();

    /// <summary>Retrieve stored credentials after biometric prompt and re-login.</summary>
    Task<(bool Success, string Error)> BiometricLoginAsync();

    /// <summary>Re-fetch subscription and creator status from the server.</summary>
    Task RefreshUserStatusAsync();

    /// <summary>Delete the user's account on the server and clear local session.</summary>
    Task<(bool Success, string Error)> DeleteAccountAsync();
}
