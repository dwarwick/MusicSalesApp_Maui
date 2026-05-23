using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Configuration;
using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.ViewModels;

public partial class AccountSettingsViewModel : ObservableObject
{
    private readonly IAuthService _authService;
    private readonly IAlertService _alertService;
    private readonly INavigationService _navigationService;
    private readonly IBrowserService _browserService;
    private readonly IConfiguration _configuration;
    private readonly IMusicService _musicService;
    private readonly IBillingService _billingService;

    private const string DefaultAppleSubscriptionManagementUrl = "https://account.apple.com/account/manage/section/subscriptions";

    [ObservableProperty]
    public partial string UserEmail { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCancelSubscription))]
    [NotifyPropertyChangedFor(nameof(CanCreateSubscription))]
    [NotifyPropertyChangedFor(nameof(CanDeleteAccount))]
    [NotifyPropertyChangedFor(nameof(IsNonRenewingSubscription))]
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
    [NotifyPropertyChangedFor(nameof(IsNonRenewingSubscription))]
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
    [NotifyPropertyChangedFor(nameof(ShowCancelSubscription))]
    [NotifyPropertyChangedFor(nameof(IsNonRenewingSubscription))]
    [NotifyPropertyChangedFor(nameof(SubscriptionStatusText))]
    [NotifyPropertyChangedFor(nameof(SubscriptionStatusMessage))]
    [NotifyPropertyChangedFor(nameof(SubscriptionEndDateText))]
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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDeleteAccount))]
    [NotifyPropertyChangedFor(nameof(ShowCreatorDeletionBlockedMessage))]
    public partial bool IsActiveCreator { get; set; }

    public bool IsNonRenewingSubscription =>
        HasActiveSubscription &&
        SubscriptionEndDate.HasValue &&
        string.Equals(SubscriptionStatus, "CANCELLED", StringComparison.OrdinalIgnoreCase);
    public bool ShowCancelSubscription => HasActiveSubscription && !IsNonRenewingSubscription;
    public bool CanCreateSubscription => !HasActiveSubscription;
    public bool CanDeleteAccount => !HasActiveSubscription && !IsActiveCreator;
    public bool CanConfirmDelete => string.Equals(ConfirmationText?.Trim(), "DELETE", StringComparison.OrdinalIgnoreCase);
    public bool ShowSubscriptionEndDate => SubscriptionEndDate.HasValue && HasActiveSubscription;
    public bool ShowBillingSource => !string.IsNullOrWhiteSpace(SubscriptionBillingSource);
    public bool ShowCreatorDeletionBlockedMessage => IsActiveCreator;
    public string SubscriptionStatusText => IsNonRenewingSubscription
        ? "Renews Off"
        : HasActiveSubscription
            ? "Active"
            : SubscriptionEndDate.HasValue
                ? "Expired"
                : "No Active Subscription";
    public string SubscriptionStatusMessage => IsNonRenewingSubscription
        ? $"Your subscription has been canceled. It will not automatically renew. You will continue to enjoy subscription benefits until {SubscriptionEndDate!.Value.ToLocalTime():MMMM dd, yyyy h:mm tt}."
        : HasActiveSubscription
            ? SubscriptionEndDate.HasValue
                ? $"Your subscription is active and will automatically renew unless canceled. Your current billing period ends on {SubscriptionEndDate.Value.ToLocalTime():MMMM dd, yyyy h:mm tt}."
                : "You have an active subscription that will automatically renew unless canceled. If you cancel, you will still have access until the end of your current billing period."
            : SubscriptionEndDate.HasValue
                ? $"Your previous subscription ended on {SubscriptionEndDate.Value.ToLocalTime():MMMM dd, yyyy h:mm tt}."
                : "You do not currently have an active subscription.";
    public string SubscriptionEndDateText => SubscriptionEndDate.HasValue
        ? IsNonRenewingSubscription
            ? $"Access Until: {SubscriptionEndDate.Value.ToLocalTime():MMMM dd, yyyy h:mm tt}"
            : HasActiveSubscription
                ? $"Current Billing Period Ends: {SubscriptionEndDate.Value.ToLocalTime():MMMM dd, yyyy h:mm tt}"
                : $"Ended On: {SubscriptionEndDate.Value.ToLocalTime():MMMM dd, yyyy h:mm tt}"
        : string.Empty;
    public string BillingSourceText => $"Billed via: {SubscriptionBillingSource}";
    public string SubscribeButtonText => SubscriptionEndDate.HasValue ? "Create New Subscription" : "Subscribe Now";

    private bool ShouldUseExternalSubscriptionManagement =>
        string.Equals(SubscriptionBillingSource, BillingProviders.Apple, StringComparison.Ordinal) ||
        string.Equals(SubscriptionBillingSource, BillingProviders.GooglePlay, StringComparison.Ordinal);

    private string GetExternalSubscriptionManagementUrl()
        => string.Equals(SubscriptionBillingSource, BillingProviders.Apple, StringComparison.Ordinal)
            ? _configuration["AppleAppStore:SubscriptionManagementUrl"] ?? DefaultAppleSubscriptionManagementUrl
            : "https://play.google.com/store/account/subscriptions";

    private string GetExternalSubscriptionManagementActionText()
        => string.Equals(SubscriptionBillingSource, BillingProviders.Apple, StringComparison.Ordinal)
            ? IsAppleSandboxManagementUrl()
                ? "Open Apple Sandbox Help"
                : "Open Apple Subscription Page"
            : "Open Google Play";

    private string GetExternalSubscriptionManagementMessage()
        => string.Equals(SubscriptionBillingSource, BillingProviders.Apple, StringComparison.Ordinal)
            ? IsAppleSandboxManagementUrl()
                ? "Sandbox Apple subscriptions are managed on the test device in Settings > Developer > Sandbox Account > Manage. Open Apple's sandbox instructions now?"
                : "Apple subscriptions must be managed with Apple. Open Apple's subscription management page now?"
            : "Google Play subscriptions should be managed in Google Play subscription settings. Open Google Play now?";

    private bool IsAppleSandboxManagementUrl()
        => GetExternalSubscriptionManagementUrl().Contains("developer.apple.com", StringComparison.OrdinalIgnoreCase);

    public AccountSettingsViewModel(
        IAuthService authService,
        IAlertService alertService,
        INavigationService navigationService,
        IBrowserService browserService,
        IConfiguration configuration,
        IMusicService musicService,
        IBillingService billingService)
    {
        _authService = authService;
        _alertService = alertService;
        _navigationService = navigationService;
        _browserService = browserService;
        _configuration = configuration;
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
        if (ShouldUseExternalSubscriptionManagement)
        {
            var manageConfirmed = await _alertService.ShowConfirmAsync(
                "Manage Subscription",
                GetExternalSubscriptionManagementMessage(),
                GetExternalSubscriptionManagementActionText(),
                "Not Now");

            if (!manageConfirmed)
            {
                return;
            }

            await _browserService.OpenExternalAsync(GetExternalSubscriptionManagementUrl());
            return;
        }

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

            var verificationResult = await _musicService.VerifySubscriptionPurchaseAsync(result.ToVerificationRequest());
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

        if (IsActiveCreator)
        {
            await _alertService.DisplayAlertAsync(
                "Creator Account",
                "You must stop being a creator on the Streamtunes website before deleting your account.",
                "OK");
            return;
        }

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
            "• All your account data including playlists, likes/dislikes, reports, and subscription records will be permanently deleted.\n" +
            "• Your custom playlists will be deleted immediately.\n" +
            "• If you create an account again later, your deleted playlists and preferences will not be restored.\n\n" +
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
        IsActiveCreator = _authService.IsCreator;
    }

    private void OnAuthStateChanged()
    {
        _ = LoadAsync();
    }
}
