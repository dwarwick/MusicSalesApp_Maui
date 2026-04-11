using Moq;
using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Tests.ViewModels;

[TestFixture]
public class ResetPasswordViewModelTests
{
    private Mock<IAuthService> _mockAuthService;
    private Mock<IAlertService> _mockAlertService;
    private Mock<INavigationService> _mockNavigationService;
    private ResetPasswordViewModel _viewModel;

    [SetUp]
    public void Setup()
    {
        _mockAuthService = new Mock<IAuthService>();
        _mockAlertService = new Mock<IAlertService>();
        _mockNavigationService = new Mock<INavigationService>();
        _viewModel = new ResetPasswordViewModel(_mockAuthService.Object, _mockAlertService.Object, _mockNavigationService.Object);
        _viewModel.UserId = 1;
        _viewModel.Email = "test@test.com";
    }

    [Test]
    public async Task ResetPasswordAsync_EmptyCode_SetsErrorMessage()
    {
        _viewModel.Code = "";
        _viewModel.NewPassword = "Passw0rd!";
        _viewModel.ConfirmPassword = "Passw0rd!";

        await _viewModel.ResetPasswordCommand.ExecuteAsync(null);

        Assert.That(_viewModel.ErrorMessage, Does.Contain("6-digit"));
    }

    [Test]
    public async Task ResetPasswordAsync_EmptyPassword_SetsErrorMessage()
    {
        _viewModel.Code = "123456";
        _viewModel.NewPassword = "";
        _viewModel.ConfirmPassword = "";

        await _viewModel.ResetPasswordCommand.ExecuteAsync(null);

        Assert.That(_viewModel.ErrorMessage, Does.Contain("password"));
    }

    [Test]
    public async Task ResetPasswordAsync_PasswordMismatch_SetsErrorMessage()
    {
        _viewModel.Code = "123456";
        _viewModel.NewPassword = "Passw0rd!";
        _viewModel.ConfirmPassword = "Different!";

        await _viewModel.ResetPasswordCommand.ExecuteAsync(null);

        Assert.That(_viewModel.ErrorMessage, Does.Contain("do not match"));
    }

    [Test]
    public async Task ResetPasswordAsync_ServiceError_SetsErrorMessage()
    {
        _viewModel.Code = "123456";
        _viewModel.NewPassword = "Passw0rd!";
        _viewModel.ConfirmPassword = "Passw0rd!";
        _mockAuthService.Setup(a => a.ResetPasswordAsync(1, "123456", "Passw0rd!"))
            .ReturnsAsync((false, "Invalid code."));

        await _viewModel.ResetPasswordCommand.ExecuteAsync(null);

        Assert.That(_viewModel.ErrorMessage, Is.EqualTo("Invalid code."));
    }

    [Test]
    public async Task ResendCodeAsync_Success_ShowsAlert()
    {
        _mockAuthService.Setup(a => a.ForgotPasswordAsync("test@test.com"))
            .ReturnsAsync((true, string.Empty, 1));

        await _viewModel.ResendCodeCommand.ExecuteAsync(null);

        _mockAlertService.Verify(a => a.DisplayAlertAsync("Code Sent", It.IsAny<string>(), "OK"), Times.Once);
    }

    [Test]
    public async Task ResendCodeAsync_Error_SetsErrorMessage()
    {
        _mockAuthService.Setup(a => a.ForgotPasswordAsync("test@test.com"))
            .ReturnsAsync((false, "Too many requests.", 0));

        await _viewModel.ResendCodeCommand.ExecuteAsync(null);

        Assert.That(_viewModel.ErrorMessage, Is.EqualTo("Too many requests."));
    }
}
