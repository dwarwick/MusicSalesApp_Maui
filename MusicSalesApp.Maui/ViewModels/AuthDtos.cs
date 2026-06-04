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

public class GoogleAuthResultDto
{
    public bool Success { get; set; }
    public bool RequiresRegistration { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public string PendingRegistrationToken { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
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
    public string SubscriptionPrice { get; set; } = string.Empty;
}

public class ForgotPasswordResponseDto
{
    public int UserId { get; set; }
    public string Message { get; set; } = string.Empty;
}
