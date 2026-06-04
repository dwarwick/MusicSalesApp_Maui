using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    private readonly IAuthService _authService;
    private readonly IAlertService _alertService;
    private readonly INavigationService _navigationService;

    [ObservableProperty]
    public partial string Email { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Password { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool BiometricVisible { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    public bool ReturnToHomeAfterAuth { get; private set; }

    public LoginViewModel(IAuthService authService, IAlertService alertService, INavigationService navigationService)
    {
        _authService = authService;
        _alertService = alertService;
        _navigationService = navigationService;
        BiometricVisible = _authService.IsBiometricEnabled;
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "Please enter your email and password.";
            return;
        }

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            var (success, message) = await _authService.LoginAsync(Email.Trim(), Password);

            if (success)
            {
                if (!_authService.EmailConfirmed)
                {
                    // Navigate to verification page for non-validated users
                    await _navigationService.GoToAsync("verify-email", new Dictionary<string, object>
                    {
                        ["UserId"] = _authService.UserId ?? 0,
                        ["Email"] = _authService.Email ?? Email.Trim(),
                        ["Password"] = Password,
                        [NavigationRoutes.ReturnToHomeAfterAuthParameter] = ReturnToHomeAfterAuth
                    });
                }
                else
                {
                    await PromptBiometricAsync();
                    await NavigateAfterAuthAsync();
                }
            }
            else
            {
                ErrorMessage = message ?? "Login failed.";
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
    private async Task BiometricLoginAsync()
    {
        IsBusy = true;
        ErrorMessage = null;

        try
        {
            var (success, message) = await _authService.BiometricLoginAsync();

            if (success)
            {
                if (!_authService.EmailConfirmed)
                {
                    await _navigationService.GoToAsync("verify-email", new Dictionary<string, object>
                    {
                        ["UserId"] = _authService.UserId ?? 0,
                        ["Email"] = _authService.Email ?? string.Empty,
                        ["Password"] = string.Empty,
                        [NavigationRoutes.ReturnToHomeAfterAuthParameter] = ReturnToHomeAfterAuth
                    });
                }
                else
                {
                    await NavigateAfterAuthAsync();
                }
            }
            else
            {
                ErrorMessage = message ?? "Biometric login failed.";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Biometric error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task GoogleLoginAsync()
    {
        IsBusy = true;
        ErrorMessage = null;

        try
        {
            var result = await _authService.AuthenticateWithGoogleAsync();
            if (result.Success)
            {
                await NavigateAfterAuthAsync();
                return;
            }

            if (result.RequiresRegistration && !string.IsNullOrWhiteSpace(result.PendingRegistrationToken))
            {
                await _navigationService.GoToAsync("register", new Dictionary<string, object>
                {
                    ["PendingGoogleRegistrationToken"] = result.PendingRegistrationToken,
                    ["Email"] = result.Email,
                    [NavigationRoutes.ReturnToHomeAfterAuthParameter] = ReturnToHomeAfterAuth
                });
                return;
            }

            ErrorMessage = string.IsNullOrWhiteSpace(result.ErrorMessage)
                ? "Google sign-in failed."
                : result.ErrorMessage;
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
    private async Task GoToRegisterAsync()
    {
        if (ReturnToHomeAfterAuth)
        {
            await _navigationService.GoToAsync("register", new Dictionary<string, object>
            {
                [NavigationRoutes.ReturnToHomeAfterAuthParameter] = true
            });
            return;
        }

        await _navigationService.GoToAsync("register");
    }

    [RelayCommand]
    private async Task GoToForgotPasswordAsync()
    {
        await _navigationService.GoToAsync("forgot-password");
    }

    private async Task PromptBiometricAsync()
    {
        if (_authService.IsBiometricEnabled)
            return;

        bool enable = await _alertService.ShowConfirmAsync(
            "Biometric Login",
            "Would you like to enable biometric login for next time?",
            "Yes", "No");

        if (enable)
        {
            await _authService.EnableBiometricLoginAsync(Email.Trim(), Password);
        }
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        ReturnToHomeAfterAuth = false;

        if (NavigationQueryHelper.TryReadBoolean(query, NavigationRoutes.ReturnToHomeAfterAuthParameter, out var returnToHomeAfterAuth))
        {
            ReturnToHomeAfterAuth = returnToHomeAfterAuth;
        }
    }

    private Task NavigateAfterAuthAsync()
        => _navigationService.GoToAsync(ReturnToHomeAfterAuth
            ? NavigationRoutes.HomeRoot
            : NavigationRoutes.MusicLibraryRoot);

}
