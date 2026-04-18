using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.ViewModels;

public partial class HomeViewModel : ObservableObject
{
    private readonly IAuthService _authService;
    private readonly IAppSettingsService _appSettingsService;
    private readonly INavigationService _navigationService;
    private readonly IAlertService _alertService;
    private readonly IAppConfig _appConfig;
    private readonly IBillingService _billingService;
    private readonly IMusicService _musicService;
    private readonly IBrowserService _browserService;

    [ObservableProperty]
    public partial bool IsLoading { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SubscribeButtonText))]
    [NotifyPropertyChangedFor(nameof(ShowLoginRegister))]
    [NotifyPropertyChangedFor(nameof(ShowValidateEmail))]
    [NotifyPropertyChangedFor(nameof(ShowSubscribeNow))]
    [NotifyPropertyChangedFor(nameof(ShowBrowseMusic))]
    public partial bool IsAuthenticated { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSubscriptionContent))]
    [NotifyPropertyChangedFor(nameof(ShowSubscribeNow))]
    [NotifyPropertyChangedFor(nameof(ShowBrowseMusic))]
    public partial bool HasActiveSubscription { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowValidateEmail))]
    [NotifyPropertyChangedFor(nameof(ShowSubscribeNow))]
    public partial bool IsEmailVerified { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SubscribeButtonText))]
    public partial string SubscriptionPrice { get; set; } = "3.99";

    public bool ShowSubscriptionContent => !HasActiveSubscription;
    public bool ShowLoginRegister => !IsAuthenticated;
    public bool ShowValidateEmail => IsAuthenticated && !IsEmailVerified;
    public bool ShowSubscribeNow => IsAuthenticated && IsEmailVerified && !HasActiveSubscription;
    public bool ShowBrowseMusic => IsAuthenticated && HasActiveSubscription;

    public string SubscribeButtonText => $"Subscribe Now — ${SubscriptionPrice}/mo";

    public HomeViewModel(
        IAuthService authService,
        IAppSettingsService appSettingsService,
        INavigationService navigationService,
        IAlertService alertService,
        IAppConfig appConfig,
        IBillingService billingService,
        IMusicService musicService,
        IBrowserService browserService)
    {
        _authService = authService;
        _appSettingsService = appSettingsService;
        _navigationService = navigationService;
        _alertService = alertService;
        _appConfig = appConfig;
        _billingService = billingService;
        _musicService = musicService;
        _browserService = browserService;

        _authService.AuthStateChanged += OnAuthStateChanged;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            SubscriptionPrice = await _appSettingsService.GetSubscriptionPriceAsync();
            OnPropertyChanged(nameof(SubscribeButtonText));
            RefreshAuthState();
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private Task NavigateToLoginAsync() => _navigationService.GoToAsync("login");

    [RelayCommand]
    private Task NavigateToRegisterAsync() => _navigationService.GoToAsync("register");

    [RelayCommand]
    private Task NavigateToValidateEmailAsync()
    {
        return _navigationService.GoToAsync("verify-email", new Dictionary<string, object>
        {
            ["UserId"] = _authService.UserId ?? 0,
            ["Email"] = _authService.Email ?? string.Empty,
            ["Password"] = string.Empty
        });
    }

    [RelayCommand]
    private async Task SubscribeAsync()
    {
        try
        {
            var result = await _billingService.PurchaseSubscriptionAsync();

            if (!result.Success)
            {
                if (result.ErrorMessage != "Purchase was cancelled.")
                    await _alertService.DisplayAlertAsync("Subscribe", result.ErrorMessage ?? "Purchase failed.", "OK");
                return;
            }

            // Verify purchase with the server and record the subscription
            var verified = await _musicService.VerifyGooglePlayPurchaseAsync(result.PurchaseToken!, result.OrderId);

            if (verified)
            {
                await _authService.RefreshUserStatusAsync();
                RefreshAuthState();
                await _alertService.DisplayAlertAsync("Success", "You're now subscribed! Enjoy unlimited music.", "OK");
            }
            else
            {
                await _alertService.DisplayAlertAsync("Subscribe", "Purchase succeeded but server verification failed. Please try again.", "OK");
            }
        }
        catch (Exception ex)
        {
            await _alertService.DisplayAlertAsync("Error", $"Subscription failed: {ex.Message}", "OK");
        }
    }

    [RelayCommand]
    private Task NavigateToMusicLibraryAsync() => _navigationService.GoToAsync("//MusicLibrary");

    [RelayCommand]
    private async Task OpenGooglePlaySubscriptionsAsync()
    {
        await _browserService.OpenAsync("https://play.google.com/store/account/subscriptions");
    }

    private void RefreshAuthState()
    {
        IsAuthenticated = _authService.IsLoggedIn;
        HasActiveSubscription = _authService.HasActiveSubscription;
        IsEmailVerified = _authService.EmailConfirmed;
    }

    private void OnAuthStateChanged()
    {
        RefreshAuthState();
    }
}
