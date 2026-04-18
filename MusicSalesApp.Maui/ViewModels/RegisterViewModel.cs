using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.ViewModels;

public partial class RegisterViewModel : ObservableObject
{
    private readonly IAuthService _authService;
    private readonly INavigationService _navigationService;
    private readonly IAppConfig _appConfig;

    [ObservableProperty]
    public partial string Email { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Password { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ConfirmPassword { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRegister))]
    public partial bool AcceptTermsOfUse { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRegister))]
    public partial bool AcceptPrivacyPolicy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRegister))]
    public partial bool AcceptRefundPolicy { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    public bool CanRegister => AcceptTermsOfUse && AcceptPrivacyPolicy && AcceptRefundPolicy;

    public RegisterViewModel(IAuthService authService, INavigationService navigationService, IAppConfig appConfig)
    {
        _authService = authService;
        _navigationService = navigationService;
        _appConfig = appConfig;
    }

    [RelayCommand]
    private async Task OpenTermsOfUseAsync()
    {
        await _navigationService.GoToAsync("policy", new Dictionary<string, object>
        {
            ["title"] = "Terms of Use",
            ["path"] = "/terms-of-use"
        });
    }

    [RelayCommand]
    private async Task OpenPrivacyPolicyAsync()
    {
        await _navigationService.GoToAsync("policy", new Dictionary<string, object>
        {
            ["title"] = "Privacy Policy",
            ["path"] = "/privacy-policy"
        });
    }

    [RelayCommand]
    private async Task OpenRefundPolicyAsync()
    {
        await _navigationService.GoToAsync("policy", new Dictionary<string, object>
        {
            ["title"] = "User Refund Policy",
            ["path"] = "/user-refund-policy"
        });
    }

    [RelayCommand]
    private async Task RegisterAsync()
    {
        if (!AcceptTermsOfUse || !AcceptPrivacyPolicy || !AcceptRefundPolicy)
        {
            ErrorMessage = "You must accept the Terms of Use, Privacy Policy, and Refund Policy to register.";
            return;
        }

        if (string.IsNullOrWhiteSpace(Email))
        {
            ErrorMessage = "Please enter your email.";
            return;
        }

        if (string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "Please enter a password.";
            return;
        }

        if (Password != ConfirmPassword)
        {
            ErrorMessage = "Passwords do not match.";
            return;
        }

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            var (success, message, userId) = await _authService.RegisterAsync(Email.Trim(), Password);

            if (success && userId != 0)
            {
                var parameters = new Dictionary<string, object>
                {
                    ["UserId"] = userId,
                    ["Email"] = Email.Trim(),
                    ["Password"] = Password
                };
                await _navigationService.GoToAsync("verify-email", parameters);
            }
            else
            {
                ErrorMessage = message ?? "Registration failed.";
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
    private async Task GoToLoginAsync()
    {
        await _navigationService.GoToAsync("..");
    }
}
