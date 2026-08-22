using Microsoft.Extensions.Configuration;
using Moq;
using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.ViewModels;
using System.Globalization;

namespace MusicSalesApp.Maui.Tests.ViewModels;

[TestFixture]
public class AccountSettingsViewModelTests
{
    private Mock<IAuthService> _mockAuthService;
    private Mock<INetworkStatusService> _mockNetworkStatus;
    private Mock<IAlertService> _mockAlertService;
    private Mock<INavigationService> _mockNavigationService;
    private Mock<IBrowserService> _mockBrowserService;
    private Mock<IConfiguration> _mockConfiguration;
    private Mock<IMusicService> _mockMusicService;
    private Mock<IBillingService> _mockBillingService;
    private AccountSettingsViewModel _viewModel;

    [SetUp]
    public void Setup()
    {
        _mockAuthService = new Mock<IAuthService>();
        _mockNetworkStatus = new Mock<INetworkStatusService>();
        _mockAlertService = new Mock<IAlertService>();
        _mockNavigationService = new Mock<INavigationService>();
        _mockBrowserService = new Mock<IBrowserService>();
        _mockConfiguration = new Mock<IConfiguration>();
        _mockMusicService = new Mock<IMusicService>();
        _mockBillingService = new Mock<IBillingService>();

        _mockConfiguration.Setup(c => c["AppleAppStore:SubscriptionManagementUrl"])
            .Returns("https://developer.apple.com/documentation/storekit/testing-disabling-auto-renew");

        _mockAuthService.Setup(a => a.Email).Returns("test@example.com");
        // This page is only reachable signed in, so that is the state the fixture models.
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(true);
        _mockAuthService.Setup(a => a.HasActiveSubscription).Returns(false);
        _mockAuthService.Setup(a => a.IsCreator).Returns(false);
        _mockMusicService.Setup(m => m.GetSubscriptionStatusAsync())
            .ReturnsAsync(new SubscriptionStatusDto { HasSubscription = false });

        _viewModel = CreateViewModel();
    }

    private AccountSettingsViewModel CreateViewModel()
    {
        return new AccountSettingsViewModel(
            _mockAuthService.Object,
            _mockAlertService.Object,
            _mockNavigationService.Object,
            _mockBrowserService.Object,
            _mockConfiguration.Object,
            _mockMusicService.Object,
            _mockBillingService.Object,
            _mockNetworkStatus.Object);
    }

    [Test]
    public void InitialState_PropertiesSetFromAuthService()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.UserEmail, Is.EqualTo("test@example.com"));
            Assert.That(_viewModel.HasActiveSubscription, Is.False);
            Assert.That(_viewModel.IsActiveCreator, Is.False);
            Assert.That(_viewModel.ShowCancelSubscription, Is.False);
            Assert.That(_viewModel.ShowDeleteConfirmation, Is.False);
            Assert.That(_viewModel.ConfirmationText, Is.EqualTo(string.Empty));
            Assert.That(_viewModel.ErrorMessage, Is.EqualTo(string.Empty));
            Assert.That(_viewModel.IsCancelling, Is.False);
            Assert.That(_viewModel.IsDeleting, Is.False);
            Assert.That(_viewModel.SubscriptionPrice, Is.Empty);
            Assert.That(_viewModel.SubscriptionPriceDisplay, Is.Empty);
        });
    }

    // --- The subscription banner reports how current the displayed status is, not connectivity ---

    [Test]
    public void SubscriptionBanner_WhenTheServerConfirmedTheStatus_IsSilent()
    {
        // Being offline is not itself worth saying anything about: a status the server confirmed
        // this session is correct, and "subscription information is unavailable" printed above
        // "Active" is a contradiction that only the banner was wrong about.
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

    [Test]
    public void SubscriptionBanner_WhenTheSessionEndedUnderneathThePage_IsSilent()
    {
        // The flyout only offers this page while signed in, but an expiry can sign the user out with
        // the page already open. Nobody signed out has a subscription to confirm.
        _viewModel.IsAuthenticated = false;
        _viewModel.SubscriptionVerification = SubscriptionVerificationState.Unverified;

        Assert.That(_viewModel.ShowSubscriptionUnavailableBanner, Is.False);
    }

    [Test]
    public void SubscriptionBanner_WhileOnline_DoesNotClaimTheUserIsOffline(
        [Values(SubscriptionVerificationState.Cached, SubscriptionVerificationState.Unverified)]
        SubscriptionVerificationState verification)
    {
        _mockNetworkStatus.Setup(n => n.IsOffline).Returns(false);
        _viewModel.IsAuthenticated = true;
        _viewModel.SubscriptionVerification = verification;

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.ShowSubscriptionUnavailableBanner, Is.True);
            Assert.That(_viewModel.SubscriptionUnavailableBannerText, Does.Not.Contain("offline"));
        });
    }

    [Test]
    public void SubscriptionVerification_DefaultsToUnverified()
    {
        // The safe default: an uninitialised value must not claim the server confirmed anything.
        Assert.That(default(SubscriptionVerificationState), Is.EqualTo(SubscriptionVerificationState.Unverified));
    }

    // --- Biometric sign-in is kept across logout, so this page is the only way to withdraw it ---

    /// <summary>An Android-shaped answer: a prompt is available and is called "your fingerprint or face".</summary>
    private void GiveTheDeviceBiometrics()
        => _mockAuthService.Setup(a => a.GetBiometricAvailabilityAsync())
            .ReturnsAsync(new BiometricAvailability(true, BiometricMethod.Fingerprint, "your fingerprint or face", "Fingerprint"));

    [Test]
    public async Task LoadCommand_ReportsWhetherBiometricCredentialsAreSaved()
    {
        GiveTheDeviceBiometrics();
        _mockAuthService.Setup(a => a.HasBiometricCredentialsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await _viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.IsBiometricLoginEnabled, Is.True);
            Assert.That(_viewModel.BiometricLoginStatusText, Does.Contain("saved on this device"));
        });
    }

    [Test]
    public async Task LoadCommand_WhenTheDeviceHasNoBiometrics_DoesNotOfferIt()
    {
        // Offering it where no prompt can appear invites the user to manage a setting that cannot
        // work. It is also two Keystore reads with nothing to do.
        _mockAuthService.Setup(a => a.GetBiometricAvailabilityAsync())
            .ReturnsAsync(BiometricAvailability.Unavailable);
        _mockAuthService.Setup(a => a.HasBiometricCredentialsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await _viewModel.LoadCommand.ExecuteAsync(null);

        Assert.That(_viewModel.IsBiometricLoginEnabled, Is.False);
        _mockAuthService.Verify(a => a.HasBiometricCredentialsAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task TurnOffBiometricLogin_WhenTheRemovalFails_SaysSoInsteadOfCrashing()
    {
        // A RelayCommand rethrows onto the sync context, so an unguarded keystore failure here is an
        // app crash from a settings tap — and the switch would still have flipped to "off" over
        // credentials that are all still there.
        GiveTheDeviceBiometrics();
        _viewModel.IsBiometricLoginEnabled = true;
        _mockAlertService.Setup(a => a.ShowConfirmAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);
        _mockAuthService.Setup(a => a.DisableBiometricLoginAsync())
            .ThrowsAsync(new InvalidOperationException("keystore unavailable"));

        await _viewModel.TurnOffBiometricLoginCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.ErrorMessage, Does.Contain("Could not remove"));
            Assert.That(_viewModel.IsBiometricLoginEnabled, Is.True);
        });
    }

    [Test]
    public async Task TurnOffBiometricLogin_WhenTheCredentialsSurvive_DoesNotClaimTheyAreGone()
    {
        GiveTheDeviceBiometrics();
        _viewModel.IsBiometricLoginEnabled = true;
        _mockAlertService.Setup(a => a.ShowConfirmAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);
        // The service swallowed a keystore failure, so the credentials are still readable.
        _mockAuthService.Setup(a => a.HasBiometricCredentialsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await _viewModel.TurnOffBiometricLoginCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.IsBiometricLoginEnabled, Is.True);
            Assert.That(_viewModel.ErrorMessage, Does.Contain("Could not remove"));
        });
    }

    [Test]
    public async Task LoadCommand_OnAFaceIdDevice_NamesFaceIdInTheCopy()
    {
        // "Fingerprint sign-in is off" on an iPhone reads as a different feature that is broken.
        _mockAuthService.Setup(a => a.GetBiometricAvailabilityAsync())
            .ReturnsAsync(new BiometricAvailability(true, BiometricMethod.FaceId, "Face ID", "Face ID"));
        _mockAuthService.Setup(a => a.HasBiometricCredentialsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await _viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.BiometricLoginStatusText, Does.Contain("Face ID"));
            Assert.That(_viewModel.BiometricLoginStatusText, Does.Not.Contain("fingerprint"));
            Assert.That(_viewModel.TurnOffBiometricLoginText, Is.EqualTo("Turn Off Face ID Sign-In"));
        });
    }

    [Test]
    public async Task LoadCommand_OnAndroid_KeepsTheFingerprintWording()
    {
        // The Android copy is what shipped, word for word. This is the regression guard on it.
        GiveTheDeviceBiometrics();
        _mockAuthService.Setup(a => a.HasBiometricCredentialsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await _viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.BiometricLoginStatusText, Does.Contain("sign in with your fingerprint or face"));
            Assert.That(_viewModel.TurnOffBiometricLoginText, Is.EqualTo("Turn Off Fingerprint Sign-In"));
        });
    }

    [Test]
    public void BiometricLoginStatusText_WhenOff_PointsAtTheLoginScreen()
    {
        // Enabling needs the plaintext password, which this page never has, so the copy has to send
        // the user somewhere that does rather than offering a switch that cannot be switched on.
        _viewModel.IsBiometricLoginEnabled = false;

        Assert.That(_viewModel.BiometricLoginStatusText, Does.Contain("sign in with your password"));
    }

    [Test]
    public async Task TurnOffBiometricLogin_WhenConfirmed_ClearsTheSavedCredentials()
    {
        GiveTheDeviceBiometrics();
        _viewModel.IsBiometricLoginEnabled = true;
        _mockAlertService.Setup(a => a.ShowConfirmAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);

        await _viewModel.TurnOffBiometricLoginCommand.ExecuteAsync(null);

        _mockAuthService.Verify(a => a.DisableBiometricLoginAsync(), Times.Once);
        Assert.That(_viewModel.IsBiometricLoginEnabled, Is.False);
    }

    [Test]
    public async Task TurnOffBiometricLogin_WhenDeclined_LeavesTheCredentialsAlone()
    {
        _viewModel.IsBiometricLoginEnabled = true;
        _mockAlertService.Setup(a => a.ShowConfirmAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(false);

        await _viewModel.TurnOffBiometricLoginCommand.ExecuteAsync(null);

        _mockAuthService.Verify(a => a.DisableBiometricLoginAsync(), Times.Never);
        Assert.That(_viewModel.IsBiometricLoginEnabled, Is.True);
    }

    [Test]
    public async Task LoadCommand_OnNonAndroid_DoesNotInventSubscriptionPrice()
    {
        _viewModel.IsAndroidSubscriptionPlatform = false;

        await _viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.SubscriptionPrice, Is.Empty);
            Assert.That(_viewModel.SubscriptionPriceDisplay, Is.Empty);
            Assert.That(_viewModel.ShowSubscriptionPriceDisplay, Is.False);
        });
        _mockBillingService.Verify(b => b.GetSubscriptionOfferAsync(), Times.Never);
    }

    [Test]
    public void ShowCancelSubscription_TrueWhenHasActiveSubscription()
    {
        _viewModel.HasActiveSubscription = true;
        _viewModel.SubscriptionEndDate = null;
        Assert.That(_viewModel.ShowCancelSubscription, Is.True);
    }

    [Test]
    public void ShowCancelSubscription_TrueWhenActiveSubscriptionHasBillingPeriodEnd()
    {
        _viewModel.HasActiveSubscription = true;
        _viewModel.SubscriptionStatus = "ACTIVE";
        _viewModel.SubscriptionEndDate = DateTime.UtcNow.AddDays(30);

        Assert.That(_viewModel.ShowCancelSubscription, Is.True);
    }

    [Test]
    public void ShowCancelSubscription_FalseWhenNoSubscription()
    {
        _viewModel.HasActiveSubscription = false;
        Assert.That(_viewModel.ShowCancelSubscription, Is.False);
    }

    [Test]
    public void CanDeleteAccount_TrueWhenNoActiveSubscription()
    {
        _viewModel.HasActiveSubscription = false;
        Assert.That(_viewModel.CanDeleteAccount, Is.True);
    }

    [Test]
    public void CanDeleteAccount_FalseWhenHasActiveSubscription()
    {
        _viewModel.HasActiveSubscription = true;
        _viewModel.SubscriptionEndDate = null;
        Assert.That(_viewModel.CanDeleteAccount, Is.False);
    }

    [Test]
    public void CanDeleteAccount_FalseWhenActiveCreator()
    {
        _viewModel.IsActiveCreator = true;
        Assert.That(_viewModel.CanDeleteAccount, Is.False);
    }

    [Test]
    public async Task OnAppearingAsync_RefreshesAuthStateAndAppliesLatestSubscriptionValues()
    {
        var endDate = DateTime.UtcNow.AddDays(30);

        _mockAuthService.Setup(a => a.RefreshUserStatusAsync())
            .Callback(() =>
            {
                _mockAuthService.Setup(a => a.HasActiveSubscription).Returns(true);
                _mockAuthService.Setup(a => a.SubscriptionStatus).Returns("ACTIVE");
                _mockAuthService.Setup(a => a.SubscriptionEndDate).Returns(endDate);
                _mockAuthService.Setup(a => a.BillingSource).Returns("PayPal");
                _mockAuthService.Setup(a => a.IsCreator).Returns(true);
            })
            .Returns(Task.CompletedTask);

        await _viewModel.OnAppearingAsync();

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.HasActiveSubscription, Is.True);
            Assert.That(_viewModel.IsActiveCreator, Is.True);
            Assert.That(_viewModel.SubscriptionStatus, Is.EqualTo("ACTIVE"));
            Assert.That(_viewModel.SubscriptionEndDate, Is.EqualTo(endDate));
            Assert.That(_viewModel.SubscriptionBillingSource, Is.EqualTo("PayPal"));
        });

        _mockAuthService.Verify(a => a.RefreshUserStatusAsync(), Times.Once);
    }

    [Test]
    public async Task LoadCommand_CancelledSubscriptionWithRemainingAccess_HidesCancelButtonAndBlocksNewSubscription()
    {
        _mockMusicService.Setup(m => m.GetSubscriptionStatusAsync())
            .ReturnsAsync(new SubscriptionStatusDto
            {
                HasSubscription = true,
                Status = "CANCELLED",
                EndDate = DateTime.UtcNow.AddDays(5),
                BillingSource = "GooglePlay"
            });

        await _viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.HasActiveSubscription, Is.True);
            Assert.That(_viewModel.ShowCancelSubscription, Is.False);
            Assert.That(_viewModel.CanCreateSubscription, Is.False);
            Assert.That(_viewModel.CanDeleteAccount, Is.False);
            Assert.That(_viewModel.SubscriptionStatusText, Is.EqualTo("Renews Off"));
            Assert.That(_viewModel.SubscriptionStatusMessage, Does.Contain("has been canceled"));
            Assert.That(_viewModel.SubscriptionStatusMessage, Does.Contain("will not automatically renew"));
            Assert.That(_viewModel.SubscriptionEndDateText, Does.StartWith("Access Until:"));
        });
    }

    [Test]
    public void SubscriptionStatusChange_RaisesShowCancelSubscriptionNotification()
    {
        var changedProperties = new List<string>();
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.PropertyName))
            {
                changedProperties.Add(args.PropertyName);
            }
        };

        _viewModel.HasActiveSubscription = true;
        _viewModel.SubscriptionEndDate = DateTime.UtcNow.AddDays(5);

        changedProperties.Clear();
        _viewModel.SubscriptionStatus = "CANCELLED";

        Assert.That(changedProperties, Does.Contain(nameof(AccountSettingsViewModel.ShowCancelSubscription)));
        Assert.That(_viewModel.ShowCancelSubscription, Is.False);
    }

    [Test]
    public async Task LoadCommand_ActiveSubscriptionWithPeriodEnd_ShowsActiveRecurringState()
    {
        _mockMusicService.Setup(m => m.GetSubscriptionStatusAsync())
            .ReturnsAsync(new SubscriptionStatusDto
            {
                HasSubscription = true,
                Status = "ACTIVE",
                EndDate = DateTime.UtcNow.AddMinutes(5),
                NextBillingDate = DateTime.UtcNow.AddMinutes(5),
                BillingSource = BillingProviders.Apple
            });

        await _viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.HasActiveSubscription, Is.True);
            Assert.That(_viewModel.ShowCancelSubscription, Is.True);
            Assert.That(_viewModel.SubscriptionStatusText, Is.EqualTo("Active"));
            Assert.That(_viewModel.SubscriptionStatusMessage, Does.Contain("will automatically renew unless canceled"));
            Assert.That(_viewModel.SubscriptionStatusMessage, Does.Contain("current billing period ends on"));
            Assert.That(_viewModel.SubscriptionEndDateText, Does.StartWith("Current Billing Period Ends:"));
            Assert.That(_viewModel.CanCreateSubscription, Is.False);
            Assert.That(_viewModel.CanDeleteAccount, Is.False);
        });
    }

    [Test]
    public async Task LoadCommand_ActiveTrial_ShowsTrialMessageAndEndDate()
    {
        var trialEnd = DateTime.UtcNow.AddDays(3);
        _mockMusicService.Setup(m => m.GetSubscriptionStatusAsync())
            .ReturnsAsync(new SubscriptionStatusDto
            {
                HasSubscription = true,
                IsOnTrial = true,
                Status = "ACTIVE",
                EndDate = trialEnd,
                TrialEndDate = trialEnd,
                BillingSource = BillingProviders.GooglePlay
            });

        await _viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.SubscriptionStatusText, Is.EqualTo("Free Trial Active"));
            Assert.That(_viewModel.SubscriptionStatusMessage, Does.Contain("free trial is active until"));
            Assert.That(_viewModel.SubscriptionStatusMessage, Does.Contain("full subscription benefits"));
            Assert.That(_viewModel.SubscriptionEndDateText, Does.StartWith("Trial Active Until:"));
        });
    }

    [Test]
    public async Task LoadCommand_NoSubscriptionWithGoogleTrialOffer_ShowsOfferCardWithGooglePrice()
    {
        _viewModel.IsAndroidSubscriptionPlatform = true;
        _mockMusicService.Setup(m => m.GetSubscriptionStatusAsync())
            .ReturnsAsync(new SubscriptionStatusDto { HasSubscription = false });
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
            Assert.That(_viewModel.ShowSubscriptionOfferCard, Is.True);
            Assert.That(_viewModel.ShowPlainSubscribeButton, Is.False);
            Assert.That(_viewModel.SubscribeButtonText, Is.EqualTo("Start My Free Trial"));
            Assert.That(_viewModel.SubscriptionOfferTitleText, Is.EqualTo("Support independent music."));
            Assert.That(_viewModel.SubscriptionOfferBodyText, Does.Contain("directly funds independent creators"));
            Assert.That(_viewModel.SubscriptionOfferDisclosureText, Does.StartWith("Full subscription benefits are included during the trial."));
            Assert.That(_viewModel.SubscriptionOfferDisclosureText, Does.Contain("$4.99/month"));
        });
    }

    [Test]
    public async Task LoadCommand_PublishesAndroidStorePrice()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUICulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");

            _viewModel.IsAndroidSubscriptionPlatform = true;
            _mockMusicService.Setup(m => m.GetSubscriptionStatusAsync())
                .ReturnsAsync(new SubscriptionStatusDto { HasSubscription = false });
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
                if (args.PropertyName == nameof(AccountSettingsViewModel.SubscriptionPriceDisplay))
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
    public async Task LoadCommand_ActiveAndroidSubscription_StillUsesGoogleRenewalPriceForDisplay()
    {
        _viewModel.IsAndroidSubscriptionPlatform = true;
        _mockMusicService.Setup(m => m.GetSubscriptionStatusAsync())
            .ReturnsAsync(new SubscriptionStatusDto
            {
                HasSubscription = true,
                Status = "ACTIVE",
                EndDate = DateTime.UtcNow.AddMonths(1),
                BillingSource = BillingProviders.GooglePlay
            });
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
            Assert.That(_viewModel.ShowSubscriptionOfferCard, Is.False);
            Assert.That(_viewModel.ShowPlainSubscribeButton, Is.False);
        });
        _mockBillingService.Verify(b => b.GetSubscriptionOfferAsync(), Times.Once);
    }

    [Test]
    public async Task LoadCommand_KeepsStorePrice_WhenSubsequentOfferLookupFails()
    {
        _viewModel.IsAndroidSubscriptionPlatform = true;
        _mockMusicService.Setup(m => m.GetSubscriptionStatusAsync())
            .ReturnsAsync(new SubscriptionStatusDto { HasSubscription = false });
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
            Assert.That(_viewModel.SubscriptionOfferDisclosureText, Does.Contain("\u20B1205.00/month"));
        });
    }

    [Test]
    public async Task LoadCommand_KeepsFirstStorePrice_WhenLaterLookupReturnsDifferentPrice()
    {
        _viewModel.IsAndroidSubscriptionPlatform = true;
        _mockMusicService.Setup(m => m.GetSubscriptionStatusAsync())
            .ReturnsAsync(new SubscriptionStatusDto { HasSubscription = false });
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
    public async Task LoadCommand_NoSubscriptionWithBillingLookupFailure_ShowsFallbackOfferCard()
    {
        _viewModel.IsAndroidSubscriptionPlatform = true;
        _mockMusicService.Setup(m => m.GetSubscriptionStatusAsync())
            .ReturnsAsync(new SubscriptionStatusDto { HasSubscription = false });
        _mockBillingService.Setup(b => b.GetSubscriptionOfferAsync())
            .ReturnsAsync(new SubscriptionOfferInfo { LookupSucceeded = false });

        await _viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.ShowSubscriptionOfferCard, Is.True);
            Assert.That(_viewModel.SubscriptionOfferBodyText, Does.Contain("Unlock the full catalog."));
            Assert.That(_viewModel.SubscriptionOfferDisclosureText, Does.Contain("monthly price shown in Google Play"));
            Assert.That(_viewModel.SubscriptionOfferDisclosureText, Does.Not.Contain("$"));
            Assert.That(_viewModel.SubscriptionOfferPriceText, Is.Empty);
            Assert.That(_viewModel.ShowSubscriptionOfferPriceText, Is.False);
            Assert.That(_viewModel.SubscriptionPriceDisplay, Is.Empty);
        });
    }

    [Test]
    public async Task LoadCommand_ExpiredSubscription_WhenGoogleReportsTrial_ShowsExpiredStateAndOfferCard()
    {
        _viewModel.IsAndroidSubscriptionPlatform = true;
        var endDate = DateTime.UtcNow.AddDays(-1);
        _mockMusicService.Setup(m => m.GetSubscriptionStatusAsync())
            .ReturnsAsync(new SubscriptionStatusDto
            {
                HasSubscription = false,
                Status = "EXPIRED",
                EndDate = endDate,
                TrialEndDate = endDate,
                BillingSource = BillingProviders.GooglePlay
            });
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
            Assert.That(_viewModel.HasActiveSubscription, Is.False);
            Assert.That(_viewModel.ShowCancelSubscription, Is.False);
            Assert.That(_viewModel.CanCreateSubscription, Is.True);
            Assert.That(_viewModel.SubscriptionStatusText, Is.EqualTo("Expired"));
            Assert.That(_viewModel.ShowSubscriptionOfferCard, Is.True);
            Assert.That(_viewModel.ShowPlainSubscribeButton, Is.False);
            Assert.That(_viewModel.SubscribeButtonText, Is.EqualTo("Start My Free Trial"));
            Assert.That(_viewModel.SubscriptionOfferTitleText, Is.EqualTo("Support independent music."));
            Assert.That(_viewModel.SubscriptionOfferBodyText, Does.Contain("Unlock the full catalog."));
            Assert.That(_viewModel.SubscriptionOfferDisclosureText, Does.Contain("$2.99/month"));
        });
        _mockBillingService.Verify(b => b.GetSubscriptionOfferAsync(), Times.Once);
    }

    [Test]
    public async Task LoadCommand_ExpiredSubscription_WhenGoogleHasNoTrial_ShowsPlainSubscribeWithGooglePrice()
    {
        _viewModel.IsAndroidSubscriptionPlatform = true;
        var endDate = DateTime.UtcNow.AddDays(-1);
        _mockMusicService.Setup(m => m.GetSubscriptionStatusAsync())
            .ReturnsAsync(new SubscriptionStatusDto
            {
                HasSubscription = false,
                Status = "EXPIRED",
                EndDate = endDate,
                TrialEndDate = endDate,
                BillingSource = BillingProviders.GooglePlay
            });
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
            Assert.That(_viewModel.HasActiveSubscription, Is.False);
            Assert.That(_viewModel.SubscriptionStatusText, Is.EqualTo("Expired"));
            Assert.That(_viewModel.ShowSubscriptionOfferCard, Is.False);
            Assert.That(_viewModel.ShowPlainSubscribeButton, Is.True);
            Assert.That(_viewModel.SubscriptionPriceDisplay, Is.EqualTo("$2.99"));
            Assert.That(_viewModel.SubscriptionOfferBodyText, Does.Not.Contain("free trial"));
        });
        _mockBillingService.Verify(b => b.GetSubscriptionOfferAsync(), Times.Once);
    }

    [Test]
    public async Task LoadCommand_ExpiredSubscription_WhenGoogleLookupFails_DoesNotShowFallbackTrialCard()
    {
        _viewModel.IsAndroidSubscriptionPlatform = true;
        var endDate = DateTime.UtcNow.AddDays(-1);
        _mockMusicService.Setup(m => m.GetSubscriptionStatusAsync())
            .ReturnsAsync(new SubscriptionStatusDto
            {
                HasSubscription = false,
                Status = "EXPIRED",
                EndDate = endDate,
                TrialEndDate = endDate,
                BillingSource = BillingProviders.GooglePlay
            });
        _mockBillingService.Setup(b => b.GetSubscriptionOfferAsync())
            .ReturnsAsync(new SubscriptionOfferInfo { LookupSucceeded = false });

        await _viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.ShowSubscriptionOfferCard, Is.False);
            Assert.That(_viewModel.ShowPlainSubscribeButton, Is.True);
            Assert.That(_viewModel.SubscriptionOfferTitleText, Does.Not.Contain("free trial"));
        });
        _mockBillingService.Verify(b => b.GetSubscriptionOfferAsync(), Times.Once);
    }

    [Test]
    public void CanConfirmDelete_TrueWhenTextIsDelete()
    {
        _viewModel.ConfirmationText = "DELETE";
        Assert.That(_viewModel.CanConfirmDelete, Is.True);
    }

    [Test]
    public void CanConfirmDelete_TrueWhenTextIsDeleteCaseInsensitive()
    {
        _viewModel.ConfirmationText = "delete";
        Assert.That(_viewModel.CanConfirmDelete, Is.True);
    }

    [Test]
    public void CanConfirmDelete_FalseWhenTextIsWrong()
    {
        _viewModel.ConfirmationText = "remove";
        Assert.That(_viewModel.CanConfirmDelete, Is.False);
    }

    [Test]
    public void CanConfirmDelete_FalseWhenTextIsEmpty()
    {
        _viewModel.ConfirmationText = string.Empty;
        Assert.That(_viewModel.CanConfirmDelete, Is.False);
    }

    // --- Cancel Subscription Tests ---

    [Test]
    public async Task CancelSubscriptionCommand_UserDeclines_DoesNotCancel()
    {
        _mockAlertService.Setup(a => a.ShowConfirmAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(false);

        await _viewModel.CancelSubscriptionCommand.ExecuteAsync(null);

        _mockMusicService.Verify(m => m.CancelSubscriptionAsync(), Times.Never);
    }

    [Test]
    public async Task CancelSubscriptionCommand_Success_RefreshesAuthAndShowsAlert()
    {
        _viewModel.SubscriptionBillingSource = "PayPal";
        _mockAlertService.Setup(a => a.ShowConfirmAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);
        _mockMusicService.Setup(m => m.CancelSubscriptionAsync())
            .ReturnsAsync((true, (DateTime?)DateTime.UtcNow.AddDays(30)));
        _mockMusicService.Setup(m => m.GetSubscriptionStatusAsync())
            .ReturnsAsync(new SubscriptionStatusDto
            {
                HasSubscription = true,
                Status = "CANCELLED",
                EndDate = DateTime.UtcNow.AddDays(30),
                BillingSource = "GooglePlay"
            });

        await _viewModel.CancelSubscriptionCommand.ExecuteAsync(null);

        _mockAuthService.Verify(a => a.RefreshUserStatusAsync(), Times.Once);
        _mockAlertService.Verify(a => a.DisplayAlertAsync("Subscription Cancelled", It.IsAny<string>(), "OK"), Times.Once);
    }

    [Test]
    public async Task CancelSubscriptionCommand_Failure_ShowsError()
    {
        _viewModel.SubscriptionBillingSource = "PayPal";
        _mockAlertService.Setup(a => a.ShowConfirmAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);
        _mockMusicService.Setup(m => m.CancelSubscriptionAsync())
            .ReturnsAsync((false, (DateTime?)null));

        await _viewModel.CancelSubscriptionCommand.ExecuteAsync(null);

        _mockAlertService.Verify(a => a.DisplayAlertAsync("Error", It.Is<string>(s => s.Contains("Failed to cancel")), "OK"), Times.Once);
        _mockAuthService.Verify(a => a.RefreshUserStatusAsync(), Times.Never);
    }

    [Test]
    public async Task CancelSubscriptionCommand_Apple_OpensAppleSubscriptionsInsteadOfCallingApi()
    {
        _viewModel.SubscriptionBillingSource = BillingProviders.Apple;
        _mockAlertService.Setup(a => a.ShowConfirmAsync(
                "Manage Subscription",
                It.Is<string>(message => message.Contains("Sandbox Apple subscriptions are managed on the test device")),
                "Open Apple Sandbox Help",
                "Not Now"))
            .ReturnsAsync(true);

        await _viewModel.CancelSubscriptionCommand.ExecuteAsync(null);

        _mockBrowserService.Verify(b => b.OpenExternalAsync("https://developer.apple.com/documentation/storekit/testing-disabling-auto-renew"), Times.Once);
        _mockMusicService.Verify(m => m.CancelSubscriptionAsync(), Times.Never);
    }

    // --- Delete Account Prompt Tests ---

    [Test]
    public async Task ShowDeleteAccountPromptCommand_WithActiveSubscription_ShowsBlockingAlert()
    {
        _viewModel.HasActiveSubscription = true;

        await _viewModel.ShowDeleteAccountPromptCommand.ExecuteAsync(null);

        _mockAlertService.Verify(a => a.DisplayAlertAsync(
            "Active Subscription",
            It.Is<string>(s => s.Contains("cancel your active subscription")),
            "OK"), Times.Once);
        Assert.That(_viewModel.ShowDeleteConfirmation, Is.False);
    }

    [Test]
    public async Task ShowDeleteAccountPromptCommand_WithActiveCreator_ShowsBlockingAlert()
    {
        _viewModel.IsActiveCreator = true;

        await _viewModel.ShowDeleteAccountPromptCommand.ExecuteAsync(null);

        _mockAlertService.Verify(a => a.DisplayAlertAsync(
            "Creator Account",
            It.Is<string>(s => s.Contains("stop being a creator") && s.Contains("website")),
            "OK"), Times.Once);
        Assert.That(_viewModel.ShowDeleteConfirmation, Is.False);
    }

    [Test]
    public async Task SubscribeCommand_Success_PurchasesAndRefreshesStatus()
    {
        _mockBillingService.Setup(b => b.PurchaseSubscriptionAsync())
            .ReturnsAsync(BillingPurchaseResult.Succeeded("test-token", "order-123"));
        _mockMusicService.Setup(m => m.VerifySubscriptionPurchaseAsync(It.Is<BillingPurchaseVerificationRequest>(r =>
                r.Provider == BillingProviders.GooglePlay &&
                r.PurchaseToken == "test-token" &&
                r.OrderId == "order-123")))
            .ReturnsAsync((true, string.Empty));
        _mockMusicService.Setup(m => m.GetSubscriptionStatusAsync())
            .ReturnsAsync(new SubscriptionStatusDto { HasSubscription = true, BillingSource = "GooglePlay" });

        await _viewModel.SubscribeCommand.ExecuteAsync(null);

        _mockAuthService.Verify(a => a.RefreshUserStatusAsync(), Times.Once);
        _mockAlertService.Verify(a => a.DisplayAlertAsync("Success", It.IsAny<string>(), "OK"), Times.Once);
    }

    [Test]
    public async Task ShowDeleteAccountPromptCommand_UserDeclinesConfirmation_DoesNotShowOverlay()
    {
        _viewModel.HasActiveSubscription = false;
        _mockAlertService.Setup(a => a.ShowConfirmAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(false);

        await _viewModel.ShowDeleteAccountPromptCommand.ExecuteAsync(null);

        Assert.That(_viewModel.ShowDeleteConfirmation, Is.False);
    }

    [Test]
    public async Task ShowDeleteAccountPromptCommand_UserConfirms_ShowsOverlay()
    {
        _viewModel.HasActiveSubscription = false;
        _mockAlertService.Setup(a => a.ShowConfirmAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);

        await _viewModel.ShowDeleteAccountPromptCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.ShowDeleteConfirmation, Is.True);
            Assert.That(_viewModel.ConfirmationText, Is.EqualTo(string.Empty));
        });
    }

    // --- Confirm Delete Tests ---

    [Test]
    public async Task ConfirmDeleteCommand_WrongText_ShowsError()
    {
        _viewModel.ConfirmationText = "wrong";

        await _viewModel.ConfirmDeleteCommand.ExecuteAsync(null);

        Assert.That(_viewModel.ErrorMessage, Does.Contain("type DELETE"));
        _mockAuthService.Verify(a => a.DeleteAccountAsync(), Times.Never);
    }

    [Test]
    public async Task ConfirmDeleteCommand_Success_DeletesAndNavigatesHome()
    {
        _viewModel.ConfirmationText = "DELETE";
        _mockAuthService.Setup(a => a.DeleteAccountAsync())
            .ReturnsAsync((true, string.Empty));

        await _viewModel.ConfirmDeleteCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.ShowDeleteConfirmation, Is.False);
            Assert.That(_viewModel.IsDeleting, Is.False);
        });
        _mockAlertService.Verify(a => a.DisplayAlertAsync(
            "Account Deleted",
            It.Is<string>(s => s.Contains("permanently deleted")),
            "OK"), Times.Once);
        _mockNavigationService.Verify(n => n.GoToAsync("//Home"), Times.Once);
    }

    [Test]
    public async Task ConfirmDeleteCommand_SuccessCaseInsensitive_DeletesAccount()
    {
        _viewModel.ConfirmationText = "delete";
        _mockAuthService.Setup(a => a.DeleteAccountAsync())
            .ReturnsAsync((true, string.Empty));

        await _viewModel.ConfirmDeleteCommand.ExecuteAsync(null);

        _mockAuthService.Verify(a => a.DeleteAccountAsync(), Times.Once);
        _mockNavigationService.Verify(n => n.GoToAsync("//Home"), Times.Once);
    }

    [Test]
    public async Task ConfirmDeleteCommand_ServerError_ShowsErrorMessage()
    {
        _viewModel.ShowDeleteConfirmation = true;
        _viewModel.ConfirmationText = "DELETE";
        _mockAuthService.Setup(a => a.DeleteAccountAsync())
            .ReturnsAsync((false, "You must cancel your active subscription before deleting your account."));

        await _viewModel.ConfirmDeleteCommand.ExecuteAsync(null);

        Assert.That(_viewModel.ErrorMessage, Does.Contain("cancel your active subscription"));
        Assert.That(_viewModel.ShowDeleteConfirmation, Is.True);
    }

    [Test]
    public async Task ConfirmDeleteCommand_Exception_ShowsErrorMessage()
    {
        _viewModel.ConfirmationText = "DELETE";
        _mockAuthService.Setup(a => a.DeleteAccountAsync())
            .ThrowsAsync(new Exception("Network error"));

        await _viewModel.ConfirmDeleteCommand.ExecuteAsync(null);

        Assert.That(_viewModel.ErrorMessage, Does.Contain("Network error"));
        Assert.That(_viewModel.IsDeleting, Is.False);
    }

    // --- Cancel Delete Tests ---

    [Test]
    public void CancelDeleteCommand_HidesOverlayAndClearsText()
    {
        _viewModel.ShowDeleteConfirmation = true;
        _viewModel.ConfirmationText = "DEL";
        _viewModel.ErrorMessage = "some error";

        _viewModel.CancelDeleteCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.ShowDeleteConfirmation, Is.False);
            Assert.That(_viewModel.ConfirmationText, Is.EqualTo(string.Empty));
            Assert.That(_viewModel.ErrorMessage, Is.EqualTo(string.Empty));
        });
    }

    // --- Auth State Changed Tests ---

    [Test]
    public async Task AuthStateChanged_RefreshesProperties()
    {
        _mockAuthService.Setup(a => a.Email).Returns("new@example.com");
        _mockAuthService.Setup(a => a.HasActiveSubscription).Returns(true);
        _mockAuthService.Setup(a => a.IsCreator).Returns(true);
        _mockMusicService.Setup(m => m.GetSubscriptionStatusAsync())
            .ReturnsAsync(new SubscriptionStatusDto
            {
                HasSubscription = true,
                Status = "ACTIVE",
                BillingSource = "GooglePlay"
            });

        _mockAuthService.Raise(a => a.AuthStateChanged += null);

        await Task.Delay(50);

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.UserEmail, Is.EqualTo("new@example.com"));
            Assert.That(_viewModel.HasActiveSubscription, Is.True);
            Assert.That(_viewModel.IsActiveCreator, Is.True);
        });
    }

    [Test]
    public async Task AuthSubscription_IsDetachedByCleanupAndReattachedByActivate()
    {
        _viewModel.Cleanup();

        _mockAuthService.Raise(service => service.AuthStateChanged += null);
        await Task.Delay(25);
        _mockMusicService.Verify(service => service.GetSubscriptionStatusAsync(), Times.Never);

        _viewModel.Activate();
        _mockAuthService.Raise(service => service.AuthStateChanged += null);
        await Task.Delay(25);

        _mockMusicService.Verify(service => service.GetSubscriptionStatusAsync(), Times.Once);
    }

    [Test]
    public async Task CancelSubscription_PromptsWithCustomPlaylistWarning()
    {
        _mockAuthService.Setup(a => a.HasActiveSubscription).Returns(true);
        _viewModel = CreateViewModel();
        _mockAlertService.Setup(a => a.ShowConfirmAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(false);

        await _viewModel.CancelSubscriptionCommand.ExecuteAsync(null);

        _mockAlertService.Verify(a => a.ShowConfirmAsync(
            "Cancel Subscription",
            It.Is<string>(s => s.Contains("custom playlists") && s.Contains("end of your subscription term")),
            It.IsAny<string>(),
            It.IsAny<string>()), Times.Once);
    }

    [Test]
    public async Task ShowDeleteAccountPrompt_IncludesCustomPlaylistsImmediateWarning()
    {
        _mockAuthService.Setup(a => a.HasActiveSubscription).Returns(false);
        _viewModel = CreateViewModel();
        _mockAlertService.Setup(a => a.ShowConfirmAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(false);

        await _viewModel.ShowDeleteAccountPromptCommand.ExecuteAsync(null);

        _mockAlertService.Verify(a => a.ShowConfirmAsync(
            "Delete Account",
            It.Is<string>(s => s.Contains("custom playlists will be deleted immediately")),
            It.IsAny<string>(),
            It.IsAny<string>()), Times.Once);
    }
}
