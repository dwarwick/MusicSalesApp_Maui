using Moq;
using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Tests.ViewModels;

[TestFixture]
public class ForgotPasswordViewModelTests
{
    private Mock<IAuthService> _mockAuthService;
    private Mock<INavigationService> _mockNavigationService;
    private ForgotPasswordViewModel _viewModel;

    [SetUp]
    public void Setup()
    {
        _mockAuthService = new Mock<IAuthService>();
        _mockNavigationService = new Mock<INavigationService>();
        _viewModel = new ForgotPasswordViewModel(_mockAuthService.Object, _mockNavigationService.Object);
    }

    [Test]
    public async Task SendResetCodeAsync_EmptyEmail_SetsErrorMessage()
    {
        _viewModel.Email = "";

        await _viewModel.SendResetCodeCommand.ExecuteAsync(null);

        Assert.That(_viewModel.ErrorMessage, Does.Contain("email"));
    }

    [Test]
    public async Task SendResetCodeAsync_ServiceError_SetsErrorMessage()
    {
        _viewModel.Email = "test@test.com";
        _mockAuthService.Setup(a => a.ForgotPasswordAsync("test@test.com"))
            .ReturnsAsync((false, "User not found.", 0));

        await _viewModel.SendResetCodeCommand.ExecuteAsync(null);

        Assert.That(_viewModel.ErrorMessage, Is.EqualTo("User not found."));
    }

    [Test]
    public async Task SendResetCodeAsync_Exception_SetsConnectionError()
    {
        _viewModel.Email = "test@test.com";
        _mockAuthService.Setup(a => a.ForgotPasswordAsync("test@test.com"))
            .ThrowsAsync(new Exception("Network failure"));

        await _viewModel.SendResetCodeCommand.ExecuteAsync(null);

        Assert.That(_viewModel.ErrorMessage, Does.Contain("Connection error"));
        Assert.That(_viewModel.IsBusy, Is.False);
    }
}
