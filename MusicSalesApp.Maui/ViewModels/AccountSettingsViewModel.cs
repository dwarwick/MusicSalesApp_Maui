using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.ViewModels;

public partial class AccountSettingsViewModel : ObservableObject
{
    private readonly IAuthService _authService;
    private readonly IAlertService _alertService;
    private readonly INavigationService _navigationService;
    private readonly IMusicService _musicService;

    [ObservableProperty]
    public partial string UserEmail { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCancelSubscription))]
    [NotifyPropertyChangedFor(nameof(CanDeleteAccount))]
    public partial bool HasActiveSubscription { get; set; }

    [ObservableProperty]
    public partial bool IsCancelling { get; set; }

    [ObservableProperty]
    public partial bool IsDeleting { get; set; }

    [ObservableProperty]
    public partial bool ShowDeleteConfirmation { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirmDelete))]
    public partial string ConfirmationText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;

    public bool ShowCancelSubscription => HasActiveSubscription;
    public bool CanDeleteAccount => !HasActiveSubscription;
    public bool CanConfirmDelete => string.Equals(ConfirmationText?.Trim(), "DELETE", StringComparison.OrdinalIgnoreCase);

    public AccountSettingsViewModel(
        IAuthService authService,
        IAlertService alertService,
        INavigationService navigationService,
        IMusicService musicService)
    {
        _authService = authService;
        _alertService = alertService;
        _navigationService = navigationService;
        _musicService = musicService;

        _authService.AuthStateChanged += OnAuthStateChanged;
        RefreshState();
    }

    [RelayCommand]
    private async Task CancelSubscriptionAsync()
    {
        var confirmed = await _alertService.ShowConfirmAsync(
            "Cancel Subscription",
            "Are you sure you want to cancel your subscription? You will still have access until the end of your current billing period. " +
            "Any custom playlists you have created will be deleted at the end of your subscription term.",
            "Cancel Subscription",
            "Keep Subscription");

        if (!confirmed) return;

        IsCancelling = true;
        try
        {
            var (success, endDate) = await _musicService.CancelSubscriptionAsync();

            if (success)
            {
                await _authService.RefreshUserStatusAsync();
                RefreshState();

                var message = endDate.HasValue
                    ? $"Your subscription has been cancelled. You can enjoy music until {endDate.Value.ToLocalTime():MMMM dd, yyyy}."
                    : "Your subscription has been cancelled.";
                await _alertService.DisplayAlertAsync("Subscription Cancelled", message, "OK");
            }
            else
            {
                await _alertService.DisplayAlertAsync("Error", "Failed to cancel subscription. Please try again.", "OK");
            }
        }
        catch (Exception ex)
        {
            await _alertService.DisplayAlertAsync("Error", $"Failed to cancel subscription: {ex.Message}", "OK");
        }
        finally
        {
            IsCancelling = false;
        }
    }

    [RelayCommand]
    private async Task ShowDeleteAccountPromptAsync()
    {
        ErrorMessage = string.Empty;

        if (HasActiveSubscription)
        {
            await _alertService.DisplayAlertAsync(
                "Active Subscription",
                "You must cancel your active subscription before deleting your account.",
                "OK");
            return;
        }

        var confirmed = await _alertService.ShowConfirmAsync(
            "Delete Account",
            "Warning: This will permanently delete your account.\n\n" +
            "• All your data including purchases, playlists, and subscriptions will be permanently deleted.\n" +
            "• Your custom playlists will be deleted immediately.\n" +
            "• You will no longer have access to your existing playlists if you create an account in the future.\n\n" +
            "This action cannot be undone!",
            "Continue",
            "Cancel");

        if (!confirmed) return;

        ShowDeleteConfirmation = true;
        ConfirmationText = string.Empty;
    }

    [RelayCommand]
    private async Task ConfirmDeleteAsync()
    {
        ErrorMessage = string.Empty;

        if (!CanConfirmDelete)
        {
            ErrorMessage = "Please type DELETE to confirm.";
            return;
        }

        IsDeleting = true;
        try
        {
            var (success, error) = await _authService.DeleteAccountAsync();

            if (success)
            {
                ShowDeleteConfirmation = false;
                await _alertService.DisplayAlertAsync(
                    "Account Deleted",
                    "Your account has been permanently deleted.",
                    "OK");
                await _navigationService.GoToAsync("//Home");
            }
            else
            {
                ErrorMessage = error;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to delete account: {ex.Message}";
        }
        finally
        {
            IsDeleting = false;
        }
    }

    [RelayCommand]
    private void CancelDelete()
    {
        ShowDeleteConfirmation = false;
        ConfirmationText = string.Empty;
        ErrorMessage = string.Empty;
    }

    private void RefreshState()
    {
        UserEmail = _authService.Email ?? string.Empty;
        HasActiveSubscription = _authService.HasActiveSubscription;
    }

    private void OnAuthStateChanged()
    {
        RefreshState();
    }
}
