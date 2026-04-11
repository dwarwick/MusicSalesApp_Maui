using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.ViewModels;

[QueryProperty(nameof(UserId), "UserId")]
[QueryProperty(nameof(Email), "Email")]
public partial class ResetPasswordViewModel : ObservableObject
{
    private readonly IAuthService _authService;
    private readonly IAlertService _alertService;
    private readonly INavigationService _navigationService;

    [ObservableProperty]
    public partial int UserId { get; set; }

    [ObservableProperty]
    public partial string Email { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Code { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NewPassword { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ConfirmPassword { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    public ResetPasswordViewModel(IAuthService authService, IAlertService alertService, INavigationService navigationService)
    {
        _authService = authService;
        _alertService = alertService;
        _navigationService = navigationService;
    }

    [RelayCommand]
    private async Task ResetPasswordAsync()
    {
        if (string.IsNullOrWhiteSpace(Code) || Code.Trim().Length != 6)
        {
            ErrorMessage = "Please enter the 6-digit code from your email.";
            return;
        }

        if (string.IsNullOrWhiteSpace(NewPassword))
        {
            ErrorMessage = "Please enter a new password.";
            return;
        }

        if (NewPassword != ConfirmPassword)
        {
            ErrorMessage = "Passwords do not match.";
            return;
        }

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            var (success, message) = await _authService.ResetPasswordAsync(UserId, Code.Trim(), NewPassword);

            if (success)
            {
                await _alertService.DisplayAlertAsync("Success", "Your password has been reset. Please log in.", "OK");
                // Navigate back to login (pop the reset + forgot password pages)
                await _navigationService.GoToAsync("//MusicLibrary/login");
            }
            else
            {
                ErrorMessage = message ?? "Password reset failed.";
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

        try
        {
            var (success, message, _) = await _authService.ForgotPasswordAsync(Email);
            if (success)
            {
                await _alertService.DisplayAlertAsync("Code Sent", "A new code has been sent to your email.", "OK");
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
}
