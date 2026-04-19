using Moq;
using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Tests.ViewModels;

[TestFixture]
public class RegisterViewModelTests
{
    private Mock<IAuthService> _mockAuthService;
    private Mock<INavigationService> _mockNavigationService;
    private Mock<IAppConfig> _mockAppConfig;
    private RegisterViewModel _viewModel;

    [SetUp]
    public void Setup()
    {
        _mockAuthService = new Mock<IAuthService>();
        _mockNavigationService = new Mock<INavigationService>();
        _mockAppConfig = new Mock<IAppConfig>();
        _mockAppConfig.Setup(c => c.WebBaseUrl).Returns("https://streamtunes.net");
        _viewModel = new RegisterViewModel(
            _mockAuthService.Object,
            _mockNavigationService.Object,
            _mockAppConfig.Object);
    }

    private void AcceptAllTerms()
    {
        _viewModel.AcceptTermsOfUse = true;
        _viewModel.AcceptPrivacyPolicy = true;
        _viewModel.AcceptRefundPolicy = true;
    }

    [Test]
    public async Task RegisterAsync_EmptyEmail_SetsErrorMessage()
    {
        AcceptAllTerms();
        _viewModel.Email = "";
        _viewModel.Password = "password";
        _viewModel.ConfirmPassword = "password";

        await _viewModel.RegisterCommand.ExecuteAsync(null);

        Assert.That(_viewModel.ErrorMessage, Does.Contain("email"));
    }

    [Test]
    public async Task RegisterAsync_EmptyPassword_SetsErrorMessage()
    {
        AcceptAllTerms();
        _viewModel.Email = "test@test.com";
        _viewModel.Password = "";
        _viewModel.ConfirmPassword = "";

        await _viewModel.RegisterCommand.ExecuteAsync(null);

        Assert.That(_viewModel.ErrorMessage, Does.Contain("password"));
    }

    [Test]
    public async Task RegisterAsync_PasswordMismatch_SetsErrorMessage()
    {
        AcceptAllTerms();
        _viewModel.Email = "test@test.com";
        _viewModel.Password = "password1";
        _viewModel.ConfirmPassword = "password2";

        await _viewModel.RegisterCommand.ExecuteAsync(null);

        Assert.That(_viewModel.ErrorMessage, Does.Contain("do not match"));
    }

    [Test]
    public async Task RegisterAsync_ServiceReturnsError_SetsErrorMessage()
    {
        AcceptAllTerms();
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
        AcceptAllTerms();
        _viewModel.Email = "test@test.com";
        _viewModel.Password = "Passw0rd!";
        _viewModel.ConfirmPassword = "Passw0rd!";
        _mockAuthService.Setup(a => a.RegisterAsync("test@test.com", "Passw0rd!"))
            .ThrowsAsync(new Exception("Network failure"));

        await _viewModel.RegisterCommand.ExecuteAsync(null);

        Assert.That(_viewModel.ErrorMessage, Does.Contain("Connection error"));
        Assert.That(_viewModel.IsBusy, Is.False);
    }

    // --- Terms acceptance ---

    [Test]
    public void CanRegister_FalseByDefault()
    {
        Assert.That(_viewModel.CanRegister, Is.False);
    }

    [Test]
    public void CanRegister_FalseWhenOnlyTermsAccepted()
    {
        _viewModel.AcceptTermsOfUse = true;
        Assert.That(_viewModel.CanRegister, Is.False);
    }

    [Test]
    public void CanRegister_FalseWhenOnlyPrivacyAccepted()
    {
        _viewModel.AcceptPrivacyPolicy = true;
        Assert.That(_viewModel.CanRegister, Is.False);
    }

    [Test]
    public void CanRegister_TrueWhenBothAccepted()
    {
        _viewModel.AcceptTermsOfUse = true;
        _viewModel.AcceptPrivacyPolicy = true;
        _viewModel.AcceptRefundPolicy = true;
        Assert.That(_viewModel.CanRegister, Is.True);
    }

    [Test]
    public void CanRegister_FalseWhenOnlyRefundAccepted()
    {
        _viewModel.AcceptRefundPolicy = true;
        Assert.That(_viewModel.CanRegister, Is.False);
    }

    [Test]
    public void CanRegister_FalseWhenRefundNotAccepted()
    {
        _viewModel.AcceptTermsOfUse = true;
        _viewModel.AcceptPrivacyPolicy = true;
        Assert.That(_viewModel.CanRegister, Is.False);
    }

    [Test]
    public async Task RegisterAsync_TermsNotAccepted_SetsErrorMessage()
    {
        _viewModel.Email = "test@test.com";
        _viewModel.Password = "Passw0rd!";
        _viewModel.ConfirmPassword = "Passw0rd!";

        await _viewModel.RegisterCommand.ExecuteAsync(null);

        Assert.That(_viewModel.ErrorMessage, Does.Contain("Terms of Use").And.Contain("Privacy Policy").And.Contain("Refund Policy"));
        _mockAuthService.Verify(a => a.RegisterAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task RegisterAsync_OnlyTermsAccepted_SetsErrorMessage()
    {
        _viewModel.AcceptTermsOfUse = true;
        _viewModel.Email = "test@test.com";
        _viewModel.Password = "Passw0rd!";
        _viewModel.ConfirmPassword = "Passw0rd!";

        await _viewModel.RegisterCommand.ExecuteAsync(null);

        Assert.That(_viewModel.ErrorMessage, Does.Contain("Terms of Use").And.Contain("Privacy Policy").And.Contain("Refund Policy"));
        _mockAuthService.Verify(a => a.RegisterAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task RegisterAsync_BothTermsAccepted_ProceedsWithRegistration()
    {
        AcceptAllTerms();
        _viewModel.Email = "test@test.com";
        _viewModel.Password = "Passw0rd!";
        _viewModel.ConfirmPassword = "Passw0rd!";
        _mockAuthService.Setup(a => a.RegisterAsync("test@test.com", "Passw0rd!"))
            .ReturnsAsync((true, string.Empty, 42));

        await _viewModel.RegisterCommand.ExecuteAsync(null);

        _mockAuthService.Verify(a => a.RegisterAsync("test@test.com", "Passw0rd!"), Times.Once);
        _mockNavigationService.Verify(n => n.GoToAsync("verify-email", It.Is<Dictionary<string, object>>(d =>
            (int)d["UserId"] == 42 &&
            (string)d["Email"] == "test@test.com" &&
            (string)d["Password"] == "Passw0rd!" &&
            (bool)d["CodeAlreadySent"] == true)), Times.Once);
    }

    // --- Open policy commands navigate in-app ---

    [Test]
    public async Task OpenTermsOfUseCommand_NavigatesToPolicyPage()
    {
        await _viewModel.OpenTermsOfUseCommand.ExecuteAsync(null);

        _mockNavigationService.Verify(n => n.GoToAsync("policy", It.Is<Dictionary<string, object>>(d =>
            (string)d["title"] == "Terms of Use" && (string)d["path"] == "/terms-of-use")), Times.Once);
    }

    [Test]
    public async Task OpenPrivacyPolicyCommand_NavigatesToPolicyPage()
    {
        await _viewModel.OpenPrivacyPolicyCommand.ExecuteAsync(null);

        _mockNavigationService.Verify(n => n.GoToAsync("policy", It.Is<Dictionary<string, object>>(d =>
            (string)d["title"] == "Privacy Policy" && (string)d["path"] == "/privacy-policy")), Times.Once);
    }

    [Test]
    public async Task OpenRefundPolicyCommand_NavigatesToPolicyPage()
    {
        await _viewModel.OpenRefundPolicyCommand.ExecuteAsync(null);

        _mockNavigationService.Verify(n => n.GoToAsync("policy", It.Is<Dictionary<string, object>>(d =>
            (string)d["title"] == "User Refund Policy" && (string)d["path"] == "/user-refund-policy")), Times.Once);
    }
}
