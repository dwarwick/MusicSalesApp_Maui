using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.ViewModels;

[QueryProperty(nameof(UserId), "UserId")]
[QueryProperty(nameof(Email), "Email")]
[QueryProperty(nameof(Password), "Password")]
[QueryProperty(nameof(CodeAlreadySent), "CodeAlreadySent")]
[QueryProperty(nameof(ReturnToHomeAfterAuth), NavigationRoutes.ReturnToHomeAfterAuthParameter)]
public partial class VerifyEmailViewModel : ObservableObject
{
    private readonly IAuthService _authService;
    private readonly IAlertService _alertService;
    private readonly INavigationService _navigationService;

    [ObservableProperty]
    public partial int UserId { get; set; }

    [ObservableProperty]
    public partial string Email { get; set; } = string.Empty;

    /// <summary>
    /// Password passed from RegisterPage so we can auto-login and offer biometric after verification.
    /// </summary>
    [ObservableProperty]
    public partial string Password { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Code { get; set; } = string.Empty;

    /// <summary>
    /// When true, a verification code was already sent (e.g. during registration),
    /// so OnAppearingAsync skips the redundant resend that would hit the cooldown.
    /// </summary>
    [ObservableProperty]
    public partial bool CodeAlreadySent { get; set; }

    [ObservableProperty]
    public partial bool ReturnToHomeAfterAuth { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    [ObservableProperty]
    public partial string NewEmail { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool ShowChangeEmail { get; set; }

    /// <summary>
    /// Whether this device offers biometric sign-in; see <see cref="PromptBiometricAsync"/>, which
    /// resolves it. An overridable property rather than an inline device check, matching
    /// <c>LoginViewModel</c> and <c>AccountSettingsViewModel</c> — this screen is one of only two
    /// places that can save biometric credentials, so the gate has to be reachable from a test.
    /// </summary>
    [ObservableProperty]
    public partial bool IsBiometricLoginSupported { get; set; }

    public string ChangeEmailToggleText => ShowChangeEmail ? "Cancel" : "Change Email";

    partial void OnShowChangeEmailChanged(bool value)
    {
        OnPropertyChanged(nameof(ChangeEmailToggleText));
    }

    public VerifyEmailViewModel(IAuthService authService, IAlertService alertService, INavigationService navigationService)
    {
        _authService = authService;
        _alertService = alertService;
        _navigationService = navigationService;
    }

    /// <summary>
    /// Called after query properties are set. Automatically sends a fresh verification code
    /// so the user always has a valid code regardless of how they reached this page.
    /// </summary>
    public async Task OnAppearingAsync()
    {
        if (UserId <= 0 || _hasAutoSent)
            return;

        _hasAutoSent = true;

        if (CodeAlreadySent)
        {
            StatusMessage = "A verification code has been sent to your email.";
            return;
        }

        try
        {
            var (success, _) = await _authService.ResendCodeAsync(UserId);
            if (success)
                StatusMessage = "A verification code has been sent to your email.";
        }
        catch
        {
            // Silently ignore — user can manually tap "Resend Code"
        }
    }

    private bool _hasAutoSent;

    [RelayCommand]
    private async Task VerifyAsync()
    {
        if (string.IsNullOrWhiteSpace(Code) || Code.Trim().Length != 6)
        {
            ErrorMessage = "Please enter the 6-digit code from your email.";
            return;
        }

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            var (success, message, _) = await _authService.VerifyCodeAsync(UserId, Code.Trim());

            if (success)
            {
                await PromptBiometricAsync();
                await NavigateAfterAuthAsync();
            }
            else
            {
                ErrorMessage = message ?? "Verification failed.";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Connection error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ResendCodeAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = null;

        try
        {
            var (success, message) = await _authService.ResendCodeAsync(UserId);
            if (success)
            {
                StatusMessage = "A new code has been sent to your email.";
            }
            else
            {
                ErrorMessage = message ?? "Could not resend code.";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Connection error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ToggleChangeEmail()
    {
        ShowChangeEmail = !ShowChangeEmail;
        if (ShowChangeEmail)
            NewEmail = Email;
    }

    [RelayCommand]
    private async Task ChangeEmailAsync()
    {
        if (string.IsNullOrWhiteSpace(NewEmail))
        {
            ErrorMessage = "Please enter a new email address.";
            return;
        }

        if (NewEmail.Trim().Equals(Email, StringComparison.OrdinalIgnoreCase))
        {
            ErrorMessage = "Please enter a different email address.";
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = null;

        try
        {
            var (success, message) = await _authService.ChangeEmailAsync(UserId, NewEmail.Trim());
            if (success)
            {
                Email = NewEmail.Trim();
                NewEmail = Email;
                ShowChangeEmail = false;
                StatusMessage = $"Email updated to {Email}. A new verification code has been sent.";
            }
            else
            {
                ErrorMessage = message ?? "Could not change email.";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Connection error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SkipAsync()
    {
        await NavigateAfterAuthAsync();
    }

    private Task NavigateAfterAuthAsync()
        => _navigationService.GoToAsync(ReturnToHomeAfterAuth
            ? NavigationRoutes.HomeRoot
            : NavigationRoutes.MusicLibraryRoot);

    private async Task PromptBiometricAsync()
    {
        // Same gate as the login screen: the offer must not appear where no prompt would, because
        // accepting it saves credentials the device could never be asked to unlock.
        var availability = await _authService.GetBiometricAvailabilityAsync();
        IsBiometricLoginSupported = availability.IsAvailable;

        if (!IsBiometricLoginSupported
            || string.IsNullOrEmpty(Password)
            || await _authService.HasBiometricCredentialsAsync())
            return;

        bool enable = await _alertService.ShowConfirmAsync(
            $"Sign In With {availability.ShortName}",
            $"Would you like to use {availability.DisplayName} to sign in next time?",
            "Yes", "No");

        if (enable)
        {
            await _authService.EnableBiometricLoginAsync(Email, Password);
        }
    }
}
