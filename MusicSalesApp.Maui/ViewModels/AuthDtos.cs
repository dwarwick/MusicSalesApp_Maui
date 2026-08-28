namespace MusicSalesApp.Maui.ViewModels;

// --- Request DTOs ---

public class LoginRequestDto
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class RegisterRequestDto
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class GoogleExchangeRequestDto
{
    public string ExchangeToken { get; set; } = string.Empty;
}

public class GoogleRegisterRequestDto
{
    public string PendingRegistrationToken { get; set; } = string.Empty;
    public bool AcceptTermsOfUse { get; set; }
    public bool AcceptPrivacyPolicy { get; set; }
    public bool AcceptRefundPolicy { get; set; }
}

public class AppleTokenRequestDto
{
    public string IdentityToken { get; set; } = string.Empty;
    public string AuthorizationCode { get; set; } = string.Empty;

    // Apple supplies these on the first authorization only, so they are sent when present and
    // simply absent afterwards - the server keys off the identity token's subject either way.
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
}

public class AppleRegisterRequestDto
{
    public string PendingRegistrationToken { get; set; } = string.Empty;
    public bool AcceptTermsOfUse { get; set; }
    public bool AcceptPrivacyPolicy { get; set; }
    public bool AcceptRefundPolicy { get; set; }
}

public class VerifyCodeRequestDto
{
    public int UserId { get; set; }
    public string Code { get; set; } = string.Empty;
}

public class ResendCodeRequestDto
{
    public int UserId { get; set; }
}

public class ForgotPasswordRequestDto
{
    public string Email { get; set; } = string.Empty;
}

public class ResetPasswordRequestDto
{
    public int UserId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

public class ChangeEmailRequestDto
{
    public int UserId { get; set; }
    public string NewEmail { get; set; } = string.Empty;
}

// --- Response DTOs ---

public class LoginResponseDto
{
    public string Token { get; set; } = string.Empty;
    public int UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = [];
    public bool EmailConfirmed { get; set; }
    public bool HasActiveSubscription { get; set; }
    public bool IsOnTrial { get; set; }
    public string? SubscriptionStatus { get; set; }
    public DateTime? SubscriptionEndDate { get; set; }
    public DateTime? TrialEndDate { get; set; }
    public string? BillingSource { get; set; }
    public bool IsCreator { get; set; }
    public int? CreatorId { get; set; }
}

public class RegisterResponseDto
{
    public int UserId { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class AppleTokenResponseDto
{
    public bool RequiresRegistration { get; set; }
    public string PendingRegistrationToken { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    /// <summary>Populated only when <see cref="RequiresRegistration"/> is false.</summary>
    public LoginResponseDto? Login { get; set; }
}

/// <summary>
/// The outcome of an external sign-in attempt, shared by Google and Apple - the wire shapes
/// differ but the three outcomes the UI cares about (signed in / needs to accept policies /
/// failed) do not.
/// </summary>
public class ExternalAuthResultDto
{
    public bool Success { get; set; }
    public bool RequiresRegistration { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public string PendingRegistrationToken { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// True when the user dismissed the provider's sheet. Distinct from a failure: the UI shows
    /// nothing at all rather than an error banner.
    /// </summary>
    public bool WasCancelled { get; set; }
}

public class ApiMessageResponse
{
    public string Message { get; set; } = string.Empty;
}

public class SubscriptionStatusDto
{
    public bool HasSubscription { get; set; }
    public bool IsOnTrial { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public DateTime? NextBillingDate { get; set; }
    public DateTime? TrialStartDate { get; set; }
    public DateTime? TrialEndDate { get; set; }
    public DateTime? TrialConvertedAt { get; set; }
    public decimal MonthlyPrice { get; set; }
    public string PaypalSubscriptionId { get; set; } = string.Empty;
    public string BillingSource { get; set; } = string.Empty;
    public bool IsSubscriptionBlocked { get; set; }

    // Nullable on purpose. A server that predates creator status on this endpoint - or one that has
    // been rolled back - omits these fields, and a non-nullable bool would silently deserialize to
    // false and revoke creator status for everyone. Null means "this server did not say", which is
    // not the same as "not a creator".
    public bool? IsCreator { get; set; }
    public int? CreatorId { get; set; }
}

public class ForgotPasswordResponseDto
{
    public int UserId { get; set; }
    public string Message { get; set; } = string.Empty;
}
