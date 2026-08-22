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

    /// <summary>
    /// Whether this device offers biometric sign-in at all. Resolved in <see cref="InitializeAsync"/>
    /// from the device rather than assumed from the platform, so a phone with nothing enrolled does
    /// not get a button that can only fail.
    /// </summary>
    [ObservableProperty]
    public partial bool IsBiometricLoginSupported { get; set; }

    /// <summary>What to call it: "Face ID", "Touch ID", or "your fingerprint or face" on Android.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BiometricSignInDescription))]
    public partial string BiometricMethodName { get; set; } = BiometricAvailability.Unavailable.DisplayName;

    /// <summary>
    /// The screen-reader label for the biometric button, which is otherwise an unlabelled glyph.
    /// Follows the "Continue with Google" description on the same page.
    /// </summary>
    public string BiometricSignInDescription => $"Sign in with {BiometricMethodName}";

    /// <summary>
    /// The button's glyph. Touch ID is a fingerprint, so it and Android share one asset; Face ID
    /// gets its own, which is the whole reason this is bound rather than an OnPlatform swap - an
    /// iPhone SE would otherwise be told to look at a camera it does not use this way.
    /// </summary>
    [ObservableProperty]
    public partial string BiometricIconSource { get; set; } = BiometricIcons.Fingerprint;

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    public bool ReturnToHomeAfterAuth { get; private set; }

    public LoginViewModel(IAuthService authService, IAlertService alertService, INavigationService navigationService)
    {
        _authService = authService;
        _alertService = alertService;
        _navigationService = navigationService;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var availability = await _authService.GetBiometricAvailabilityAsync();
        ApplyBiometricAvailability(availability);

        BiometricVisible = IsBiometricLoginSupported
            && await _authService.HasBiometricCredentialsAsync(cancellationToken);
    }

    private void ApplyBiometricAvailability(BiometricAvailability availability)
    {
        IsBiometricLoginSupported = availability.IsAvailable;
        BiometricMethodName = availability.DisplayName;
        BiometricIconSource = BiometricIcons.For(availability.Method);
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
        // The offer is only honest where a prompt would actually appear. Accepting it on a device
        // with no biometrics would save the credentials and show a button that fails on every tap.
        // InitializeAsync has normally answered this already; a password login on a screen that was
        // never initialised has not, so ask.
        var availability = await _authService.GetBiometricAvailabilityAsync();
        ApplyBiometricAvailability(availability);

        if (!IsBiometricLoginSupported || await _authService.HasBiometricCredentialsAsync())
            return;

        bool enable = await _alertService.ShowConfirmAsync(
            $"Sign In With {availability.ShortName}",
            $"Would you like to use {availability.DisplayName} to sign in next time?",
            "Yes", "No");

        if (enable)
        {
            await _authService.EnableBiometricLoginAsync(Email.Trim(), Password);
            BiometricVisible = true;
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
