using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicSalesApp.Maui.Services;
using System.ComponentModel.DataAnnotations;

namespace MusicSalesApp.Maui.ViewModels;

public partial class RegisterViewModel : ObservableObject
{
    private readonly IAuthService _authService;
    private readonly IBrowserService _browserService;
    private readonly INavigationService _navigationService;
    private readonly IAppConfig _appConfig;

    [ObservableProperty]
    public partial string Email { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Password { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ConfirmPassword { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGoogleRegistrationPending))]
    [NotifyPropertyChangedFor(nameof(RegisterButtonText))]
    public partial string PendingGoogleRegistrationToken { get; set; } = string.Empty;

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
    public bool IsGoogleRegistrationPending => !string.IsNullOrWhiteSpace(PendingGoogleRegistrationToken);
    public string RegisterButtonText => IsGoogleRegistrationPending ? "Complete Google Sign Up" : "Register";

    public RegisterViewModel(
        IAuthService authService,
        IBrowserService browserService,
        INavigationService navigationService,
        IAppConfig appConfig)
    {
        _authService = authService;
        _browserService = browserService;
        _navigationService = navigationService;
        _appConfig = appConfig;
    }

    [RelayCommand]
    private async Task OpenTermsOfUseAsync()
    {
        await _browserService.OpenExternalAsync(BuildWebUrl("/terms-of-use"));
    }

    [RelayCommand]
    private async Task OpenPrivacyPolicyAsync()
    {
        await _browserService.OpenExternalAsync(BuildWebUrl("/privacy-policy"));
    }

    [RelayCommand]
    private async Task OpenRefundPolicyAsync()
    {
        await _browserService.OpenExternalAsync(BuildWebUrl("/user-refund-policy"));
    }

    [RelayCommand]
    private async Task RegisterAsync()
    {
        if (!AcceptTermsOfUse || !AcceptPrivacyPolicy || !AcceptRefundPolicy)
        {
            ErrorMessage = "You must accept the Terms of Use, Privacy Policy, and Refund Policy to register.";
            return;
        }

        if (IsGoogleRegistrationPending)
        {
            IsBusy = true;
            ErrorMessage = null;

            try
            {
                var (success, message) = await _authService.CompleteGoogleRegistrationAsync(
                    PendingGoogleRegistrationToken,
                    AcceptTermsOfUse,
                    AcceptPrivacyPolicy,
                    AcceptRefundPolicy);

                if (success)
                {
                    PendingGoogleRegistrationToken = string.Empty;
                    await _navigationService.GoToAsync("//MusicLibrary");
                }
                else
                {
                    ErrorMessage = message ?? "Google registration failed.";
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

            return;
        }

        if (string.IsNullOrWhiteSpace(Email))
        {
            ErrorMessage = "Please enter your email.";
            return;
        }

        var normalizedEmail = Email.Trim();
        if (!new EmailAddressAttribute().IsValid(normalizedEmail))
        {
            ErrorMessage = "Please enter a valid email address and retype it.";
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
        Email = normalizedEmail;

        try
        {
            var (success, message, userId) = await _authService.RegisterAsync(normalizedEmail, Password);

            if (success && userId != 0)
            {
                var parameters = new Dictionary<string, object>
                {
                    ["UserId"] = userId,
                    ["Email"] = normalizedEmail,
                    ["Password"] = Password,
                    ["CodeAlreadySent"] = true
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
    private async Task RegisterWithGoogleAsync()
    {
        if (!AcceptTermsOfUse || !AcceptPrivacyPolicy || !AcceptRefundPolicy)
        {
            ErrorMessage = "You must accept the Terms of Use, Privacy Policy, and Refund Policy to register.";
            return;
        }

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            var result = await _authService.AuthenticateWithGoogleAsync();
            if (result.Success)
            {
                await _navigationService.GoToAsync("//MusicLibrary");
                return;
            }

            if (result.RequiresRegistration && !string.IsNullOrWhiteSpace(result.PendingRegistrationToken))
            {
                var (success, message) = await _authService.CompleteGoogleRegistrationAsync(
                    result.PendingRegistrationToken,
                    AcceptTermsOfUse,
                    AcceptPrivacyPolicy,
                    AcceptRefundPolicy);

                if (success)
                {
                    await _navigationService.GoToAsync("//MusicLibrary");
                    return;
                }

                ErrorMessage = message ?? "Google registration failed.";
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
    private async Task GoToLoginAsync()
    {
        await _navigationService.GoToAsync("login");
    }

    private string BuildWebUrl(string relativePath)
        => $"{_appConfig.WebBaseUrl.TrimEnd('/')}/{relativePath.TrimStart('/')}";

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("PendingGoogleRegistrationToken", out var pendingToken) && pendingToken is string token)
        {
            PendingGoogleRegistrationToken = token;
            Password = string.Empty;
            ConfirmPassword = string.Empty;
        }

        if (query.TryGetValue("Email", out var email) && email is string emailValue)
        {
            Email = emailValue;
        }
    }
}
