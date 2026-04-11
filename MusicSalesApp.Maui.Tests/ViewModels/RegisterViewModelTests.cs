using Moq;
using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Tests.ViewModels;

[TestFixture]
public class RegisterViewModelTests
{
    private Mock<IAuthService> _mockAuthService;
    private Mock<INavigationService> _mockNavigationService;
    private RegisterViewModel _viewModel;

    [SetUp]
    public void Setup()
    {
        _mockAuthService = new Mock<IAuthService>();
        _mockNavigationService = new Mock<INavigationService>();
        _viewModel = new RegisterViewModel(_mockAuthService.Object, _mockNavigationService.Object);
    }

    [Test]
    public async Task RegisterAsync_EmptyEmail_SetsErrorMessage()
    {
        _viewModel.Email = "";
        _viewModel.Password = "password";
        _viewModel.ConfirmPassword = "password";

        await _viewModel.RegisterCommand.ExecuteAsync(null);

        Assert.That(_viewModel.ErrorMessage, Does.Contain("email"));
    }

    [Test]
    public async Task RegisterAsync_EmptyPassword_SetsErrorMessage()
    {
        _viewModel.Email = "test@test.com";
        _viewModel.Password = "";
        _viewModel.ConfirmPassword = "";

        await _viewModel.RegisterCommand.ExecuteAsync(null);

        Assert.That(_viewModel.ErrorMessage, Does.Contain("password"));
    }

    [Test]
    public async Task RegisterAsync_PasswordMismatch_SetsErrorMessage()
    {
        _viewModel.Email = "test@test.com";
        _viewModel.Password = "password1";
        _viewModel.ConfirmPassword = "password2";

        await _viewModel.RegisterCommand.ExecuteAsync(null);

        Assert.That(_viewModel.ErrorMessage, Does.Contain("do not match"));
    }

    [Test]
    public async Task RegisterAsync_ServiceReturnsError_SetsErrorMessage()
    {
        _viewModel.Email = "test@test.com";
        _viewModel.Password = "Passw0rd!";
        _viewModel.ConfirmPassword = "Passw0rd!";
        _mockAuthService.Setup(a => a.RegisterAsync("test@test.com", "Passw0rd!"))
            .ReturnsAsync((false, "Email already taken.", 0));

        await _viewModel.RegisterCommand.ExecuteAsync(null);

        Assert.That(_viewModel.ErrorMessage, Is.EqualTo("Email already taken."));
    }

    [Test]
    public async Task RegisterAsync_Exception_SetsConnectionError()
    {
        _viewModel.Email = "test@test.com";
        _viewModel.Password = "Passw0rd!";
        _viewModel.ConfirmPassword = "Passw0rd!";
        _mockAuthService.Setup(a => a.RegisterAsync("test@test.com", "Passw0rd!"))
            .ThrowsAsync(new Exception("Network failure"));

        await _viewModel.RegisterCommand.ExecuteAsync(null);

        Assert.That(_viewModel.ErrorMessage, Does.Contain("Connection error"));
        Assert.That(_viewModel.IsBusy, Is.False);
    }
}
