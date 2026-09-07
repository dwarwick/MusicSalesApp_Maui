using Moq;
using MusicSalesApp.Common.Helpers;
using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Tests.ViewModels;

[TestFixture]
public class LoginViewModelTests
{
    private Mock<IAuthService> _mockAuthService;
    private Mock<IAlertService> _mockAlertService;
    private Mock<INavigationService> _mockNavigationService;
    private LoginViewModel _viewModel;

    [SetUp]
    public void Setup()
    {
        _mockAuthService = new Mock<IAuthService>();
        _mockAuthService
            .Setup(a => a.HasBiometricCredentialsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        GiveTheDeviceBiometrics();
        _mockAlertService = new Mock<IAlertService>();
        _mockNavigationService = new Mock<INavigationService>();
        _viewModel = new LoginViewModel(_mockAuthService.Object, _mockAlertService.Object, _mockNavigationService.Object);
    }

    /// <summary>
    /// An Android-shaped answer: a prompt is available and it is called "your fingerprint or face".
    /// </summary>
    private void GiveTheDeviceBiometrics()
        => _mockAuthService.Setup(a => a.GetBiometricAvailabilityAsync())
            .ReturnsAsync(new BiometricAvailability(true, BiometricMethod.Fingerprint, "your fingerprint or face", "Fingerprint"));

    /// <summary>Nothing enrolled, or no hardware. The platform is irrelevant - the device's answer is not.</summary>
    private void GiveTheDeviceNoBiometrics()
        => _mockAuthService.Setup(a => a.GetBiometricAvailabilityAsync())
            .ReturnsAsync(BiometricAvailability.Unavailable);

    [Test]
    public async Task LoginAsync_EmptyEmail_SetsErrorMessage()
    {
        _viewModel.Email = "";
        _viewModel.Password = "password";

        await _viewModel.LoginCommand.ExecuteAsync(null);

        Assert.That(_viewModel.ErrorMessage, Is.Not.Null);
        Assert.That(_viewModel.ErrorMessage, Does.Contain("email"));
    }

    [Test]
    public async Task LoginAsync_EmptyPassword_SetsErrorMessage()
    {
        _viewModel.Email = "test@test.com";
        _viewModel.Password = "";

        await _viewModel.LoginCommand.ExecuteAsync(null);

        Assert.That(_viewModel.ErrorMessage, Is.Not.Null);
        Assert.That(_viewModel.ErrorMessage, Does.Contain("password"));
    }

    [Test]
    public async Task LoginAsync_ServiceReturnsError_SetsErrorMessage()
    {
        _viewModel.Email = "test@test.com";
        _viewModel.Password = "password";
        _mockAuthService.Setup(a => a.LoginAsync("test@test.com", "password"))
            .ReturnsAsync((false, "Invalid credentials."));

        await _viewModel.LoginCommand.ExecuteAsync(null);

        Assert.That(_viewModel.ErrorMessage, Is.EqualTo("Invalid credentials."));
        Assert.That(_viewModel.IsBusy, Is.False);
    }

    [Test]
    public async Task LoginAsync_Exception_SetsConnectionError()
    {
        _viewModel.Email = "test@test.com";
        _viewModel.Password = "password";
        _mockAuthService.Setup(a => a.LoginAsync("test@test.com", "password"))
            .ThrowsAsync(new Exception("Network failure"));

        await _viewModel.LoginCommand.ExecuteAsync(null);

        Assert.That(_viewModel.ErrorMessage, Does.Contain("Connection error"));
        Assert.That(_viewModel.IsBusy, Is.False);
    }

    [Test]
    public async Task BiometricLoginAsync_ServiceReturnsError_SetsErrorMessage()
    {
        _mockAuthService.Setup(a => a.BiometricLoginAsync())
            .ReturnsAsync((false, "No saved credentials."));

        await _viewModel.BiometricLoginCommand.ExecuteAsync(null);

        Assert.That(_viewModel.ErrorMessage, Is.EqualTo("No saved credentials."));
    }

    [Test]
    public async Task InitializeAsync_WhenCredentialsExist_ShowsBiometricLogin()
    {
        _mockAuthService
            .Setup(a => a.HasBiometricCredentialsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var vm = new LoginViewModel(_mockAuthService.Object, _mockAlertService.Object, _mockNavigationService.Object);

        await vm.InitializeAsync();

        Assert.That(vm.BiometricVisible, Is.True);
    }

    [Test]
    public async Task InitializeAsync_WhenTheDeviceHasNoBiometrics_HidesBiometricLogin()
    {
        // Saved credentials are not enough on their own: with nothing enrolled the button would be
        // chrome over a prompt that cannot appear, and every tap would fail.
        GiveTheDeviceNoBiometrics();
        _mockAuthService
            .Setup(a => a.HasBiometricCredentialsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var vm = new LoginViewModel(_mockAuthService.Object, _mockAlertService.Object, _mockNavigationService.Object);

        await vm.InitializeAsync();

        Assert.That(vm.BiometricVisible, Is.False);
    }

    [Test]
    public async Task InitializeAsync_OnAFaceIdDevice_ShowsTheFaceIdIcon()
    {
        // The reason the icon is bound rather than an OnPlatform swap: a Touch ID iPhone and a Face
        // ID iPhone are the same platform and want different glyphs.
        _mockAuthService.Setup(a => a.GetBiometricAvailabilityAsync())
            .ReturnsAsync(new BiometricAvailability(true, BiometricMethod.FaceId, "Face ID", "Face ID"));
        var vm = new LoginViewModel(_mockAuthService.Object, _mockAlertService.Object, _mockNavigationService.Object);

        await vm.InitializeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(vm.BiometricIconSource, Is.EqualTo(BiometricIcons.FaceId));
            Assert.That(vm.BiometricMethodName, Is.EqualTo("Face ID"));
        });
    }

    [Test]
    public async Task InitializeAsync_OnATouchIdDevice_KeepsTheFingerprintIcon()
    {
        _mockAuthService.Setup(a => a.GetBiometricAvailabilityAsync())
            .ReturnsAsync(new BiometricAvailability(true, BiometricMethod.TouchId, "Touch ID", "Touch ID"));
        var vm = new LoginViewModel(_mockAuthService.Object, _mockAlertService.Object, _mockNavigationService.Object);

        await vm.InitializeAsync();

        Assert.That(vm.BiometricIconSource, Is.EqualTo(BiometricIcons.Fingerprint));
    }

    [Test]
    public async Task InitializeAsync_WhenCredentialsAreMissing_HidesBiometricLogin()
    {
        _mockAuthService
            .Setup(a => a.HasBiometricCredentialsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var vm = new LoginViewModel(_mockAuthService.Object, _mockAlertService.Object, _mockNavigationService.Object);

        await vm.InitializeAsync();

        Assert.That(vm.BiometricVisible, Is.False);
    }

    [Test]
    public async Task LoginAsync_EmailNotConfirmed_NavigatesToVerifyEmail()
    {
        _viewModel.Email = "test@test.com";
        _viewModel.Password = "password";
        _mockAuthService.Setup(a => a.LoginAsync("test@test.com", "password"))
            .ReturnsAsync((true, string.Empty));
        _mockAuthService.Setup(a => a.EmailConfirmed).Returns(false);
        _mockAuthService.Setup(a => a.UserId).Returns(42);
        _mockAuthService.Setup(a => a.Email).Returns("test@test.com");

        await _viewModel.LoginCommand.ExecuteAsync(null);

        _mockNavigationService.Verify(n => n.GoToAsync("verify-email", It.Is<IDictionary<string, object>>(d =>
            (int)d["UserId"] == 42 &&
            (string)d["Email"] == "test@test.com" &&
            (string)d["Password"] == "password"
        )), Times.Once);
        _mockNavigationService.Verify(n => n.GoToAsync(NavigationRoutes.MusicLibraryRoot), Times.Never);
    }

    // --- Offering biometric sign-in after a password login -------------------------------------
    //
    // This whole chain had no coverage: nothing asserted that a successful password login even
    // offers to save the credentials, which is the ONLY way the feature is ever switched on.

    /// <summary>A password login that succeeds, with the device able to prompt.</summary>
    private void ArrangeSuccessfulPasswordLogin(bool acceptsTheOffer)
    {
        _viewModel.Email = "test@test.com";
        _viewModel.Password = "password";
        _mockAuthService.Setup(a => a.LoginAsync("test@test.com", "password"))
            .ReturnsAsync((true, string.Empty));
        _mockAuthService.Setup(a => a.EmailConfirmed).Returns(true);
        _mockAlertService
            .Setup(a => a.ShowConfirmAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(acceptsTheOffer);
    }

    [Test]
    public async Task LoginAsync_OffersToSaveCredentialsAndShowsTheButtonOnceAccepted()
    {
        ArrangeSuccessfulPasswordLogin(acceptsTheOffer: true);

        await _viewModel.LoginCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            _mockAuthService.Verify(
                a => a.EnableBiometricLoginAsync("test@test.com", "password"), Times.Once);
            Assert.That(
                _viewModel.BiometricVisible, Is.True,
                "the button has to appear straight away, not only on the next visit");
        });
    }

    [Test]
    public async Task LoginAsync_DoesNotSaveCredentialsWhenTheOfferIsDeclined()
    {
        ArrangeSuccessfulPasswordLogin(acceptsTheOffer: false);

        await _viewModel.LoginCommand.ExecuteAsync(null);

        _mockAuthService.Verify(
            a => a.EnableBiometricLoginAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        Assert.That(_viewModel.BiometricVisible, Is.False);
    }

    [Test]
    public async Task LoginAsync_TrimsTheEmailItSavesSoItMatchesTheOneItSignedInWith()
    {
        // The login call trims; the save has to trim the same way, or the saved pair is an address
        // the server never accepts and the fingerprint button fails on every tap.
        ArrangeSuccessfulPasswordLogin(acceptsTheOffer: true);
        _viewModel.Email = "  test@test.com  ";
        _mockAuthService.Setup(a => a.LoginAsync("test@test.com", "password"))
            .ReturnsAsync((true, string.Empty));

        await _viewModel.LoginCommand.ExecuteAsync(null);

        _mockAuthService.Verify(
            a => a.EnableBiometricLoginAsync("test@test.com", "password"), Times.Once);
    }

    [Test]
    public async Task LoginAsync_DoesNotOfferWhereTheDeviceCannotPrompt()
    {
        // Accepting on a device with nothing enrolled would save the credentials and show a button
        // that fails on every tap.
        GiveTheDeviceNoBiometrics();
        ArrangeSuccessfulPasswordLogin(acceptsTheOffer: true);

        await _viewModel.LoginCommand.ExecuteAsync(null);

        _mockAlertService.Verify(
            a => a.ShowConfirmAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
        _mockAuthService.Verify(
            a => a.EnableBiometricLoginAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task LoginAsync_DoesNotAskAgainWhenCredentialsAreAlreadySaved()
    {
        ArrangeSuccessfulPasswordLogin(acceptsTheOffer: true);
        _mockAuthService
            .Setup(a => a.HasBiometricCredentialsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await _viewModel.LoginCommand.ExecuteAsync(null);

        _mockAlertService.Verify(
            a => a.ShowConfirmAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Test]
    public async Task GoogleLoginAsync_CannotOfferBiometricsBecauseThereIsNoPasswordToSave()
    {
        // Documents a real gap rather than approving of it: biometric sign-in replays a saved email
        // and PASSWORD, and a Google sign-in never handles one - so a Google-only account can never
        // switch the feature on, while the account page tells the user to "sign in with your
        // password". Closing that needs a different mechanism, not a call added here.
        _mockAuthService.Setup(a => a.AuthenticateWithGoogleAsync())
            .ReturnsAsync(new ExternalAuthResultDto { Success = true, Email = "user@test.com" });
        _mockAuthService.Setup(a => a.EmailConfirmed).Returns(true);

        await _viewModel.GoogleLoginCommand.ExecuteAsync(null);

        _mockAuthService.Verify(
            a => a.EnableBiometricLoginAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task LoginAsync_EmailConfirmed_NavigatesToMusicLibrary()
    {
        _viewModel.Email = "test@test.com";
        _viewModel.Password = "password";
        _mockAuthService.Setup(a => a.LoginAsync("test@test.com", "password"))
            .ReturnsAsync((true, string.Empty));
        _mockAuthService.Setup(a => a.EmailConfirmed).Returns(true);

        await _viewModel.LoginCommand.ExecuteAsync(null);

        _mockNavigationService.Verify(n => n.GoToAsync(NavigationRoutes.MusicLibraryRoot), Times.Once);
    }

    [Test]
    public async Task LoginAsync_FromOfferCard_EmailConfirmed_NavigatesToHome()
    {
        _viewModel.ApplyQueryAttributes(new Dictionary<string, object>
        {
            [NavigationRoutes.ReturnToHomeAfterAuthParameter] = true
        });
        _viewModel.Email = "test@test.com";
        _viewModel.Password = "password";
        _mockAuthService.Setup(a => a.LoginAsync("test@test.com", "password"))
            .ReturnsAsync((true, string.Empty));
        _mockAuthService.Setup(a => a.EmailConfirmed).Returns(true);

        await _viewModel.LoginCommand.ExecuteAsync(null);

        _mockNavigationService.Verify(n => n.GoToAsync(NavigationRoutes.HomeRoot), Times.Once);
        _mockNavigationService.Verify(n => n.GoToAsync(NavigationRoutes.MusicLibraryRoot), Times.Never);
    }

    [Test]
    public async Task BiometricLoginAsync_EmailNotConfirmed_NavigatesToVerifyEmail()
    {
        _mockAuthService.Setup(a => a.BiometricLoginAsync())
            .ReturnsAsync((true, string.Empty));
        _mockAuthService.Setup(a => a.EmailConfirmed).Returns(false);
        _mockAuthService.Setup(a => a.UserId).Returns(42);
        _mockAuthService.Setup(a => a.Email).Returns("test@test.com");

        await _viewModel.BiometricLoginCommand.ExecuteAsync(null);

        _mockNavigationService.Verify(n => n.GoToAsync("verify-email", It.IsAny<IDictionary<string, object>>()), Times.Once);
        _mockNavigationService.Verify(n => n.GoToAsync(NavigationRoutes.MusicLibraryRoot), Times.Never);
    }

    [Test]
    public async Task LoginAsync_FromOfferCard_EmailNotConfirmed_PassesReturnHomeToVerifyEmail()
    {
        _viewModel.ApplyQueryAttributes(new Dictionary<string, object>
        {
            [NavigationRoutes.ReturnToHomeAfterAuthParameter] = true
        });
        _viewModel.Email = "test@test.com";
        _viewModel.Password = "password";
        _mockAuthService.Setup(a => a.LoginAsync("test@test.com", "password"))
            .ReturnsAsync((true, string.Empty));
        _mockAuthService.Setup(a => a.EmailConfirmed).Returns(false);
        _mockAuthService.Setup(a => a.UserId).Returns(42);
        _mockAuthService.Setup(a => a.Email).Returns("test@test.com");

        await _viewModel.LoginCommand.ExecuteAsync(null);

        _mockNavigationService.Verify(n => n.GoToAsync("verify-email", It.Is<IDictionary<string, object>>(d =>
            (bool)d[NavigationRoutes.ReturnToHomeAfterAuthParameter])), Times.Once);
    }

    [Test]
    public async Task BiometricLoginAsync_EmailConfirmed_NavigatesToMusicLibrary()
    {
        _mockAuthService.Setup(a => a.BiometricLoginAsync())
            .ReturnsAsync((true, string.Empty));
        _mockAuthService.Setup(a => a.EmailConfirmed).Returns(true);

        await _viewModel.BiometricLoginCommand.ExecuteAsync(null);

        _mockNavigationService.Verify(n => n.GoToAsync(NavigationRoutes.MusicLibraryRoot), Times.Once);
    }

    [Test]
    public async Task GoogleLoginAsync_Success_NavigatesToMusicLibrary()
    {
        _mockAuthService.Setup(a => a.AuthenticateWithGoogleAsync())
            .ReturnsAsync(new ExternalAuthResultDto { Success = true, Email = "user@test.com" });

        await _viewModel.GoogleLoginCommand.ExecuteAsync(null);

        _mockNavigationService.Verify(n => n.GoToAsync(NavigationRoutes.MusicLibraryRoot), Times.Once);
    }

    [Test]
    public async Task GoogleLoginAsync_FromOfferCard_Success_NavigatesToHome()
    {
        _viewModel.ApplyQueryAttributes(new Dictionary<string, object>
        {
            [NavigationRoutes.ReturnToHomeAfterAuthParameter] = true
        });
        _mockAuthService.Setup(a => a.AuthenticateWithGoogleAsync())
            .ReturnsAsync(new ExternalAuthResultDto { Success = true, Email = "user@test.com" });

        await _viewModel.GoogleLoginCommand.ExecuteAsync(null);

        _mockNavigationService.Verify(n => n.GoToAsync(NavigationRoutes.HomeRoot), Times.Once);
        _mockNavigationService.Verify(n => n.GoToAsync(NavigationRoutes.MusicLibraryRoot), Times.Never);
    }

    [Test]
    public async Task GoogleLoginAsync_RequiresRegistration_NavigatesToRegister()
    {
        _mockAuthService.Setup(a => a.AuthenticateWithGoogleAsync())
            .ReturnsAsync(new ExternalAuthResultDto
            {
                RequiresRegistration = true,
                PendingRegistrationToken = "pending-token",
                Email = "new-google@test.com"
            });

        await _viewModel.GoogleLoginCommand.ExecuteAsync(null);

        _mockNavigationService.Verify(n => n.GoToAsync("register", It.Is<IDictionary<string, object>>(d =>
            (string)d["PendingExternalRegistrationToken"] == "pending-token" &&
            (string)d["Email"] == "new-google@test.com"
        )), Times.Once);
    }

    [Test]
    public async Task GoogleLoginAsync_FromOfferCard_RequiresRegistration_PassesReturnHomeToRegister()
    {
        _viewModel.ApplyQueryAttributes(new Dictionary<string, object>
        {
            [NavigationRoutes.ReturnToHomeAfterAuthParameter] = true
        });
        _mockAuthService.Setup(a => a.AuthenticateWithGoogleAsync())
            .ReturnsAsync(new ExternalAuthResultDto
            {
                RequiresRegistration = true,
                PendingRegistrationToken = "pending-token",
                Email = "new-google@test.com"
            });

        await _viewModel.GoogleLoginCommand.ExecuteAsync(null);

        _mockNavigationService.Verify(n => n.GoToAsync("register", It.Is<IDictionary<string, object>>(d =>
            (string)d["PendingExternalRegistrationToken"] == "pending-token" &&
            (string)d["Email"] == "new-google@test.com" &&
            (bool)d[NavigationRoutes.ReturnToHomeAfterAuthParameter])), Times.Once);
    }

    [Test]
    public async Task AppleLoginAsync_Success_NavigatesToMusicLibrary()
    {
        _mockAuthService.Setup(a => a.AuthenticateWithAppleAsync())
            .ReturnsAsync(new ExternalAuthResultDto { Success = true, Email = "user@test.com" });

        await _viewModel.AppleLoginCommand.ExecuteAsync(null);

        _mockNavigationService.Verify(n => n.GoToAsync(NavigationRoutes.MusicLibraryRoot), Times.Once);
        Assert.That(_viewModel.ErrorMessage, Is.Null);
    }

    [Test]
    public async Task AppleLoginAsync_RequiresRegistration_NavigatesToRegisterNamingApple()
    {
        _mockAuthService.Setup(a => a.AuthenticateWithAppleAsync())
            .ReturnsAsync(new ExternalAuthResultDto
            {
                RequiresRegistration = true,
                PendingRegistrationToken = "pending-token",
                Email = "new-apple@test.com"
            });

        await _viewModel.AppleLoginCommand.ExecuteAsync(null);

        _mockNavigationService.Verify(n => n.GoToAsync("register", It.Is<IDictionary<string, object>>(d =>
            (string)d["PendingExternalRegistrationToken"] == "pending-token" &&
            (string)d["PendingExternalProvider"] == ExternalLoginProviders.Apple &&
            (string)d["Email"] == "new-apple@test.com"
        )), Times.Once);
    }

    [Test]
    public async Task AppleLoginAsync_FromOfferCard_RequiresRegistration_PassesReturnHomeToRegister()
    {
        _viewModel.ApplyQueryAttributes(new Dictionary<string, object>
        {
            [NavigationRoutes.ReturnToHomeAfterAuthParameter] = true
        });
        _mockAuthService.Setup(a => a.AuthenticateWithAppleAsync())
            .ReturnsAsync(new ExternalAuthResultDto
            {
                RequiresRegistration = true,
                PendingRegistrationToken = "pending-token",
                Email = "new-apple@test.com"
            });

        await _viewModel.AppleLoginCommand.ExecuteAsync(null);

        _mockNavigationService.Verify(n => n.GoToAsync("register", It.Is<IDictionary<string, object>>(d =>
            (string)d["PendingExternalRegistrationToken"] == "pending-token" &&
            (bool)d[NavigationRoutes.ReturnToHomeAfterAuthParameter])), Times.Once);
    }

    [Test]
    public async Task AppleLoginAsync_Cancelled_ShowsNoErrorAndDoesNotNavigate()
    {
        _mockAuthService.Setup(a => a.AuthenticateWithAppleAsync())
            .ReturnsAsync(new ExternalAuthResultDto { WasCancelled = true });

        await _viewModel.AppleLoginCommand.ExecuteAsync(null);

        Assert.That(_viewModel.ErrorMessage, Is.Null);
        _mockNavigationService.Verify(n => n.GoToAsync(It.IsAny<string>()), Times.Never);
        _mockNavigationService.Verify(
            n => n.GoToAsync(It.IsAny<string>(), It.IsAny<IDictionary<string, object>>()), Times.Never);
    }

    [Test]
    public async Task AppleLoginAsync_Failure_SetsErrorMessage()
    {
        _mockAuthService.Setup(a => a.AuthenticateWithAppleAsync())
            .ReturnsAsync(new ExternalAuthResultDto { ErrorMessage = "Apple sign-in could not be verified." });

        await _viewModel.AppleLoginCommand.ExecuteAsync(null);

        Assert.That(_viewModel.ErrorMessage, Is.EqualTo("Apple sign-in could not be verified."));
    }

    [Test]
    public async Task GoogleLoginAsync_Cancelled_ShowsNoErrorAndDoesNotNavigate()
    {
        _mockAuthService.Setup(a => a.AuthenticateWithGoogleAsync())
            .ReturnsAsync(new ExternalAuthResultDto { WasCancelled = true });

        await _viewModel.GoogleLoginCommand.ExecuteAsync(null);

        Assert.That(_viewModel.ErrorMessage, Is.Null);
        _mockNavigationService.Verify(n => n.GoToAsync(It.IsAny<string>()), Times.Never);
    }

    [Test]
    public void IsAppleSignInVisible_FollowsAuthService()
    {
        _mockAuthService.SetupGet(a => a.IsAppleSignInSupported).Returns(false);
        Assert.That(_viewModel.IsAppleSignInVisible, Is.False);

        _mockAuthService.SetupGet(a => a.IsAppleSignInSupported).Returns(true);
        Assert.That(_viewModel.IsAppleSignInVisible, Is.True);
    }

    [Test]
    public async Task ApplyQueryAttributes_WhenReturnHomeKeyAbsent_ResetsReturnHomeFlag()
    {
        _viewModel.ApplyQueryAttributes(new Dictionary<string, object>
        {
            [NavigationRoutes.ReturnToHomeAfterAuthParameter] = true
        });
        _viewModel.ApplyQueryAttributes(new Dictionary<string, object>());
        _viewModel.Email = "test@test.com";
        _viewModel.Password = "password";
        _mockAuthService.Setup(a => a.LoginAsync("test@test.com", "password"))
            .ReturnsAsync((true, string.Empty));
        _mockAuthService.Setup(a => a.EmailConfirmed).Returns(true);

        await _viewModel.LoginCommand.ExecuteAsync(null);

        _mockNavigationService.Verify(n => n.GoToAsync(NavigationRoutes.MusicLibraryRoot), Times.Once);
        _mockNavigationService.Verify(n => n.GoToAsync(NavigationRoutes.HomeRoot), Times.Never);
    }
}
