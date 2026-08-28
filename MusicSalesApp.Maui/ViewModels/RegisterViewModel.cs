using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicSalesApp.Common.Helpers;
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
    [NotifyPropertyChangedFor(nameof(IsExternalRegistrationPending))]
    [NotifyPropertyChangedFor(nameof(RegisterButtonText))]
    [NotifyPropertyChangedFor(nameof(ExternalRegistrationPrompt))]
    public partial string PendingExternalRegistrationToken { get; set; } = string.Empty;

    /// <summary>
    /// Which provider the pending token was minted for - "Google" or "Apple". Only affects the
    /// wording on screen and which completion endpoint is called; the flow is identical.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RegisterButtonText))]
    [NotifyPropertyChangedFor(nameof(ExternalRegistrationPrompt))]
    public partial string PendingExternalProvider { get; set; } = ExternalLoginProviders.Google;

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
    public bool IsExternalRegistrationPending => !string.IsNullOrWhiteSpace(PendingExternalRegistrationToken);
    public string RegisterButtonText => IsExternalRegistrationPending
        ? $"Complete {PendingExternalProvider} Sign Up"
        : "Register";

    public string ExternalRegistrationPrompt =>
        $"Finish creating your {PendingExternalProvider} account by accepting the policies below.";
    public bool ReturnToHomeAfterAuth { get; private set; }

    /// <summary>
    /// Apple sign-in exists only on iOS, so the button is hidden rather than shown disabled.
    /// </summary>
    public bool IsAppleSignInVisible => _authService.IsAppleSignInSupported;

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

        if (IsExternalRegistrationPending)
        {
            IsBusy = true;
            ErrorMessage = null;

            try
            {
                var (success, message) = await CompleteExternalRegistrationAsync(
                    PendingExternalProvider,
                    PendingExternalRegistrationToken);

                if (success)
                {
                    PendingExternalRegistrationToken = string.Empty;
                    await NavigateAfterAuthAsync();
                }
                else
                {
                    ErrorMessage = message ?? $"{PendingExternalProvider} registration failed.";
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
                    ["CodeAlreadySent"] = true,
                    [NavigationRoutes.ReturnToHomeAfterAuthParameter] = ReturnToHomeAfterAuth
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

    /// <summary>
    /// Routes the pending token to the endpoint that minted it. An unrecognised provider fails
    /// here rather than defaulting to Google, because posting an Apple token to the Google
    /// endpoint surfaces as an opaque "invalid token" instead of naming the real problem.
    /// </summary>
    private Task<(bool Success, string Error)> CompleteExternalRegistrationAsync(string provider, string token)
        => provider switch
        {
            ExternalLoginProviders.Apple => _authService.CompleteAppleRegistrationAsync(
                token, AcceptTermsOfUse, AcceptPrivacyPolicy, AcceptRefundPolicy),
            ExternalLoginProviders.Google => _authService.CompleteGoogleRegistrationAsync(
                token, AcceptTermsOfUse, AcceptPrivacyPolicy, AcceptRefundPolicy),
            _ => Task.FromResult((false, $"Unsupported sign-in provider '{provider}'."))
        };

    [RelayCommand]
    private async Task RegisterWithAppleAsync()
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
            var result = await _authService.AuthenticateWithAppleAsync();
            if (result.WasCancelled)
            {
                return;
            }

            if (result.Success)
            {
                await NavigateAfterAuthAsync();
                return;
            }

            // Consent was already given on this page, so finish inline rather than bouncing the
            // user to a second acceptance screen.
            if (result.RequiresRegistration && !string.IsNullOrWhiteSpace(result.PendingRegistrationToken))
            {
                var (success, message) = await _authService.CompleteAppleRegistrationAsync(
                    result.PendingRegistrationToken,
                    AcceptTermsOfUse,
                    AcceptPrivacyPolicy,
                    AcceptRefundPolicy);

                if (success)
                {
                    await NavigateAfterAuthAsync();
                    return;
                }

                ErrorMessage = message ?? "Apple registration failed.";
                return;
            }

            ErrorMessage = string.IsNullOrWhiteSpace(result.ErrorMessage)
                ? "Apple sign-in failed."
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
            if (result.WasCancelled)
            {
                return;
            }

            if (result.Success)
            {
                await NavigateAfterAuthAsync();
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
                    await NavigateAfterAuthAsync();
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
        if (ReturnToHomeAfterAuth)
        {
            await _navigationService.GoToAsync(NavigationRoutes.LoginEntry, new Dictionary<string, object>
            {
                [NavigationRoutes.ReturnToHomeAfterAuthParameter] = true
            });
            return;
        }

        await _navigationService.GoToAsync(NavigationRoutes.LoginEntry);
    }

    private string BuildWebUrl(string relativePath)
        => $"{_appConfig.WebBaseUrl.TrimEnd('/')}/{relativePath.TrimStart('/')}";

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        ReturnToHomeAfterAuth = false;

        if (query.TryGetValue("PendingExternalRegistrationToken", out var pendingToken) && pendingToken is string token)
        {
            PendingExternalProvider = query.TryGetValue("PendingExternalProvider", out var provider) && provider is string providerValue
                ? providerValue
                : ExternalLoginProviders.Google;
            PendingExternalRegistrationToken = token;
            Password = string.Empty;
            ConfirmPassword = string.Empty;
        }

        if (query.TryGetValue("Email", out var email) && email is string emailValue)
        {
            Email = emailValue;
        }

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
