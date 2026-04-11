using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.ViewModels;

public partial class ForgotPasswordViewModel : ObservableObject
{
    private readonly IAuthService _authService;
    private readonly INavigationService _navigationService;

    [ObservableProperty]
    public partial string Email { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    public ForgotPasswordViewModel(IAuthService authService, INavigationService navigationService)
    {
        _authService = authService;
        _navigationService = navigationService;
    }

    [RelayCommand]
    private async Task SendResetCodeAsync()
    {
        if (string.IsNullOrWhiteSpace(Email))
        {
            ErrorMessage = "Please enter your email.";
            return;
        }

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            var (success, message, userId) = await _authService.ForgotPasswordAsync(Email.Trim());

            if (success && userId != 0)
            {
                var parameters = new Dictionary<string, object>
                {
                    ["UserId"] = userId,
                    ["Email"] = Email.Trim()
                };
                await _navigationService.GoToAsync("reset-password", parameters);
            }
            else
            {
                ErrorMessage = message ?? "Could not send reset code.";
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
    private async Task GoBackAsync()
    {
        await _navigationService.GoToAsync("..");
    }
}
