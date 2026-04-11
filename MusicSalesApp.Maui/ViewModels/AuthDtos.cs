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
    public bool IsCreator { get; set; }
    public int? CreatorId { get; set; }
}

public class RegisterResponseDto
{
    public int UserId { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class ApiMessageResponse
{
    public string Message { get; set; } = string.Empty;
}

public class ForgotPasswordResponseDto
{
    public int UserId { get; set; }
    public string Message { get; set; } = string.Empty;
}
