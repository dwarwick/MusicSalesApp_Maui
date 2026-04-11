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
        _mockAlertService = new Mock<IAlertService>();
        _mockNavigationService = new Mock<INavigationService>();
        _viewModel = new LoginViewModel(_mockAuthService.Object, _mockAlertService.Object, _mockNavigationService.Object);
    }

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
    public void BiometricVisible_ReflectsAuthServiceState()
    {
        _mockAuthService.Setup(a => a.IsBiometricEnabled).Returns(true);
        var vm = new LoginViewModel(_mockAuthService.Object, _mockAlertService.Object, _mockNavigationService.Object);

        Assert.That(vm.BiometricVisible, Is.True);
    }

    [Test]
    public void BiometricVisible_FalseWhenNotEnabled()
    {
        _mockAuthService.Setup(a => a.IsBiometricEnabled).Returns(false);
        var vm = new LoginViewModel(_mockAuthService.Object, _mockAlertService.Object, _mockNavigationService.Object);

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
        _mockNavigationService.Verify(n => n.GoToAsync("//MusicLibrary"), Times.Never);
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

        _mockNavigationService.Verify(n => n.GoToAsync("//MusicLibrary"), Times.Once);
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
        _mockNavigationService.Verify(n => n.GoToAsync("//MusicLibrary"), Times.Never);
    }

    [Test]
    public async Task BiometricLoginAsync_EmailConfirmed_NavigatesToMusicLibrary()
    {
        _mockAuthService.Setup(a => a.BiometricLoginAsync())
            .ReturnsAsync((true, string.Empty));
        _mockAuthService.Setup(a => a.EmailConfirmed).Returns(true);

        await _viewModel.BiometricLoginCommand.ExecuteAsync(null);

        _mockNavigationService.Verify(n => n.GoToAsync("//MusicLibrary"), Times.Once);
    }
}
