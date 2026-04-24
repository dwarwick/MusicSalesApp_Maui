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
    private readonly IBillingService _billingService;

    [ObservableProperty]
    public partial string UserEmail { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCancelSubscription))]
    [NotifyPropertyChangedFor(nameof(CanCreateSubscription))]
    [NotifyPropertyChangedFor(nameof(CanDeleteAccount))]
    [NotifyPropertyChangedFor(nameof(SubscriptionStatusText))]
    [NotifyPropertyChangedFor(nameof(SubscriptionStatusMessage))]
    [NotifyPropertyChangedFor(nameof(ShowSubscriptionEndDate))]
    [NotifyPropertyChangedFor(nameof(SubscriptionEndDateText))]
    [NotifyPropertyChangedFor(nameof(SubscribeButtonText))]
    public partial bool HasActiveSubscription { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCancelSubscription))]
    [NotifyPropertyChangedFor(nameof(CanCreateSubscription))]
    [NotifyPropertyChangedFor(nameof(CanDeleteAccount))]
    [NotifyPropertyChangedFor(nameof(SubscriptionStatusText))]
    [NotifyPropertyChangedFor(nameof(SubscriptionStatusMessage))]
    [NotifyPropertyChangedFor(nameof(ShowSubscriptionEndDate))]
    [NotifyPropertyChangedFor(nameof(SubscriptionEndDateText))]
    [NotifyPropertyChangedFor(nameof(SubscribeButtonText))]
    public partial DateTime? SubscriptionEndDate { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowBillingSource))]
    [NotifyPropertyChangedFor(nameof(BillingSourceText))]
    public partial string SubscriptionBillingSource { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SubscriptionStatusText))]
    [NotifyPropertyChangedFor(nameof(SubscriptionStatusMessage))]
    public partial string SubscriptionStatus { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsCancelling { get; set; }

    [ObservableProperty]
    public partial bool IsSubscribing { get; set; }

    [ObservableProperty]
    public partial bool IsDeleting { get; set; }

    [ObservableProperty]
    public partial bool ShowDeleteConfirmation { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirmDelete))]
    public partial string ConfirmationText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;

    public bool ShowCancelSubscription => HasActiveSubscription && !SubscriptionEndDate.HasValue;
    public bool CanCreateSubscription => !ShowCancelSubscription;
    public bool CanDeleteAccount => !ShowCancelSubscription;
    public bool CanConfirmDelete => string.Equals(ConfirmationText?.Trim(), "DELETE", StringComparison.OrdinalIgnoreCase);
    public bool ShowSubscriptionEndDate => SubscriptionEndDate.HasValue && HasActiveSubscription;
    public bool ShowBillingSource => !string.IsNullOrWhiteSpace(SubscriptionBillingSource);
    public string SubscriptionStatusText => ShowCancelSubscription
        ? "Active"
        : SubscriptionEndDate.HasValue
            ? (HasActiveSubscription ? "Cancelled" : "Expired")
            : "No Active Subscription";
    public string SubscriptionStatusMessage => ShowCancelSubscription
        ? "You have an active subscription. If you cancel, you will still have access until the end of your current billing period."
        : SubscriptionEndDate.HasValue && HasActiveSubscription
            ? $"Your subscription has been cancelled. You still have full access until {SubscriptionEndDate.Value.ToLocalTime():MMMM dd, yyyy h:mm tt}."
            : SubscriptionEndDate.HasValue
                ? $"Your previous subscription ended on {SubscriptionEndDate.Value.ToLocalTime():MMMM dd, yyyy h:mm tt}."
                : "You do not currently have an active subscription.";
    public string SubscriptionEndDateText => SubscriptionEndDate.HasValue
        ? $"Access Until: {SubscriptionEndDate.Value.ToLocalTime():MMMM dd, yyyy h:mm tt}"
        : string.Empty;
    public string BillingSourceText => $"Billed via: {SubscriptionBillingSource}";
    public string SubscribeButtonText => SubscriptionEndDate.HasValue ? "Create New Subscription" : "Subscribe Now";

    public AccountSettingsViewModel(
        IAuthService authService,
        IAlertService alertService,
        INavigationService navigationService,
        IMusicService musicService,
        IBillingService billingService)
    {
        _authService = authService;
        _alertService = alertService;
        _navigationService = navigationService;
        _musicService = musicService;
        _billingService = billingService;

        _authService.AuthStateChanged += OnAuthStateChanged;
        ApplySubscriptionState(null);
    }

    public async Task OnAppearingAsync()
    {
        await _authService.RefreshUserStatusAsync();
        ApplySubscriptionState(null);
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        var status = await _musicService.GetSubscriptionStatusAsync();
        ApplySubscriptionState(status);
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
                await LoadAsync();

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
    private async Task SubscribeAsync()
    {
        IsSubscribing = true;
        try
        {
            var result = await _billingService.PurchaseSubscriptionAsync();

            if (!result.Success)
            {
                if (result.ErrorMessage != "Purchase was cancelled.")
                    await _alertService.DisplayAlertAsync("Subscribe", result.ErrorMessage ?? "Purchase failed.", "OK");
                return;
            }

            var verificationResult = await _musicService.VerifyGooglePlayPurchaseAsync(result.PurchaseToken!, result.OrderId);
            if (!verificationResult.Success)
            {
                var errorMessage = string.IsNullOrWhiteSpace(verificationResult.ErrorMessage)
                    ? "Purchase succeeded but server verification failed. Please try again."
                    : verificationResult.ErrorMessage;
                await _alertService.DisplayAlertAsync("Subscribe", errorMessage, "OK");
                return;
            }

            await _authService.RefreshUserStatusAsync();
            await LoadAsync();
            await _alertService.DisplayAlertAsync("Success", "You're now subscribed! Enjoy unlimited music.", "OK");
        }
        catch (Exception ex)
        {
            await _alertService.DisplayAlertAsync("Error", $"Subscription failed: {ex.Message}", "OK");
        }
        finally
        {
            IsSubscribing = false;
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

    private void ApplySubscriptionState(SubscriptionStatusDto? status)
    {
        UserEmail = _authService.Email ?? string.Empty;
        HasActiveSubscription = status?.HasSubscription ?? _authService.HasActiveSubscription;
        SubscriptionEndDate = status?.EndDate ?? _authService.SubscriptionEndDate;
        SubscriptionStatus = status?.Status ?? _authService.SubscriptionStatus ?? string.Empty;
        SubscriptionBillingSource = status?.BillingSource ?? _authService.BillingSource ?? string.Empty;
    }

    private void OnAuthStateChanged()
    {
        _ = LoadAsync();
    }
}
