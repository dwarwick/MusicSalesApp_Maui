using Moq;
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
            .ReturnsAsync(new GoogleAuthResultDto { Success = true, Email = "user@test.com" });

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
            .ReturnsAsync(new GoogleAuthResultDto { Success = true, Email = "user@test.com" });

        await _viewModel.GoogleLoginCommand.ExecuteAsync(null);

        _mockNavigationService.Verify(n => n.GoToAsync(NavigationRoutes.HomeRoot), Times.Once);
        _mockNavigationService.Verify(n => n.GoToAsync(NavigationRoutes.MusicLibraryRoot), Times.Never);
    }

    [Test]
    public async Task GoogleLoginAsync_RequiresRegistration_NavigatesToRegister()
    {
        _mockAuthService.Setup(a => a.AuthenticateWithGoogleAsync())
            .ReturnsAsync(new GoogleAuthResultDto
            {
                RequiresRegistration = true,
                PendingRegistrationToken = "pending-token",
                Email = "new-google@test.com"
            });

        await _viewModel.GoogleLoginCommand.ExecuteAsync(null);

        _mockNavigationService.Verify(n => n.GoToAsync("register", It.Is<IDictionary<string, object>>(d =>
            (string)d["PendingGoogleRegistrationToken"] == "pending-token" &&
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
            .ReturnsAsync(new GoogleAuthResultDto
            {
                RequiresRegistration = true,
                PendingRegistrationToken = "pending-token",
                Email = "new-google@test.com"
            });

        await _viewModel.GoogleLoginCommand.ExecuteAsync(null);

        _mockNavigationService.Verify(n => n.GoToAsync("register", It.Is<IDictionary<string, object>>(d =>
            (string)d["PendingGoogleRegistrationToken"] == "pending-token" &&
            (string)d["Email"] == "new-google@test.com" &&
            (bool)d[NavigationRoutes.ReturnToHomeAfterAuthParameter])), Times.Once);
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
