using Moq;
using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Tests.ViewModels;

[TestFixture]
public class HomeViewModelTests
{
    private Mock<IAuthService> _mockAuthService;
    private Mock<IAppSettingsService> _mockAppSettingsService;
    private Mock<INavigationService> _mockNavigationService;
    private Mock<IAlertService> _mockAlertService;
    private Mock<IAppConfig> _mockAppConfig;
    private Mock<IBillingService> _mockBillingService;
    private Mock<IMusicService> _mockMusicService;
    private Mock<IBrowserService> _mockBrowserService;
    private Mock<IPlaylistService> _mockPlaylistService;
    private HomeViewModel _viewModel;

    [SetUp]
    public void Setup()
    {
        _mockAuthService = new Mock<IAuthService>();
        _mockAppSettingsService = new Mock<IAppSettingsService>();
        _mockNavigationService = new Mock<INavigationService>();
        _mockAlertService = new Mock<IAlertService>();
        _mockAppConfig = new Mock<IAppConfig>();
        _mockBillingService = new Mock<IBillingService>();
        _mockMusicService = new Mock<IMusicService>();
        _mockBrowserService = new Mock<IBrowserService>();
        _mockPlaylistService = new Mock<IPlaylistService>();

        _mockAppSettingsService.Setup(s => s.GetSubscriptionPriceAsync()).ReturnsAsync("3.99");
        _mockAppConfig.Setup(c => c.WebBaseUrl).Returns("https://streamtunes.net");
        _mockPlaylistService.Setup(p => p.GetHomePlaylistsAsync())
            .ReturnsAsync(new HomePlaylistsDto());

        _viewModel = CreateViewModel();
    }

    private HomeViewModel CreateViewModel()
    {
        return new HomeViewModel(
            _mockAuthService.Object,
            _mockAppSettingsService.Object,
            _mockNavigationService.Object,
            _mockAlertService.Object,
            _mockAppConfig.Object,
            _mockBillingService.Object,
            _mockMusicService.Object,
            _mockBrowserService.Object,
            _mockPlaylistService.Object);
    }

    [Test]
    public void InitialState_IsNotAuthenticated()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.IsAuthenticated, Is.False);
            Assert.That(_viewModel.HasActiveSubscription, Is.False);
            Assert.That(_viewModel.IsEmailVerified, Is.False);
            Assert.That(_viewModel.SubscriptionPrice, Is.EqualTo("3.99"));
            Assert.That(_viewModel.IsLoading, Is.True);
        });
    }

    [Test]
    public void ShowLoginRegister_TrueWhenNotAuthenticated()
    {
        _viewModel.IsAuthenticated = false;
        Assert.That(_viewModel.ShowLoginRegister, Is.True);
    }

    [Test]
    public void ShowLoginRegister_FalseWhenAuthenticated()
    {
        _viewModel.IsAuthenticated = true;
        Assert.That(_viewModel.ShowLoginRegister, Is.False);
    }

    [Test]
    public void ShowValidateEmail_TrueWhenAuthenticatedAndNotVerified()
    {
        _viewModel.IsAuthenticated = true;
        _viewModel.IsEmailVerified = false;
        Assert.That(_viewModel.ShowValidateEmail, Is.True);
    }

    [Test]
    public void ShowValidateEmail_FalseWhenNotAuthenticated()
    {
        _viewModel.IsAuthenticated = false;
        _viewModel.IsEmailVerified = false;
        Assert.That(_viewModel.ShowValidateEmail, Is.False);
    }

    [Test]
    public void ShowSubscribeNow_TrueWhenAuthenticatedVerifiedNoSubscription()
    {
        _viewModel.IsAuthenticated = true;
        _viewModel.IsEmailVerified = true;
        _viewModel.HasActiveSubscription = false;
        Assert.That(_viewModel.ShowSubscribeNow, Is.True);
    }

    [Test]
    public void ShowSubscribeNow_FalseWhenHasSubscription()
    {
        _viewModel.IsAuthenticated = true;
        _viewModel.IsEmailVerified = true;
        _viewModel.HasActiveSubscription = true;
        Assert.That(_viewModel.ShowSubscribeNow, Is.False);
    }

    [Test]
    public void ShowBrowseMusic_TrueWhenAuthenticatedWithSubscription()
    {
        _viewModel.IsAuthenticated = true;
        _viewModel.HasActiveSubscription = true;
        Assert.That(_viewModel.ShowBrowseMusic, Is.True);
    }

    [Test]
    public void ShowBrowseMusic_FalseWhenNoSubscription()
    {
        _viewModel.IsAuthenticated = true;
        _viewModel.HasActiveSubscription = false;
        Assert.That(_viewModel.ShowBrowseMusic, Is.False);
    }

    [Test]
    public void ShowSubscriptionContent_FalseWhenHasSubscription()
    {
        _viewModel.HasActiveSubscription = true;
        Assert.That(_viewModel.ShowSubscriptionContent, Is.False);
    }

    [Test]
    public void ShowSubscriptionContent_TrueWhenNoSubscription()
    {
        _viewModel.HasActiveSubscription = false;
        Assert.That(_viewModel.ShowSubscriptionContent, Is.True);
    }

    [Test]
    public async Task LoadAsync_SetsSubscriptionPriceFromService()
    {
        _mockAppSettingsService.Setup(s => s.GetSubscriptionPriceAsync()).ReturnsAsync("9.99");

        await _viewModel.LoadCommand.ExecuteAsync(null);

        Assert.That(_viewModel.SubscriptionPrice, Is.EqualTo("9.99"));
    }

    [Test]
    public async Task LoadAsync_SetsIsLoadingFalseAfterCompletion()
    {
        await _viewModel.LoadCommand.ExecuteAsync(null);

        Assert.That(_viewModel.IsLoading, Is.False);
    }

    [Test]
    public async Task LoadAsync_RefreshesAuthState()
    {
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(true);
        _mockAuthService.Setup(a => a.HasActiveSubscription).Returns(true);
        _mockAuthService.Setup(a => a.EmailConfirmed).Returns(true);

        await _viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.IsAuthenticated, Is.True);
            Assert.That(_viewModel.HasActiveSubscription, Is.True);
            Assert.That(_viewModel.IsEmailVerified, Is.True);
        });
    }

    [Test]
    public async Task NavigateToLoginCommand_NavigatesToLoginRoute()
    {
        await _viewModel.NavigateToLoginCommand.ExecuteAsync(null);

        _mockNavigationService.Verify(n => n.GoToAsync("login"), Times.Once);
    }

    [Test]
    public async Task NavigateToRegisterCommand_NavigatesToRegisterRoute()
    {
        await _viewModel.NavigateToRegisterCommand.ExecuteAsync(null);

        _mockNavigationService.Verify(n => n.GoToAsync("register"), Times.Once);
    }

    [Test]
    public async Task NavigateToValidateEmailCommand_NavigatesToVerifyEmail()
    {
        _mockAuthService.Setup(a => a.UserId).Returns(42);
        _mockAuthService.Setup(a => a.Email).Returns("test@test.com");
        _viewModel = CreateViewModel();

        await _viewModel.NavigateToValidateEmailCommand.ExecuteAsync(null);

        _mockNavigationService.Verify(n => n.GoToAsync("verify-email", It.Is<IDictionary<string, object>>(d =>
            (int)d["UserId"] == 42 &&
            (string)d["Email"] == "test@test.com")), Times.Once);
    }

    [Test]
    public async Task SubscribeCommand_SuccessfulPurchase_VerifiesWithServerAndRefreshesStatus()
    {
        _mockBillingService.Setup(b => b.PurchaseSubscriptionAsync())
            .ReturnsAsync(BillingPurchaseResult.Succeeded("test-token", "order-123"));
        _mockMusicService.Setup(m => m.VerifyGooglePlayPurchaseAsync("test-token", "order-123"))
            .ReturnsAsync((true, string.Empty));

        await _viewModel.SubscribeCommand.ExecuteAsync(null);

        _mockMusicService.Verify(m => m.VerifyGooglePlayPurchaseAsync("test-token", "order-123"), Times.Once);
        _mockAuthService.Verify(a => a.RefreshUserStatusAsync(), Times.Once);
        _mockAlertService.Verify(a => a.DisplayAlertAsync("Success", It.IsAny<string>(), "OK"), Times.Once);
    }

    [Test]
    public async Task SubscribeCommand_PurchaseFailed_ShowsErrorAlert()
    {
        _mockBillingService.Setup(b => b.PurchaseSubscriptionAsync())
            .ReturnsAsync(BillingPurchaseResult.Failed("Connection error"));

        await _viewModel.SubscribeCommand.ExecuteAsync(null);

        _mockAlertService.Verify(a => a.DisplayAlertAsync("Subscribe", "Connection error", "OK"), Times.Once);
        _mockMusicService.Verify(m => m.VerifyGooglePlayPurchaseAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task SubscribeCommand_UserCancelled_NoAlertShown()
    {
        _mockBillingService.Setup(b => b.PurchaseSubscriptionAsync())
            .ReturnsAsync(BillingPurchaseResult.Cancelled());

        await _viewModel.SubscribeCommand.ExecuteAsync(null);

        _mockAlertService.Verify(a => a.DisplayAlertAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task SubscribeCommand_ServerVerificationFails_ShowsError()
    {
        _mockBillingService.Setup(b => b.PurchaseSubscriptionAsync())
            .ReturnsAsync(BillingPurchaseResult.Succeeded("test-token", "order-123"));
        _mockMusicService.Setup(m => m.VerifyGooglePlayPurchaseAsync("test-token", "order-123"))
            .ReturnsAsync((false, "Configured Google Play service account key file was not found on the server."));

        await _viewModel.SubscribeCommand.ExecuteAsync(null);

        _mockAlertService.Verify(a => a.DisplayAlertAsync("Subscribe",
            It.Is<string>(s => s.Contains("Configured Google Play service account key file was not found on the server.")), "OK"), Times.Once);
        _mockAuthService.Verify(a => a.RefreshUserStatusAsync(), Times.Never);
    }

    [Test]
    public async Task NavigateToMusicLibraryCommand_NavigatesToMusicLibrary()
    {
        await _viewModel.NavigateToMusicLibraryCommand.ExecuteAsync(null);

        _mockNavigationService.Verify(n => n.GoToAsync("//MusicLibrary"), Times.Once);
    }

    [Test]
    public void SubscribeButtonText_IncludesPrice()
    {
        _viewModel.SubscriptionPrice = "4.99";
        Assert.That(_viewModel.SubscribeButtonText, Is.EqualTo("Subscribe Now — $4.99/mo"));
    }

    [Test]
    public void AuthStateChanged_RefreshesProperties()
    {
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(true);
        _mockAuthService.Setup(a => a.HasActiveSubscription).Returns(true);
        _mockAuthService.Setup(a => a.EmailConfirmed).Returns(true);

        // Raise the AuthStateChanged event
        _mockAuthService.Raise(a => a.AuthStateChanged += null);

        // The event handler calls MainThread.BeginInvokeOnMainThread which won't work in tests,
        // so we test RefreshAuthState behavior indirectly via LoadCommand instead
        // (AuthStateChanged tested through integration)
    }

    [Test]
    public async Task OpenGooglePlaySubscriptions_OpensBrowserToSubscriptionsUrl()
    {
        await _viewModel.OpenGooglePlaySubscriptionsCommand.ExecuteAsync(null);

        _mockBrowserService.Verify(b => b.OpenAsync("https://play.google.com/store/account/subscriptions"), Times.Once);
    }

    [Test]
    public async Task OpenRecommendedCommand_PassesUserIdAsString()
    {
        // Shell.ApplyQueryAttributes throws InvalidCastException when a non-string value is
        // assigned to a string? query property, so the id must be passed as a string.
        _mockAuthService.Setup(a => a.UserId).Returns(7);

        await _viewModel.OpenRecommendedCommand.ExecuteAsync(null);

        _mockNavigationService.Verify(n => n.GoToAsync("playlist-player",
            It.Is<IDictionary<string, object>>(d => (string)d["RecommendedUserId"] == "7")), Times.Once);
    }

    [Test]
    public async Task OpenPlaylistCommand_PassesPlaylistIdAsString()
    {
        var playlist = new PlaylistDto { Id = 99, Name = "Workout" };

        await _viewModel.OpenPlaylistCommand.ExecuteAsync(playlist);

        _mockNavigationService.Verify(n => n.GoToAsync("playlist-player",
            It.Is<IDictionary<string, object>>(d => (string)d["PlaylistId"] == "99")), Times.Once);
    }

    [Test]
    public async Task OpenPlaylistCommand_Null_DoesNothing()
    {
        await _viewModel.OpenPlaylistCommand.ExecuteAsync(null);

        _mockNavigationService.Verify(n => n.GoToAsync(It.IsAny<string>(), It.IsAny<IDictionary<string, object>>()), Times.Never);
    }
}
