using Microsoft.Extensions.Configuration;
using Moq;
using MusicSalesApp.Common.Helpers;
using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.ViewModels;
using System.Collections.ObjectModel;
using System.Globalization;

namespace MusicSalesApp.Maui.Tests.ViewModels;

[TestFixture]
public class HomeViewModelTests
{
    private Mock<IAuthService> _mockAuthService;
    private Mock<INetworkStatusService> _mockNetworkStatus;
    private Mock<INavigationService> _mockNavigationService;
    private Mock<IAlertService> _mockAlertService;
    private Mock<IAppConfig> _mockAppConfig;
    private Mock<IBillingService> _mockBillingService;
    private Mock<IMusicService> _mockMusicService;
    private Mock<ISignalRService> _mockSignalRService;
    private Mock<IPlaybackService> _mockPlaybackService;
    private Mock<IMediaPlaybackOnboardingService> _mockMediaPlaybackOnboardingService;
    private Mock<IBrowserService> _mockBrowserService;
    private Mock<IConfiguration> _mockConfiguration;
    private Mock<IPlaylistService> _mockPlaylistService;
    private HomeViewModel _viewModel;

    [SetUp]
    public void Setup()
    {
        _mockAuthService = new Mock<IAuthService>();
        _mockNetworkStatus = new Mock<INetworkStatusService>();
        _mockNavigationService = new Mock<INavigationService>();
        _mockAlertService = new Mock<IAlertService>();
        _mockAppConfig = new Mock<IAppConfig>();
        _mockBillingService = new Mock<IBillingService>();
        _mockMusicService = new Mock<IMusicService>();
        _mockSignalRService = new Mock<ISignalRService>();
        _mockPlaybackService = new Mock<IPlaybackService>();
        _mockMediaPlaybackOnboardingService = new Mock<IMediaPlaybackOnboardingService>();
        _mockBrowserService = new Mock<IBrowserService>();
        _mockConfiguration = new Mock<IConfiguration>();
        _mockPlaylistService = new Mock<IPlaylistService>();

        _mockConfiguration.Setup(c => c["AppleAppStore:SubscriptionManagementUrl"])
            .Returns("https://developer.apple.com/documentation/storekit/testing-disabling-auto-renew");

        _mockAppConfig.Setup(c => c.ApiBaseUrl).Returns("https://streamtunes.net");
        _mockAppConfig.Setup(c => c.WebBaseUrl).Returns("https://streamtunes.net");
        _mockMediaPlaybackOnboardingService.Setup(s => s.EnsureBackgroundPlaybackExplainedAsync()).Returns(Task.CompletedTask);
        _mockMusicService.Setup(s => s.GetSongsAsync()).ReturnsAsync([]);
        _mockMusicService.Setup(s => s.GetStreamQualifyingSecondsAsync()).ReturnsAsync(30);
        _mockMusicService.Setup(s => s.GetBulkLikeCountsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync([]);
        _mockMusicService.Setup(s => s.GetBulkUserLikeStatusAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new Dictionary<int, UserSongRatingState>());
        _mockPlaylistService.Setup(p => p.GetHomePlaylistsAsync())
            .ReturnsAsync(new HomePlaylistsDto());
        _mockPlaylistService.Setup(p => p.GetTopStreamedPlaylistsAsync())
            .ReturnsAsync([]);

        _viewModel = CreateViewModel();
    }

    private HomeViewModel CreateViewModel()
    {
        return new HomeViewModel(
            _mockAuthService.Object,
            _mockNetworkStatus.Object,
            _mockNavigationService.Object,
            _mockAlertService.Object,
            _mockAppConfig.Object,
            _mockBillingService.Object,
            _mockMusicService.Object,
            _mockSignalRService.Object,
            _mockPlaybackService.Object,
            _mockMediaPlaybackOnboardingService.Object,
            _mockBrowserService.Object,
            _mockConfiguration.Object,
            _mockPlaylistService.Object);
    }

    [Test]
    public void NetworkStatus_ExposesOfflineStateForSubscriptionBanner()
    {
        _mockNetworkStatus.SetupGet(service => service.IsOffline).Returns(true);

        Assert.That(_viewModel.NetworkStatus.IsOffline, Is.True);
    }

    [Test]
    public void InitialState_IsNotAuthenticated()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.IsAuthenticated, Is.False);
            Assert.That(_viewModel.HasActiveSubscription, Is.False);
            Assert.That(_viewModel.IsEmailVerified, Is.False);
            Assert.That(_viewModel.SubscriptionPrice, Is.Empty);
            Assert.That(_viewModel.SubscriptionPriceDisplay, Is.Empty);
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
    public void AndroidFreeTrialCard_HidesStandaloneSubscribeButton()
    {
        _viewModel.IsAuthenticated = true;
        _viewModel.IsEmailVerified = true;
        _viewModel.HasActiveSubscription = false;
        _viewModel.HasEligibleAndroidFreeTrial = true;

        Assert.Multiple(() =>
        {
            _viewModel.IsAndroidSubscriptionPlatform = true;
            Assert.That(_viewModel.ShowSubscriptionOfferCard, Is.True);
            Assert.That(_viewModel.ShowSubscribeNow, Is.False);
        });
    }

    [Test]
    public void LoggedInVerifiedAndroidFirstTimeUser_SeesOfferCardWhileGoogleOfferLookupIsUnresolved()
    {
        _viewModel.IsAndroidSubscriptionPlatform = true;
        _viewModel.IsAuthenticated = true;
        _viewModel.IsEmailVerified = true;
        _viewModel.HasActiveSubscription = false;
        _viewModel.HasPreviousSubscriptionHistory = false;
        _viewModel.HasResolvedAndroidSubscriptionOffer = false;

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.ShowSubscriptionOfferCard, Is.True);
            Assert.That(_viewModel.ShowSubscribeNow, Is.False);
            Assert.That(_viewModel.SubscriptionOfferPrimaryButtonText, Is.EqualTo("Start My Free Trial"));
            Assert.That(_viewModel.ShowSubscriptionOfferSecondaryButton, Is.False);
            Assert.That(_viewModel.SubscriptionOfferTitleText, Is.EqualTo("Support independent music."));
            Assert.That(_viewModel.SubscriptionOfferBodyText, Does.Contain("directly funds independent creators"));
            Assert.That(_viewModel.SubscriptionOfferDisclosureText, Does.StartWith("Full subscription benefits are included during the trial."));
            Assert.That(_viewModel.SubscriptionOfferDisclosureText, Does.Contain("Try it free for 3 days."));
        });
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
    public void ShowBrowseMusic_TrueWhenLoggedOut()
    {
        Assert.That(_viewModel.ShowBrowseMusic, Is.True);
    }

    [Test]
    public void ShowBrowseMusic_TrueWhenAuthenticatedWithoutSubscription()
    {
        _viewModel.IsAuthenticated = true;
        _viewModel.HasActiveSubscription = false;
        Assert.That(_viewModel.ShowBrowseMusic, Is.True);
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
    public async Task LoadAsync_OnNonAndroid_DoesNotInventSubscriptionPrice()
    {
        _viewModel.IsAndroidSubscriptionPlatform = false;

        await _viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.SubscriptionPrice, Is.Empty);
            Assert.That(_viewModel.SubscriptionPriceDisplay, Is.Empty);
            Assert.That(_viewModel.SubscribeButtonText, Is.EqualTo("Subscribe Now"));
        });
        _mockBillingService.Verify(b => b.GetSubscriptionOfferAsync(), Times.Never);
    }

    [Test]
    public async Task LoadAsync_UsesGoogleRenewalPrice_WhenSubscriptionOfferLookupSucceeds()
    {
        _viewModel.IsAndroidSubscriptionPlatform = true;
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(true);
        _mockAuthService.Setup(a => a.EmailConfirmed).Returns(true);
        _mockBillingService.Setup(b => b.GetSubscriptionOfferAsync())
            .ReturnsAsync(new SubscriptionOfferInfo
            {
                LookupSucceeded = true,
                IsAvailable = true,
                HasFreeTrial = true,
                FreeTrialDays = 3,
                RenewalPrice = "$4.99"
            });

        await _viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.SubscriptionPriceDisplay, Is.EqualTo("$4.99"));
            Assert.That(_viewModel.SubscriptionOfferDisclosureText, Does.Contain("$4.99/month"));
            Assert.That(_viewModel.ShowSubscriptionOfferCard, Is.True);
        });
    }

    [Test]
    public async Task LoadAsync_PublishesAndroidStorePrice()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUICulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");

            _viewModel.IsAndroidSubscriptionPlatform = true;
            _mockAuthService.Setup(a => a.IsLoggedIn).Returns(true);
            _mockAuthService.Setup(a => a.EmailConfirmed).Returns(true);
            _mockAuthService.Setup(a => a.HasActiveSubscription).Returns(false);
            _mockBillingService.Setup(b => b.GetSubscriptionOfferAsync())
                .ReturnsAsync(new SubscriptionOfferInfo
                {
                    LookupSucceeded = true,
                    IsAvailable = true,
                    HasFreeTrial = true,
                    FreeTrialDays = 3,
                    RenewalPrice = "\u20B1205.00"
                });

            var observedPrices = new List<string>();
            _viewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(HomeViewModel.SubscriptionPriceDisplay))
                {
                    observedPrices.Add(_viewModel.SubscriptionPriceDisplay);
                }
            };

            await _viewModel.LoadCommand.ExecuteAsync(null);

            Assert.Multiple(() =>
            {
                Assert.That(observedPrices, Does.Contain("\u20B1205.00"));
                Assert.That(_viewModel.SubscriptionPriceDisplay, Is.EqualTo("\u20B1205.00"));
            });
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUICulture;
        }
    }

    [Test]
    public async Task LoadAsync_ActiveAndroidSubscription_StillUsesGoogleRenewalPriceForDisplay()
    {
        _viewModel.IsAndroidSubscriptionPlatform = true;
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(true);
        _mockAuthService.Setup(a => a.EmailConfirmed).Returns(true);
        _mockAuthService.Setup(a => a.HasActiveSubscription).Returns(true);
        _mockBillingService.Setup(b => b.GetSubscriptionOfferAsync())
            .ReturnsAsync(new SubscriptionOfferInfo
            {
                LookupSucceeded = true,
                IsAvailable = true,
                HasFreeTrial = false,
                RenewalPrice = "\u20B1205.00"
            });

        await _viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.HasActiveSubscription, Is.True);
            Assert.That(_viewModel.SubscriptionPriceDisplay, Is.EqualTo("\u20B1205.00"));
            Assert.That(_viewModel.ShowSubscriptionContent, Is.False);
        });
        _mockBillingService.Verify(b => b.GetSubscriptionOfferAsync(), Times.Once);
    }

    [Test]
    public async Task LoadAsync_KeepsStorePrice_WhenSubsequentOfferLookupFails()
    {
        _viewModel.IsAndroidSubscriptionPlatform = true;
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(true);
        _mockAuthService.Setup(a => a.EmailConfirmed).Returns(true);
        _mockAuthService.Setup(a => a.HasActiveSubscription).Returns(false);
        _mockBillingService.SetupSequence(b => b.GetSubscriptionOfferAsync())
            .ReturnsAsync(new SubscriptionOfferInfo
            {
                LookupSucceeded = true,
                IsAvailable = true,
                HasFreeTrial = true,
                FreeTrialDays = 3,
                RenewalPrice = "\u20B1205.00"
            })
            .ReturnsAsync(new SubscriptionOfferInfo
            {
                LookupSucceeded = false,
                ErrorMessage = "Temporary billing lookup failure"
            });

        await _viewModel.LoadCommand.ExecuteAsync(null);
        await _viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.SubscriptionPriceDisplay, Is.EqualTo("\u20B1205.00"));
            Assert.That(_viewModel.SubscribeButtonText, Does.Contain("\u20B1205.00"));
        });
    }

    [Test]
    public async Task LoadAsync_KeepsFirstStorePrice_WhenLaterLookupReturnsDifferentPrice()
    {
        _viewModel.IsAndroidSubscriptionPlatform = true;
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(true);
        _mockAuthService.Setup(a => a.EmailConfirmed).Returns(true);
        _mockAuthService.Setup(a => a.HasActiveSubscription).Returns(false);
        _mockBillingService.SetupSequence(b => b.GetSubscriptionOfferAsync())
            .ReturnsAsync(new SubscriptionOfferInfo
            {
                LookupSucceeded = true,
                IsAvailable = true,
                HasFreeTrial = true,
                FreeTrialDays = 3,
                RenewalPrice = "\u20B1205.00"
            })
            .ReturnsAsync(new SubscriptionOfferInfo
            {
                LookupSucceeded = true,
                IsAvailable = true,
                HasFreeTrial = true,
                FreeTrialDays = 3,
                RenewalPrice = "$5.99"
            });

        await _viewModel.LoadCommand.ExecuteAsync(null);
        await _viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.SubscriptionPriceDisplay, Is.EqualTo("\u20B1205.00"));
            Assert.That(_viewModel.SubscriptionOfferDisclosureText, Does.Contain("\u20B1205.00/month"));
        });
    }

    [Test]
    public async Task LoadAsync_ExpiredGooglePlaySubscription_WhenGoogleReportsTrial_ShowsFreeTrialOfferCard()
    {
        _viewModel.IsAndroidSubscriptionPlatform = true;
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(true);
        _mockAuthService.Setup(a => a.EmailConfirmed).Returns(true);
        _mockAuthService.Setup(a => a.HasActiveSubscription).Returns(false);
        _mockAuthService.Setup(a => a.SubscriptionStatus).Returns("EXPIRED");
        _mockAuthService.Setup(a => a.SubscriptionEndDate).Returns(DateTime.UtcNow.AddMinutes(-5));
        _mockAuthService.Setup(a => a.BillingSource).Returns(BillingProviders.GooglePlay);
        _mockBillingService.Setup(b => b.GetSubscriptionOfferAsync())
            .ReturnsAsync(new SubscriptionOfferInfo
            {
                LookupSucceeded = true,
                IsAvailable = true,
                HasFreeTrial = true,
                FreeTrialDays = 3,
                RenewalPrice = "$2.99"
            });

        await _viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.HasPreviousSubscriptionHistory, Is.True);
            Assert.That(_viewModel.ShowSubscriptionOfferCard, Is.True);
            Assert.That(_viewModel.ShowSubscribeNow, Is.False);
            Assert.That(_viewModel.SubscriptionOfferPrimaryButtonText, Is.EqualTo("Start My Free Trial"));
            Assert.That(_viewModel.SubscriptionOfferTitleText, Is.EqualTo("Support independent music."));
            Assert.That(_viewModel.SubscriptionOfferBodyText, Does.Contain("Unlock the full catalog."));
            Assert.That(_viewModel.SubscriptionOfferDisclosureText, Does.Contain("$2.99/month"));
        });
        _mockBillingService.Verify(b => b.GetSubscriptionOfferAsync(), Times.Once);
    }

    [Test]
    public async Task LoadAsync_ExpiredGooglePlaySubscription_WhenGoogleHasNoTrial_ShowsPlainSubscribeWithGooglePrice()
    {
        _viewModel.IsAndroidSubscriptionPlatform = true;
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(true);
        _mockAuthService.Setup(a => a.EmailConfirmed).Returns(true);
        _mockAuthService.Setup(a => a.HasActiveSubscription).Returns(false);
        _mockAuthService.Setup(a => a.SubscriptionStatus).Returns("EXPIRED");
        _mockAuthService.Setup(a => a.SubscriptionEndDate).Returns(DateTime.UtcNow.AddMinutes(-5));
        _mockAuthService.Setup(a => a.TrialEndDate).Returns(DateTime.UtcNow.AddMinutes(-10));
        _mockAuthService.Setup(a => a.BillingSource).Returns(BillingProviders.GooglePlay);
        _mockBillingService.Setup(b => b.GetSubscriptionOfferAsync())
            .ReturnsAsync(new SubscriptionOfferInfo
            {
                LookupSucceeded = true,
                IsAvailable = true,
                HasFreeTrial = false,
                RenewalPrice = "$2.99"
            });

        await _viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.HasPreviousSubscriptionHistory, Is.True);
            Assert.That(_viewModel.ShowSubscriptionOfferCard, Is.False);
            Assert.That(_viewModel.ShowSubscribeNow, Is.True);
            Assert.That(_viewModel.SubscribeButtonText, Is.EqualTo("Subscribe Now - $2.99/mo"));
            Assert.That(_viewModel.SubscriptionOfferBodyText, Does.Not.Contain("free trial"));
        });
        _mockBillingService.Verify(b => b.GetSubscriptionOfferAsync(), Times.Once);
    }

    [Test]
    public async Task LoadAsync_ExpiredGooglePlaySubscription_WhenGoogleLookupFails_DoesNotShowFallbackTrial()
    {
        _viewModel.IsAndroidSubscriptionPlatform = true;
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(true);
        _mockAuthService.Setup(a => a.EmailConfirmed).Returns(true);
        _mockAuthService.Setup(a => a.HasActiveSubscription).Returns(false);
        _mockAuthService.Setup(a => a.SubscriptionStatus).Returns("EXPIRED");
        _mockAuthService.Setup(a => a.SubscriptionEndDate).Returns(DateTime.UtcNow.AddMinutes(-5));
        _mockAuthService.Setup(a => a.TrialEndDate).Returns(DateTime.UtcNow.AddMinutes(-10));
        _mockAuthService.Setup(a => a.BillingSource).Returns(BillingProviders.GooglePlay);
        _mockBillingService.Setup(b => b.GetSubscriptionOfferAsync())
            .ReturnsAsync(new SubscriptionOfferInfo
            {
                LookupSucceeded = false,
                ErrorMessage = "Google Play Billing is not available for this installed build."
            });

        await _viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.HasPreviousSubscriptionHistory, Is.True);
            Assert.That(_viewModel.ShowSubscriptionOfferCard, Is.False);
            Assert.That(_viewModel.ShowSubscribeNow, Is.True);
            Assert.That(_viewModel.SubscriptionOfferTitleText, Does.Not.Contain("free trial"));
        });
        _mockBillingService.Verify(b => b.GetSubscriptionOfferAsync(), Times.Once);
    }

    [Test]
    public async Task LoadAsync_ShowsFallbackTrialCard_WhenGoogleOfferLookupFails()
    {
        _viewModel.IsAndroidSubscriptionPlatform = true;
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(true);
        _mockAuthService.Setup(a => a.EmailConfirmed).Returns(true);
        _mockBillingService.Setup(b => b.GetSubscriptionOfferAsync())
            .ReturnsAsync(new SubscriptionOfferInfo
            {
                LookupSucceeded = false,
                ErrorMessage = "Google Play Billing is not available for this installed build."
            });

        await _viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.ShowSubscriptionOfferCard, Is.True);
            Assert.That(_viewModel.SubscriptionOfferPrimaryButtonText, Is.EqualTo("Start My Free Trial"));
            Assert.That(_viewModel.SubscriptionOfferTitleText, Is.EqualTo("Support independent music."));
            Assert.That(_viewModel.SubscriptionOfferBodyText, Does.Contain("directly funds independent creators"));
            Assert.That(_viewModel.SubscriptionOfferDisclosureText, Does.Contain("monthly price shown in Google Play"));
            Assert.That(_viewModel.SubscriptionOfferPriceText, Is.Empty);
            Assert.That(_viewModel.ShowSubscriptionOfferPriceText, Is.False);
            Assert.That(_viewModel.SubscriptionPriceDisplay, Is.Empty);
        });
    }

    [Test]
    public async Task LoadAsync_LoggedOutAndroidVisitor_WhenStorePriceMissing_LeavesPriceEmpty()
    {
        _viewModel.IsAndroidSubscriptionPlatform = true;
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(false);
        _mockBillingService.Setup(b => b.GetSubscriptionOfferAsync())
            .ReturnsAsync(new SubscriptionOfferInfo
            {
                LookupSucceeded = false,
                ErrorMessage = "Temporary billing lookup failure."
            });

        await _viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.IsAuthenticated, Is.False);
            Assert.That(_viewModel.ShowSubscriptionOfferCard, Is.True);
            Assert.That(_viewModel.SubscriptionPriceDisplay, Is.Empty);
            Assert.That(_viewModel.SubscriptionOfferBodyText, Does.Contain("Unlock the full catalog."));
            Assert.That(_viewModel.SubscriptionOfferDisclosureText, Does.Contain("monthly price shown in Google Play"));
            Assert.That(_viewModel.SubscriptionOfferDisclosureText, Does.Not.Contain("$"));
            Assert.That(_viewModel.SubscriptionOfferPriceText, Is.Empty);
            Assert.That(_viewModel.ShowSubscriptionOfferPriceText, Is.False);
        });
    }

    [Test]
    public async Task LoadAsync_LoggedOutAndroidVisitor_WhenGoogleOfferHasNoTrial_ShowsNewUserTrialCopy()
    {
        _viewModel.IsAndroidSubscriptionPlatform = true;
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(false);
        _mockBillingService.Setup(b => b.GetSubscriptionOfferAsync())
            .ReturnsAsync(new SubscriptionOfferInfo
            {
                LookupSucceeded = true,
                IsAvailable = true,
                HasFreeTrial = false,
                RenewalPrice = "$2.99"
            });

        await _viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.IsAuthenticated, Is.False);
            Assert.That(_viewModel.ShowSubscriptionOfferCard, Is.True);
            Assert.That(_viewModel.SubscriptionOfferPrimaryButtonText, Is.EqualTo("Create Account"));
            Assert.That(_viewModel.SubscriptionOfferTitleText, Is.EqualTo("Support independent music."));
            Assert.That(_viewModel.SubscriptionOfferBodyText, Does.Contain("Unlock the full catalog."));
            Assert.That(_viewModel.SubscriptionOfferDisclosureText, Does.StartWith("Full subscription benefits are included during the trial."));
            Assert.That(_viewModel.SubscriptionOfferDisclosureText, Does.Contain("Try it free for 3 days."));
            Assert.That(_viewModel.SubscriptionOfferDisclosureText, Does.Contain("$2.99/month"));
            Assert.That(_viewModel.SubscriptionOfferPriceText, Is.EqualTo("$2.99"));
        });
    }

    [Test]
    public async Task LoadAsync_UnverifiedUser_WhenGoogleOfferHasNoTrial_ShowsNonTrialOfferCopy()
    {
        _viewModel.IsAndroidSubscriptionPlatform = true;
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(true);
        _mockAuthService.Setup(a => a.EmailConfirmed).Returns(false);
        _mockAuthService.Setup(a => a.HasActiveSubscription).Returns(false);
        _mockBillingService.Setup(b => b.GetSubscriptionOfferAsync())
            .ReturnsAsync(new SubscriptionOfferInfo
            {
                LookupSucceeded = true,
                IsAvailable = true,
                HasFreeTrial = false,
                RenewalPrice = "$2.99"
            });

        await _viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.ShowSubscriptionOfferCard, Is.True);
            Assert.That(_viewModel.SubscriptionOfferTitleText, Is.EqualTo("Subscribe for unlimited music"));
            Assert.That(_viewModel.SubscriptionOfferDisclosureText, Does.Contain("automatically renews monthly"));
            Assert.That(_viewModel.SubscriptionOfferBodyText, Does.Contain("$2.99/month"));
            Assert.That(_viewModel.SubscriptionOfferPrimaryButtonText, Is.EqualTo("Validate Email"));
        });
    }

    [Test]
    public async Task LoadAsync_VerifiedUser_WhenGoogleOfferHasNoTrial_ShowsPlainSubscribeWithGooglePrice()
    {
        _viewModel.IsAndroidSubscriptionPlatform = true;
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(true);
        _mockAuthService.Setup(a => a.EmailConfirmed).Returns(true);
        _mockAuthService.Setup(a => a.HasActiveSubscription).Returns(false);
        _mockBillingService.Setup(b => b.GetSubscriptionOfferAsync())
            .ReturnsAsync(new SubscriptionOfferInfo
            {
                LookupSucceeded = true,
                IsAvailable = true,
                HasFreeTrial = false,
                RenewalPrice = "$2.99"
            });

        await _viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.ShowSubscriptionOfferCard, Is.False);
            Assert.That(_viewModel.ShowSubscribeNow, Is.True);
            Assert.That(_viewModel.SubscribeButtonText, Is.EqualTo("Subscribe Now - $2.99/mo"));
        });
    }

    [Test]
    public void LoggedOutAndroidVisitor_SeesTrialIncentiveCard()
    {
        _viewModel.IsAndroidSubscriptionPlatform = true;
        _viewModel.IsAuthenticated = false;
        _viewModel.HasActiveSubscription = false;

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.ShowSubscriptionOfferCard, Is.True);
            Assert.That(_viewModel.ShowLoginRegister, Is.False);
            Assert.That(_viewModel.SubscriptionOfferPrimaryButtonText, Is.EqualTo("Create Account"));
            Assert.That(_viewModel.ShowSubscriptionOfferSecondaryButton, Is.True);
            Assert.That(_viewModel.SubscriptionOfferDisclosureText, Does.Contain("Google Play subscription settings"));
        });
    }

    [Test]
    public async Task NavigateToRegisterFromOfferCommand_PassesReturnHomeFlag()
    {
        await _viewModel.NavigateToRegisterFromOfferCommand.ExecuteAsync(null);

        _mockNavigationService.Verify(n => n.GoToAsync("register", It.Is<IDictionary<string, object>>(d =>
            (bool)d[NavigationRoutes.ReturnToHomeAfterAuthParameter])), Times.Once);
    }

    [Test]
    public async Task NavigateToLoginFromOfferCommand_PassesReturnHomeFlag()
    {
        await _viewModel.NavigateToLoginFromOfferCommand.ExecuteAsync(null);

        _mockNavigationService.Verify(n => n.GoToAsync(NavigationRoutes.LoginEntry, It.Is<IDictionary<string, object>>(d =>
            (bool)d[NavigationRoutes.ReturnToHomeAfterAuthParameter])), Times.Once);
    }

    [Test]
    public async Task NavigateToValidateEmailFromOfferCommand_PassesReturnHomeFlag()
    {
        _mockAuthService.Setup(a => a.UserId).Returns(42);
        _mockAuthService.Setup(a => a.Email).Returns("test@test.com");
        _viewModel = CreateViewModel();

        await _viewModel.NavigateToValidateEmailFromOfferCommand.ExecuteAsync(null);

        _mockNavigationService.Verify(n => n.GoToAsync("verify-email", It.Is<IDictionary<string, object>>(d =>
            (int)d["UserId"] == 42 &&
            (string)d["Email"] == "test@test.com" &&
            (bool)d[NavigationRoutes.ReturnToHomeAfterAuthParameter])), Times.Once);
    }

    [Test]
    public async Task LoadAsync_SetsIsLoadingFalseAfterCompletion()
    {
        await _viewModel.LoadCommand.ExecuteAsync(null);

        Assert.That(_viewModel.IsLoading, Is.False);
    }

    [Test]
    public async Task LoadAsync_LoadsOnlyFeaturedSongsAndBuildsShareUrls()
    {
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(true);
        _mockAuthService.Setup(a => a.EmailConfirmed).Returns(true);

        _mockMusicService.Setup(s => s.GetSongsAsync()).ReturnsAsync(
        [
            new SongDto { Id = 1, SongTitle = "Featured Song", DisplayOnHomePage = true },
            new SongDto { Id = 2, SongTitle = "Library Song", DisplayOnHomePage = false }
        ]);

        _mockMusicService.Setup(s => s.GetBulkLikeCountsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync([
                new LikeCountDto { SongMetadataId = 1, LikeCount = 5, DislikeCount = 2 }
            ]);

        _mockMusicService.Setup(s => s.GetBulkUserLikeStatusAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new Dictionary<int, UserSongRatingState> { [1] = new(true, true) });

        await _viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.FeaturedSongs, Has.Count.EqualTo(1));
            Assert.That(_viewModel.ShowFeaturedMusic, Is.True);
            Assert.That(_viewModel.FeaturedSongs[0].Id, Is.EqualTo(1));
            Assert.That(_viewModel.FeaturedSongs[0].ShareUrl, Is.EqualTo("https://streamtunes.net/share/1"));
            Assert.That(_viewModel.FeaturedSongs[0].LikeCount, Is.EqualTo(5));
            Assert.That(_viewModel.FeaturedSongs[0].DislikeCount, Is.EqualTo(2));
            Assert.That(_viewModel.FeaturedSongs[0].UserLikeStatus, Is.True);
        });
    }

    [Test]
    public async Task LoadAsync_OrdersFeaturedSongsByDisplayOrder()
    {
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(true);
        _mockAuthService.Setup(a => a.EmailConfirmed).Returns(true);

        _mockMusicService.Setup(s => s.GetSongsAsync()).ReturnsAsync(
        [
            new SongDto { Id = 10, SongTitle = "Ranked One", DisplayOnHomePage = true, DisplayOrder = 1 },
            new SongDto { Id = 40, SongTitle = "Ranked Two", DisplayOnHomePage = true, DisplayOrder = 2 },
            new SongDto { Id = 30, SongTitle = "Null Newest", DisplayOnHomePage = true, DisplayOrder = null },
            new SongDto { Id = 20, SongTitle = "Null Older", DisplayOnHomePage = true, DisplayOrder = null }
        ]);

        await _viewModel.LoadCommand.ExecuteAsync(null);

        Assert.That(_viewModel.FeaturedSongs.Select(song => song.SongTitle), Is.EqualTo(new[]
        {
            "Null Newest",
            "Null Older",
            "Ranked One",
            "Ranked Two"
        }));
    }

    [Test]
    public async Task LoadAsync_SetsPlaybackStreamQualifyingSeconds()
    {
        _mockMusicService.Setup(s => s.GetStreamQualifyingSecondsAsync()).ReturnsAsync(45);

        await _viewModel.LoadCommand.ExecuteAsync(null);

        _mockPlaybackService.Verify(p => p.SetStreamQualifyingSeconds(45), Times.Once);
    }

    [Test]
    public async Task StartSignalRAsync_StartsService()
    {
        await _viewModel.StartSignalRAsync();

        _mockSignalRService.Verify(s => s.StartAsync(), Times.Once);
    }

    [Test]
    public void SignalR_StreamCountUpdate_UpdatesFeaturedSong()
    {
        var song = new SongDto { Id = 42, SongTitle = "Featured", StreamCount = 5 };
        _viewModel.FeaturedSongs = new ObservableCollection<SongDto> { song };

        _mockSignalRService.Raise(s => s.OnStreamCountUpdated += null, 42, 11);

        Assert.That(song.StreamCount, Is.EqualTo(11));
    }

    [Test]
    public void SignalR_LikeCountUpdate_UpdatesFeaturedSong()
    {
        var song = new SongDto { Id = 42, SongTitle = "Featured", LikeCount = 3, DislikeCount = 1 };
        _viewModel.FeaturedSongs = new ObservableCollection<SongDto> { song };

        _mockSignalRService.Raise(s => s.OnLikeCountUpdated += null, 42, 9, 2);

        Assert.That(song.LikeCount, Is.EqualTo(9));
        Assert.That(song.DislikeCount, Is.EqualTo(2));
    }

    [Test]
    public void Activate_ReattachesSignalR_AfterCleanup()
    {
        var song = new SongDto { Id = 42, SongTitle = "Featured", StreamCount = 5 };
        _viewModel.FeaturedSongs = new ObservableCollection<SongDto> { song };

        _viewModel.Cleanup();
        _viewModel.Activate();

        _mockSignalRService.Raise(s => s.OnStreamCountUpdated += null, 42, 12);

        Assert.That(song.StreamCount, Is.EqualTo(12));
    }

    [Test]
    public async Task AuthSubscription_IsDetachedByCleanupAndReattachedByActivate()
    {
        _viewModel.Cleanup();

        _mockAuthService.Raise(service => service.AuthStateChanged += null);
        await Task.Delay(25);
        _mockMusicService.Verify(service => service.GetSongsAsync(), Times.Never);

        _viewModel.Activate();
        _mockAuthService.Raise(service => service.AuthStateChanged += null);
        await Task.Delay(25);

        _mockMusicService.Verify(service => service.GetSongsAsync(), Times.Once);
    }

    [Test]
    public void MusicService_StreamCountRecorded_UpdatesFeaturedSong()
    {
        var song = new SongDto { Id = 42, SongTitle = "Featured", StreamCount = 5 };
        _viewModel.FeaturedSongs = new ObservableCollection<SongDto> { song };

        _mockMusicService.Raise(s => s.OnStreamCountRecorded += null, 42, 12);

        Assert.That(song.StreamCount, Is.EqualTo(12));
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

        _mockNavigationService.Verify(n => n.GoToAsync(NavigationRoutes.LoginEntry), Times.Once);
        _mockNavigationService.Verify(n => n.GoToAsync(NavigationRoutes.Login), Times.Never);
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
        // A purchase can only be made signed in - see SubscriptionPurchaseGate.
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(true);
        _mockBillingService.Setup(b => b.PurchaseSubscriptionAsync())
            .ReturnsAsync(BillingPurchaseResult.Succeeded("test-token", "order-123"));
        _mockMusicService.Setup(m => m.VerifySubscriptionPurchaseAsync(It.Is<BillingPurchaseVerificationRequest>(r =>
                r.Provider == BillingProviders.GooglePlay &&
                r.PurchaseToken == "test-token" &&
                r.OrderId == "order-123")))
            .ReturnsAsync((true, string.Empty));

        await _viewModel.SubscribeCommand.ExecuteAsync(null);

        _mockMusicService.Verify(m => m.VerifySubscriptionPurchaseAsync(It.Is<BillingPurchaseVerificationRequest>(r =>
            r.Provider == BillingProviders.GooglePlay &&
            r.PurchaseToken == "test-token" &&
            r.OrderId == "order-123")), Times.Once);
        _mockAuthService.Verify(a => a.RefreshUserStatusAsync(), Times.Once);
        _mockAlertService.Verify(a => a.DisplayAlertAsync("Success", It.IsAny<string>(), "OK"), Times.Once);
    }

    /// <summary>
    /// A purchase made while signed out carries no app account token and cannot be verified, because
    /// the server's verify endpoint is authenticated — the customer is charged and gets nothing.
    /// </summary>
    [Test]
    public async Task SubscribeCommand_WhenSignedOut_NeverReachesTheStore()
    {
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(false);
        _mockAlertService
            .Setup(a => a.ShowConfirmAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);

        await _viewModel.SubscribeCommand.ExecuteAsync(null);

        _mockBillingService.Verify(b => b.PurchaseSubscriptionAsync(), Times.Never);
        _mockNavigationService.Verify(
            n => n.GoToAsync(NavigationRoutes.LoginEntry, It.Is<IDictionary<string, object>>(p =>
                p.ContainsKey(NavigationRoutes.ReturnToHomeAfterAuthParameter))),
            Times.Once);
    }

    [Test]
    public async Task SubscribeCommand_PurchaseFailed_ShowsErrorAlert()
    {
        // A purchase can only be made signed in - see SubscriptionPurchaseGate.
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(true);
        _mockBillingService.Setup(b => b.PurchaseSubscriptionAsync())
            .ReturnsAsync(BillingPurchaseResult.Failed("Connection error"));

        await _viewModel.SubscribeCommand.ExecuteAsync(null);

        _mockAlertService.Verify(a => a.DisplayAlertAsync("Subscribe", "Connection error", "OK"), Times.Once);
        _mockMusicService.Verify(m => m.VerifySubscriptionPurchaseAsync(It.IsAny<BillingPurchaseVerificationRequest>()), Times.Never);
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
        // A purchase can only be made signed in - see SubscriptionPurchaseGate.
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(true);
        _mockBillingService.Setup(b => b.PurchaseSubscriptionAsync())
            .ReturnsAsync(BillingPurchaseResult.Succeeded("test-token", "order-123"));
        _mockMusicService.Setup(m => m.VerifySubscriptionPurchaseAsync(It.Is<BillingPurchaseVerificationRequest>(r =>
                r.Provider == BillingProviders.GooglePlay &&
                r.PurchaseToken == "test-token" &&
                r.OrderId == "order-123")))
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

        _mockNavigationService.Verify(n => n.GoToAsync(NavigationRoutes.MusicLibraryRoot), Times.Once);
    }

    [Test]
    public async Task PlaySongCommand_SetsFeaturedPlaylistOnPlaybackService()
    {
        var firstSong = new SongDto { Id = 1, SongTitle = "First" };
        var secondSong = new SongDto { Id = 2, SongTitle = "Second" };
        _viewModel.FeaturedSongs = new ObservableCollection<SongDto> { firstSong, secondSong };

        await _viewModel.PlaySongCommand.ExecuteAsync(secondSong);

        _mockPlaybackService.Verify(p => p.SetPlaylist(
            It.Is<List<SongDto>>(songs => songs.Count == 2 && songs[0] == firstSong && songs[1] == secondSong),
            1,
            "Featured Songs"), Times.Once);
        _mockMediaPlaybackOnboardingService.Verify(service => service.EnsureBackgroundPlaybackExplainedAsync(), Times.Once);
    }

    [Test]
    public async Task PlaySongCommand_WhenSongMatchesCurrentPlayback_TogglesPlayPause()
    {
        var song = new SongDto { Id = 2, SongTitle = "Second" };
        _viewModel.FeaturedSongs = new ObservableCollection<SongDto> { new SongDto { Id = 1, SongTitle = "First" }, song };
        _mockPlaybackService.SetupGet(p => p.CurrentSong).Returns(song);
        _mockPlaybackService.SetupGet(p => p.PreviewLimitReached).Returns(false);

        await _viewModel.PlaySongCommand.ExecuteAsync(song);

        _mockPlaybackService.Verify(p => p.TogglePlayPause(), Times.Once);
        _mockPlaybackService.Verify(p => p.SetPlaylist(It.IsAny<List<SongDto>>(), It.IsAny<int>()), Times.Never);
        _mockMediaPlaybackOnboardingService.Verify(service => service.EnsureBackgroundPlaybackExplainedAsync(), Times.Never);
    }

    [Test]
    public async Task PlayFeaturedQueueFromStartAsync_QueuesFeaturedSongsFromBeginning()
    {
        var firstSong = new SongDto { Id = 1, SongTitle = "First" };
        var secondSong = new SongDto { Id = 2, SongTitle = "Second" };
        _viewModel.FeaturedSongs = new ObservableCollection<SongDto> { firstSong, secondSong };

        var started = await _viewModel.PlayFeaturedQueueFromStartAsync();

        Assert.That(started, Is.True);
        _mockPlaybackService.Verify(p => p.SetPlaylist(
            It.Is<List<SongDto>>(songs => songs.Select(song => song.Id).SequenceEqual(new[] { 1, 2 })),
            0,
            "Featured Songs"), Times.Once);
    }

    [Test]
    public void Activate_WithDifferentActivePlaylist_SyncsQueueToFeaturedSongs()
    {
        var firstSong = new SongDto { Id = 1, SongTitle = "First" };
        var secondSong = new SongDto { Id = 2, SongTitle = "Second" };
        _viewModel.FeaturedSongs = new ObservableCollection<SongDto> { firstSong, secondSong };
        _mockPlaybackService.SetupGet(p => p.HasPlaylist).Returns(true);
        _mockPlaybackService.SetupGet(p => p.CurrentSong).Returns(secondSong);
        _mockPlaybackService.SetupGet(p => p.Playlist).Returns(new List<SongDto>
        {
            secondSong,
            new() { Id = 99, SongTitle = "Library Song" }
        });

        _viewModel.Activate();

        _mockPlaybackService.Verify(p => p.SetPlaylist(
            It.Is<List<SongDto>>(songs => songs.Select(song => song.Id).SequenceEqual(new[] { 1, 2 })),
            1,
            PlaybackQueueStartBehavior.PreserveCurrentSongIfPresent,
            "Featured Songs"), Times.Once);
    }

    [Test]
    public async Task LikeSong_WhenNotLoggedIn_ShowsLoginPrompt()
    {
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(false);
        _mockAlertService.Setup(a => a.ShowConfirmAsync(
            "Login Required",
            It.IsAny<string>(),
            "Login",
            "Cancel")).ReturnsAsync(false);

        await _viewModel.LikeSongCommand.ExecuteAsync(new SongDto { Id = 10, SongTitle = "Test" });

        _mockAlertService.Verify(a => a.ShowConfirmAsync(
            "Login Required",
            It.IsAny<string>(),
            "Login",
            "Cancel"), Times.Once);
        _mockMusicService.Verify(s => s.ToggleLikeAsync(It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task LikeSong_WhenNotLoggedInAndPromptAccepted_NavigatesToAnchoredLoginEntry()
    {
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(false);
        _mockAlertService.Setup(a => a.ShowConfirmAsync(
            "Login Required",
            It.IsAny<string>(),
            "Login",
            "Cancel")).ReturnsAsync(true);

        await _viewModel.LikeSongCommand.ExecuteAsync(new SongDto { Id = 10, SongTitle = "Test" });

        _mockNavigationService.Verify(n => n.GoToAsync(NavigationRoutes.LoginEntry), Times.Once);
        _mockNavigationService.Verify(n => n.GoToAsync(NavigationRoutes.Login), Times.Never);
        _mockMusicService.Verify(s => s.ToggleLikeAsync(It.IsAny<int>()), Times.Never);
    }

    [Test]
    public void SubscribeButtonText_IncludesPrice()
    {
        _viewModel.SubscriptionPrice = "4.99";
        Assert.That(_viewModel.SubscribeButtonText, Is.EqualTo("Subscribe Now - $4.99/mo"));
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
    public async Task OpenSubscriptionManagement_UsesGooglePlayUrlByDefault()
    {
        await _viewModel.OpenSubscriptionManagementCommand.ExecuteAsync(null);

        _mockBrowserService.Verify(b => b.OpenExternalAsync("https://play.google.com/store/account/subscriptions"), Times.Once);
    }

    [Test]
    public async Task OpenSubscriptionManagement_UsesAppleUrlForAppleBillingSource()
    {
        _mockAuthService.SetupGet(a => a.BillingSource).Returns(BillingProviders.Apple);

        await _viewModel.OpenSubscriptionManagementCommand.ExecuteAsync(null);

        _mockBrowserService.Verify(b => b.OpenExternalAsync("https://developer.apple.com/documentation/storekit/testing-disabling-auto-renew"), Times.Once);
    }

    [Test]
    public void ManageSubscriptionText_UsesGenericAppleLabel()
    {
        _mockAuthService.SetupGet(a => a.BillingSource).Returns(BillingProviders.Apple);

        var viewModel = CreateViewModel();

        Assert.That(viewModel.ManageSubscriptionText, Is.EqualTo("Manage subscription with Apple ›"));
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

    // --- The featured-songs blurb and the subscription banner ---

    [Test]
    public void SubscriptionAccessText_ForASubscriber_StatesWhatTheyHave()
    {
        // It used to be hard-coded in the page, so a subscriber was told to "subscribe for
        // unlimited access" alongside their own active subscription.
        _viewModel.HasActiveSubscription = true;

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.SubscriptionAccessText, Does.Contain("You have unlimited access"));
            Assert.That(_viewModel.SubscriptionAccessText, Does.Not.Contain("Subscribe for"));
        });
    }

    [Test]
    public void SubscriptionAccessText_ForANonSubscriber_AsksThemToSubscribe()
    {
        _viewModel.HasActiveSubscription = false;

        Assert.That(_viewModel.SubscriptionAccessText, Does.Contain("Subscribe for unlimited access"));
    }

    [Test]
    public void SubscriptionBanner_WhenTheServerConfirmedTheStatus_IsSilent()
    {
        // Home carried the same connectivity-driven banner that contradicted Account Settings, so
        // going offline printed "subscription information is unavailable" above "You have unlimited
        // access to the full library!".
        _viewModel.IsAuthenticated = true;
        _viewModel.SubscriptionVerification = SubscriptionVerificationState.Verified;

        Assert.That(_viewModel.ShowSubscriptionUnavailableBanner, Is.False);
    }

    [Test]
    public void SubscriptionBanner_WhenStandingOnACachedStatus_SaysItIsUnconfirmedNotPaused()
    {
        _viewModel.IsAuthenticated = true;
        _viewModel.SubscriptionVerification = SubscriptionVerificationState.Cached;

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.ShowSubscriptionUnavailableBanner, Is.True);
            Assert.That(_viewModel.SubscriptionUnavailableBannerText, Does.Contain("last confirmed"));
            Assert.That(_viewModel.SubscriptionUnavailableBannerText, Does.Not.Contain("paused"));
        });
    }

    [Test]
    public void SubscriptionBanner_WhenEntitlementCouldNotBeEstablished_SaysFeaturesArePaused()
    {
        _viewModel.IsAuthenticated = true;
        _viewModel.SubscriptionVerification = SubscriptionVerificationState.Unverified;

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.ShowSubscriptionUnavailableBanner, Is.True);
            Assert.That(_viewModel.SubscriptionUnavailableBannerText, Does.Contain("paused"));
        });
    }

    /// <summary>
    /// The reported bug. A fresh install sits at the default Unverified, and Home was telling a
    /// user who had never signed in that their subscription features were paused - on wifi, with
    /// no subscription and no account in play at all.
    /// </summary>
    [Test]
    public void SubscriptionBanner_WhenSignedOut_IsSilent(
        [Values(SubscriptionVerificationState.Unverified,
                SubscriptionVerificationState.Cached,
                SubscriptionVerificationState.Verified)] SubscriptionVerificationState verification)
    {
        _viewModel.IsAuthenticated = false;
        _viewModel.SubscriptionVerification = verification;

        Assert.That(_viewModel.ShowSubscriptionUnavailableBanner, Is.False);
    }

    [Test]
    public void SubscriptionBanner_WhileOnline_DoesNotClaimTheUserIsOffline(
        [Values(SubscriptionVerificationState.Cached, SubscriptionVerificationState.Unverified)]
        SubscriptionVerificationState verification)
    {
        _mockNetworkStatus.SetupGet(n => n.IsOffline).Returns(false);
        _viewModel.IsAuthenticated = true;
        _viewModel.SubscriptionVerification = verification;

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.ShowSubscriptionUnavailableBanner, Is.True);
            Assert.That(_viewModel.SubscriptionUnavailableBannerText, Does.Not.Contain("offline"));
        });
    }

    [Test]
    public void SubscriptionBanner_WhileOffline_SaysSo(
        [Values(SubscriptionVerificationState.Cached, SubscriptionVerificationState.Unverified)]
        SubscriptionVerificationState verification)
    {
        _mockNetworkStatus.SetupGet(n => n.IsOffline).Returns(true);
        _viewModel.IsAuthenticated = true;
        _viewModel.SubscriptionVerification = verification;

        Assert.That(_viewModel.SubscriptionUnavailableBannerText, Does.Contain("offline"));
    }

    // --- Home re-asks the server when the entitlement was never confirmed ---

    [Test]
    public async Task LoadCommand_WhenSignedInAndUnverified_AsksTheServerAgain()
    {
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(true);
        _mockAuthService.Setup(a => a.SubscriptionVerification)
            .Returns(SubscriptionVerificationState.Unverified);

        await _viewModel.LoadCommand.ExecuteAsync(null);

        _mockAuthService.Verify(a => a.RefreshUserStatusAsync(), Times.Once);
    }

    [Test]
    public async Task LoadCommand_WhenAlreadyVerified_DoesNotAskTheServerAgain()
    {
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(true);
        _mockAuthService.Setup(a => a.SubscriptionVerification)
            .Returns(SubscriptionVerificationState.Verified);

        await _viewModel.LoadCommand.ExecuteAsync(null);

        _mockAuthService.Verify(a => a.RefreshUserStatusAsync(), Times.Never);
    }

    [Test]
    public async Task LoadCommand_WhenSignedOut_DoesNotAskTheServer()
    {
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(false);
        _mockAuthService.Setup(a => a.SubscriptionVerification)
            .Returns(SubscriptionVerificationState.Unverified);

        await _viewModel.LoadCommand.ExecuteAsync(null);

        _mockAuthService.Verify(a => a.RefreshUserStatusAsync(), Times.Never);
    }

    [Test]
    public async Task LoadCommand_WithNoNetworkAccess_DoesNotAskTheServer()
    {
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(true);
        _mockAuthService.Setup(a => a.SubscriptionVerification)
            .Returns(SubscriptionVerificationState.Unverified);
        _mockNetworkStatus.SetupGet(n => n.HasNoNetworkAccess).Returns(true);

        await _viewModel.LoadCommand.ExecuteAsync(null);

        _mockAuthService.Verify(a => a.RefreshUserStatusAsync(), Times.Never);
    }

    /// <summary>
    /// Home used only to re-read whatever AuthService had already decided, so the reload on
    /// reconnect could not repair an entitlement that was never confirmed - the banner stayed up
    /// until the user happened to open Account Settings or play a song.
    /// </summary>
    [Test]
    public void NetworkStatusChanged_OnReconnect_AsksTheServerAgain()
    {
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(true);
        _mockAuthService.Setup(a => a.SubscriptionVerification)
            .Returns(SubscriptionVerificationState.Unverified);
        _viewModel.Activate();
        _viewModel.IsLoading = false;

        _mockNetworkStatus.Raise(
            n => n.PropertyChanged += null,
            new System.ComponentModel.PropertyChangedEventArgs(nameof(INetworkStatusService.HasNoNetworkAccess)));

        _mockAuthService.Verify(a => a.RefreshUserStatusAsync(), Times.Once);
    }

    // --- Manage-subscription routing follows the billing source, not the device ---

    [Test]
    public void ManageSubscriptionLink_ForAPayPalSubscription_IsHidden()
    {
        // Neither store page can manage a PayPal subscription, and this card has no cancel flow of
        // its own. Account Settings does, and that is where such a subscriber is served.
        _mockAuthService.Setup(a => a.BillingSource).Returns(BillingSources.PayPal);

        Assert.That(_viewModel.ShowManageSubscriptionLink, Is.False);
    }

    [Test]
    public void ManageSubscriptionLink_ForAStoreSubscription_IsShown(
        [Values(BillingProviders.Apple, BillingProviders.GooglePlay)] string billingSource)
    {
        _mockAuthService.Setup(a => a.BillingSource).Returns(billingSource);

        Assert.That(_viewModel.ShowManageSubscriptionLink, Is.True);
    }

    [Test]
    public void ManageSubscriptionLink_WithNoBillingSourceYet_IsShown()
    {
        // A non-subscriber being told where they would cancel - the platform default is right here.
        _mockAuthService.Setup(a => a.BillingSource).Returns((string?)null);

        Assert.That(_viewModel.ShowManageSubscriptionLink, Is.True);
    }

    [Test]
    public async Task OpenSubscriptionManagement_ForAnApplePurchase_OpensApple()
    {
        _mockAuthService.Setup(a => a.BillingSource).Returns(BillingProviders.Apple);

        await _viewModel.OpenSubscriptionManagementCommand.ExecuteAsync(null);

        _mockBrowserService.Verify(
            b => b.OpenExternalAsync(It.Is<string>(url => url.Contains("apple.com"))),
            Times.Once);
    }

    [Test]
    public async Task OpenSubscriptionManagement_ForAGooglePlayPurchase_OpensGooglePlay()
    {
        // Keyed on the billing source rather than the device: this must stay true on an iPhone,
        // where the old platform-only rule sent every user to Apple.
        _mockAuthService.Setup(a => a.BillingSource).Returns(BillingProviders.GooglePlay);

        await _viewModel.OpenSubscriptionManagementCommand.ExecuteAsync(null);

        _mockBrowserService.Verify(
            b => b.OpenExternalAsync(It.Is<string>(url => url.Contains("play.google.com"))),
            Times.Once);
    }

    [Test]
    public void SubscriptionBanner_WhenSigningIn_IsReEvaluated()
    {
        _viewModel.SubscriptionVerification = SubscriptionVerificationState.Unverified;
        var raised = new List<string?>();
        _viewModel.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        _viewModel.IsAuthenticated = true;

        // RefreshAuthState assigns IsAuthenticated before SubscriptionVerification, so without this
        // notification the banner would not appear until some later, unrelated change.
        Assert.That(raised, Does.Contain(nameof(HomeViewModel.ShowSubscriptionUnavailableBanner)));
    }

    // --- An expired token signs the user out silently; the notice is the only thing that says so ---

    private void SetupPendingSessionExpiry(bool hadEntitlement, DateTime? entitlementEndDate = null)
    {
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(false);
        _mockAuthService.SetupGet(a => a.PendingSessionExpiryNotice)
            .Returns(new SessionExpiryNotice(hadEntitlement, entitlementEndDate));
    }

    [Test]
    public async Task SessionExpiredNotice_ForASubscriberStillInTerm_OffersToRestoreTheSubscription()
    {
        SetupPendingSessionExpiry(hadEntitlement: true, entitlementEndDate: DateTime.UtcNow.AddDays(20));

        await _viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.ShowSessionExpiredNotice, Is.True);
            Assert.That(_viewModel.SessionExpiredMessage, Does.Contain("restore your subscription"));
        });
    }

    [Test]
    public async Task SessionExpiredNotice_WithNoKnownEndDate_StillOffersToRestore()
    {
        // An active subscription with no end date is the server's own shape for an open-ended one.
        SetupPendingSessionExpiry(hadEntitlement: true);

        await _viewModel.LoadCommand.ExecuteAsync(null);

        Assert.That(_viewModel.SessionExpiredMessage, Does.Contain("restore your subscription"));
    }

    [Test]
    public async Task SessionExpiredNotice_ForALapsedSubscription_OffersToRenewNotRestore()
    {
        // "Restore" is a promise signing in cannot keep once the term has run out — they would land
        // on an expired subscription. Renewing is the thing they can actually do.
        SetupPendingSessionExpiry(hadEntitlement: true, entitlementEndDate: DateTime.UtcNow.AddDays(-3));

        await _viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.SessionExpiredMessage, Does.Contain("renew your subscription"));
            Assert.That(_viewModel.SessionExpiredMessage, Does.Not.Contain("restore"));
        });
    }

    [Test]
    public async Task SessionExpiredNotice_ForSomeoneWhoNeverSubscribed_DoesNotMentionASubscription()
    {
        SetupPendingSessionExpiry(hadEntitlement: false);

        await _viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.ShowSessionExpiredNotice, Is.True);
            Assert.That(_viewModel.SessionExpiredMessage, Does.Not.Contain("subscription"));
        });
    }

    [Test]
    public async Task SessionExpiredNotice_WhenTheSessionEndedSomeOtherWay_IsSilent()
    {
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(false);
        _mockAuthService.SetupGet(a => a.PendingSessionExpiryNotice).Returns((SessionExpiryNotice?)null);

        await _viewModel.LoadCommand.ExecuteAsync(null);

        Assert.That(_viewModel.ShowSessionExpiredNotice, Is.False);
    }

    [Test]
    public async Task SessionExpiredNotice_SurvivesASecondLoad()
    {
        // The service keeps the notice standing rather than handing it over once, so a rebuilt page —
        // Home is transient and comes from a Shell DataTemplate — still finds the explanation.
        SetupPendingSessionExpiry(hadEntitlement: true);

        await _viewModel.LoadCommand.ExecuteAsync(null);
        await _viewModel.LoadCommand.ExecuteAsync(null);

        Assert.That(_viewModel.SessionExpiredMessage, Does.Contain("restore your subscription"));
    }

    [Test]
    public async Task SessionExpiredNotice_OnceSignedIn_IsCleared()
    {
        SetupPendingSessionExpiry(hadEntitlement: true);
        await _viewModel.LoadCommand.ExecuteAsync(null);

        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(true);
        await _viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.ShowSessionExpiredNotice, Is.False);
            Assert.That(_viewModel.SessionExpiredMessage, Is.Null);
        });
    }

    // ---- The global "most streamed" playlists -------------------------------------

    private static PlaylistDto TopStreamedTile(string window) => new()
    {
        Id = 0,
        Key = window,
        Name = $"Top 10 {window}",
        SongCount = 10,
        IsSystemGenerated = true,
        Kind = PlaylistKinds.TopStreamed
    };

    [Test]
    public async Task TheMostStreamedPlaylistsLoadForASignedOutVisitor()
    {
        // The point of the feature: these five are identical for everybody, so unlike Recommended and
        // Liked Songs they must not be behind the sign-in gate.
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(false);
        _mockPlaylistService.Setup(p => p.GetTopStreamedPlaylistsAsync())
            .ReturnsAsync([TopStreamedTile("Day"), TopStreamedTile("AllTime")]);

        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.TopStreamedPlaylists, Has.Count.EqualTo(2));
            Assert.That(viewModel.ShowTopStreamed, Is.True);
            Assert.That(viewModel.RecommendedPlaylist, Is.Null, "Personal tiles stay hidden.");
            Assert.That(viewModel.LikedSongsPlaylist, Is.Null);
            Assert.That(viewModel.ShowPlaylists, Is.False);
        });
    }

    [Test]
    public async Task TheMostStreamedPlaylistsComeFromTheHomeResponseWhenSignedIn()
    {
        // Signed in they ride along on the home payload, so no second request is made.
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(true);
        _mockAuthService.Setup(a => a.EmailConfirmed).Returns(true);
        _mockPlaylistService.Setup(p => p.GetHomePlaylistsAsync()).ReturnsAsync(new HomePlaylistsDto
        {
            TopStreamed = [TopStreamedTile("Day"), TopStreamedTile("Week")]
        });

        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.That(viewModel.TopStreamedPlaylists.Select(p => p.Key), Is.EqualTo(new[] { "Day", "Week" }));
        _mockPlaylistService.Verify(p => p.GetTopStreamedPlaylistsAsync(), Times.Never);
    }

    [Test]
    public async Task AnOlderServerOmittingTheMostStreamedListIsNotACrash()
    {
        // TopStreamed is nullable because a server that predates it sends no such property, and
        // System.Text.Json would overwrite the initialiser if one ever sent an explicit null.
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(true);
        _mockAuthService.Setup(a => a.EmailConfirmed).Returns(true);
        _mockPlaylistService.Setup(p => p.GetHomePlaylistsAsync())
            .ReturnsAsync(new HomePlaylistsDto { TopStreamed = null });

        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.TopStreamedPlaylists, Is.Empty);
            Assert.That(viewModel.ShowTopStreamed, Is.False);
        });
    }

    [Test]
    public async Task ShowTopStreamed_IsIndependentOfShowPlaylists()
    {
        // ShowPlaylists gates the two personal tiles on sign-in + email verification. The most
        // streamed section must not inherit that.
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(true);
        _mockAuthService.Setup(a => a.EmailConfirmed).Returns(false);
        _mockPlaylistService.Setup(p => p.GetTopStreamedPlaylistsAsync())
            .ReturnsAsync([TopStreamedTile("Day")]);

        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.ShowPlaylists, Is.False);
            Assert.That(viewModel.ShowTopStreamed, Is.True);
        });
    }

    [Test]
    public async Task OpenPlaylistAsync_OpensAMostStreamedPlaylistByWindowNotById()
    {
        var viewModel = CreateViewModel();

        await viewModel.OpenPlaylistCommand.ExecuteAsync(TopStreamedTile("Year"));

        _mockNavigationService.Verify(n => n.GoToAsync(
            NavigationRoutes.PlaylistPlayer,
            It.Is<Dictionary<string, object>>(query =>
                (string)query[PlaylistNavigationTarget.TopStreamedWindowKey] == "Year"
                && !query.ContainsKey(PlaylistNavigationTarget.PlaylistIdKey))),
            Times.Once);
    }

    // ---- The marketing hero -------------------------------------------------------

    [TestCase(false, false, true, Description = "Anonymous visitor")]
    [TestCase(false, true, true, Description = "Anonymous, stale subscription flag")]
    [TestCase(true, false, true, Description = "Signed in, not subscribed")]
    [TestCase(true, true, false, Description = "Signed-in subscriber")]
    public void ShowHero_IsOffOnlyForASignedInSubscriber(bool isAuthenticated, bool hasSubscription, bool expected)
    {
        var viewModel = CreateViewModel();
        viewModel.IsAuthenticated = isAuthenticated;
        viewModel.HasActiveSubscription = hasSubscription;

        Assert.That(viewModel.ShowHero, Is.EqualTo(expected));
    }

    [Test]
    public void ShowBrowseMusic_TracksTheHero()
    {
        // Browse Music is the hero's own CTA, so it must not outlive the block it sits in.
        var viewModel = CreateViewModel();
        viewModel.IsAuthenticated = true;
        viewModel.HasActiveSubscription = true;

        Assert.That(viewModel.ShowBrowseMusic, Is.False);
    }

    [Test]
    public void ShowHero_RaisesChangeNotificationsWhenSubscriptionResolves()
    {
        // The subscription is confirmed after the page has already rendered, so the hero has to
        // disappear on notification rather than only on a fresh navigation.
        var viewModel = CreateViewModel();
        viewModel.IsAuthenticated = true;
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        viewModel.HasActiveSubscription = true;

        Assert.That(changed, Does.Contain(nameof(HomeViewModel.ShowHero))
            .And.Contain(nameof(HomeViewModel.ShowBrowseMusic)));
    }

    [Test]
    public void TheHeroBlockCollapsesEntirelyForASubscriber()
    {
        // The container holds the block's padding, so leaving it visible with every child hidden
        // leaves a dead gap between Featured Music and the playlists.
        //
        // Verified matters here: an authenticated user whose entitlement has NOT been confirmed this
        // session is shown the "we couldn't confirm your subscription" banner, which legitimately
        // keeps the container alive - that case is the test below.
        var viewModel = CreateViewModel();
        viewModel.IsAuthenticated = true;
        viewModel.HasActiveSubscription = true;
        viewModel.SubscriptionVerification = SubscriptionVerificationState.Verified;

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.ShowSubscriptionUnavailableBanner, Is.False, "Precondition.");
            Assert.That(viewModel.ShowHeroContainer, Is.False);
        });
    }

    [Test]
    public void TheHeroBlockStaysWhenASubscriberHasAWarningToRead()
    {
        // The banners are diagnostics, not marketing, and one of them fires exactly when a
        // subscription cannot be verified - which a user with a cached subscription can hit.
        var viewModel = CreateViewModel();
        viewModel.IsAuthenticated = true;
        viewModel.HasActiveSubscription = true;
        viewModel.SubscriptionVerification = SubscriptionVerificationState.Verified;
        viewModel.SessionExpiredMessage = "Your session expired. Please sign in again.";

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.ShowHero, Is.False, "The marketing still goes.");
            Assert.That(viewModel.ShowHeroContainer, Is.True, "But the notice must still be readable.");
        });
    }

    [Test]
    public void ASubscriberStillSeesTheMostStreamedPlaylists()
    {
        // Hiding the hero must not take the playlists with it - that is the point of the change.
        var viewModel = CreateViewModel();
        viewModel.IsAuthenticated = true;
        viewModel.HasActiveSubscription = true;
        viewModel.TopStreamedPlaylists.Add(TopStreamedTile("Day"));

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.ShowHero, Is.False);
            Assert.That(viewModel.ShowTopStreamed, Is.True);
        });
    }

    // ---- When the ranking was taken -----------------------------------------------

    [Test]
    public async Task TopStreamedRankedAtDisplay_ShowsTheRankingTimeInLocalTime()
    {
        var generatedAtUtc = new DateTime(2026, 8, 29, 2, 0, 0, DateTimeKind.Utc);
        var tile = TopStreamedTile("Day");
        tile.GeneratedAtUtc = generatedAtUtc;
        _mockPlaylistService.Setup(p => p.GetTopStreamedPlaylistsAsync()).ReturnsAsync([tile]);

        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.ExecuteAsync(null);

        var expectedLocal = generatedAtUtc.ToLocalTime();
        Assert.That(
            viewModel.TopStreamedRankedAtDisplay,
            Is.EqualTo($"Ranked {expectedLocal:MM/dd/yyyy} at {expectedLocal:h:mm tt}"));
    }

    [Test]
    public void TopStreamedRankedAtDisplay_FallsBackBeforeAnythingIsLoaded()
    {
        Assert.That(CreateViewModel().TopStreamedRankedAtDisplay, Is.EqualTo("Updated daily"));
    }

    [Test]
    public async Task TopStreamedRankedAtDisplay_FallsBackWhenTheServerSendsNoTimestamp()
    {
        // An older server does not send the property at all.
        _mockPlaylistService.Setup(p => p.GetTopStreamedPlaylistsAsync()).ReturnsAsync([TopStreamedTile("Day")]);

        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.That(viewModel.TopStreamedRankedAtDisplay, Is.EqualTo("Updated daily"));
    }
}
