using Moq;
using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Tests.ViewModels;

[TestFixture]
public class VerifyEmailViewModelTests
{
    private Mock<IAuthService> _mockAuthService;
    private Mock<IAlertService> _mockAlertService;
    private Mock<INavigationService> _mockNavigationService;
    private VerifyEmailViewModel _viewModel;

    [SetUp]
    public void Setup()
    {
        _mockAuthService = new Mock<IAuthService>();
        _mockAlertService = new Mock<IAlertService>();
        _mockNavigationService = new Mock<INavigationService>();
        _viewModel = new VerifyEmailViewModel(_mockAuthService.Object, _mockAlertService.Object, _mockNavigationService.Object);
        _viewModel.UserId = 1;
        _viewModel.Email = "test@test.com";
    }

    // --- Biometric enrolment: this screen is one of only two places that can save credentials ---

    private void ArrangeSuccessfulVerification()
    {
        _viewModel.Code = "123456";
        _viewModel.Password = "Passw0rd!";
        _mockAuthService.Setup(a => a.VerifyCodeAsync(1, "123456"))
            .ReturnsAsync((true, string.Empty, (LoginResponseDto?)null));
        _mockAuthService.Setup(a => a.HasBiometricCredentialsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _mockAlertService.Setup(a => a.ShowConfirmAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);
    }

    [Test]
    public async Task VerifyAsync_OnAndroid_OffersBiometricEnrolment()
    {
        _viewModel.IsBiometricLoginSupported = true;
        ArrangeSuccessfulVerification();

        await _viewModel.VerifyCommand.ExecuteAsync(null);

        _mockAuthService.Verify(a => a.EnableBiometricLoginAsync("test@test.com", "Passw0rd!"), Times.Once);
    }

    [Test]
    public async Task VerifyAsync_OnAPlatformWithoutBiometrics_NeverSavesCredentials()
    {
        // AuthService.PromptBiometricAsync is #if ANDROID and answers "not supported on this
        // platform" elsewhere. Accepting the offer on iOS would write a plaintext password to the
        // keychain for a prompt that can never consume it.
        _viewModel.IsBiometricLoginSupported = false;
        ArrangeSuccessfulVerification();

        await _viewModel.VerifyCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            _mockAuthService.Verify(
                a => a.EnableBiometricLoginAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            _mockAlertService.Verify(
                a => a.ShowConfirmAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
                Times.Never);
        });
    }

    [Test]
    public async Task VerifyAsync_WhenCredentialsAreAlreadySaved_DoesNotAskAgain()
    {
        _viewModel.IsBiometricLoginSupported = true;
        ArrangeSuccessfulVerification();
        _mockAuthService.Setup(a => a.HasBiometricCredentialsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await _viewModel.VerifyCommand.ExecuteAsync(null);

        _mockAuthService.Verify(
            a => a.EnableBiometricLoginAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task VerifyAsync_WithNoPasswordInHand_DoesNotOfferBiometrics()
    {
        // Arriving here from a deep link rather than registration leaves Password empty, and there is
        // nothing to save.
        _viewModel.IsBiometricLoginSupported = true;
        ArrangeSuccessfulVerification();
        _viewModel.Password = string.Empty;

        await _viewModel.VerifyCommand.ExecuteAsync(null);

        _mockAuthService.Verify(
            a => a.EnableBiometricLoginAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task VerifyAsync_WhenTheUserDeclinesTheOffer_SavesNothing()
    {
        _viewModel.IsBiometricLoginSupported = true;
        ArrangeSuccessfulVerification();
        _mockAlertService.Setup(a => a.ShowConfirmAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(false);

        await _viewModel.VerifyCommand.ExecuteAsync(null);

        _mockAuthService.Verify(
            a => a.EnableBiometricLoginAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task VerifyAsync_WhenVerificationFails_DoesNotOfferBiometrics()
    {
        _viewModel.IsBiometricLoginSupported = true;
        ArrangeSuccessfulVerification();
        _mockAuthService.Setup(a => a.VerifyCodeAsync(1, "123456"))
            .ReturnsAsync((false, "Invalid code.", (LoginResponseDto?)null));

        await _viewModel.VerifyCommand.ExecuteAsync(null);

        _mockAuthService.Verify(
            a => a.EnableBiometricLoginAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task VerifyAsync_EmptyCode_SetsErrorMessage()
    {
        _viewModel.Code = "";

        await _viewModel.VerifyCommand.ExecuteAsync(null);

        Assert.That(_viewModel.ErrorMessage, Does.Contain("6-digit"));
    }

    [Test]
    public async Task VerifyAsync_ShortCode_SetsErrorMessage()
    {
        _viewModel.Code = "123";

        await _viewModel.VerifyCommand.ExecuteAsync(null);

        Assert.That(_viewModel.ErrorMessage, Does.Contain("6-digit"));
    }

    [Test]
    public async Task VerifyAsync_ServiceError_SetsErrorMessage()
    {
        _viewModel.Code = "123456";
        _mockAuthService.Setup(a => a.VerifyCodeAsync(1, "123456"))
            .ReturnsAsync((false, "Invalid code.", (LoginResponseDto?)null));

        await _viewModel.VerifyCommand.ExecuteAsync(null);

        Assert.That(_viewModel.ErrorMessage, Is.EqualTo("Invalid code."));
    }

    [Test]
    public async Task ResendCodeAsync_Success_SetsStatusMessage()
    {
        _mockAuthService.Setup(a => a.ResendCodeAsync(1))
            .ReturnsAsync((true, string.Empty));

        await _viewModel.ResendCodeCommand.ExecuteAsync(null);

        Assert.That(_viewModel.StatusMessage, Does.Contain("new code"));
        Assert.That(_viewModel.ErrorMessage, Is.Null);
    }

    [Test]
    public async Task ResendCodeAsync_Error_SetsErrorMessage()
    {
        _mockAuthService.Setup(a => a.ResendCodeAsync(1))
            .ReturnsAsync((false, "Please wait before requesting another code."));

        await _viewModel.ResendCodeCommand.ExecuteAsync(null);

        Assert.That(_viewModel.ErrorMessage, Is.EqualTo("Please wait before requesting another code."));
    }

    [Test]
    public async Task VerifyAsync_ExpiredCode_ShowsServerErrorMessage()
    {
        _viewModel.Code = "123456";
        _mockAuthService.Setup(a => a.VerifyCodeAsync(1, "123456"))
            .ReturnsAsync((false, "Invalid or expired verification code.", (LoginResponseDto?)null));

        await _viewModel.VerifyCommand.ExecuteAsync(null);

        Assert.That(_viewModel.ErrorMessage, Is.EqualTo("Invalid or expired verification code."));
    }

    [Test]
    public async Task VerifyAsync_WrongCode_DoesNotNavigate()
    {
        _viewModel.Code = "999999";
        _mockAuthService.Setup(a => a.VerifyCodeAsync(1, "999999"))
            .ReturnsAsync((false, "Invalid or expired verification code.", (LoginResponseDto?)null));

        await _viewModel.VerifyCommand.ExecuteAsync(null);

        _mockNavigationService.Verify(n => n.GoToAsync(It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task VerifyAsync_Success_NavigatesToMusicLibrary()
    {
        _viewModel.Code = "123456";
        _mockAuthService.Setup(a => a.VerifyCodeAsync(1, "123456"))
            .ReturnsAsync((true, string.Empty, new LoginResponseDto()));

        await _viewModel.VerifyCommand.ExecuteAsync(null);

        _mockNavigationService.Verify(n => n.GoToAsync(NavigationRoutes.MusicLibraryRoot), Times.Once);
    }

    [Test]
    public async Task VerifyAsync_FromOfferCard_Success_NavigatesToHome()
    {
        _viewModel.ReturnToHomeAfterAuth = true;
        _viewModel.Code = "123456";
        _mockAuthService.Setup(a => a.VerifyCodeAsync(1, "123456"))
            .ReturnsAsync((true, string.Empty, new LoginResponseDto()));

        await _viewModel.VerifyCommand.ExecuteAsync(null);

        _mockNavigationService.Verify(n => n.GoToAsync(NavigationRoutes.HomeRoot), Times.Once);
        _mockNavigationService.Verify(n => n.GoToAsync(NavigationRoutes.MusicLibraryRoot), Times.Never);
    }

    [Test]
    public async Task VerifyAsync_Success_ClearsErrorMessage()
    {
        _viewModel.Code = "123456";
        _viewModel.ErrorMessage = "Previous error";
        _mockAuthService.Setup(a => a.VerifyCodeAsync(1, "123456"))
            .ReturnsAsync((true, string.Empty, new LoginResponseDto()));

        await _viewModel.VerifyCommand.ExecuteAsync(null);

        Assert.That(_viewModel.ErrorMessage, Is.Null);
    }

    [Test]
    public async Task VerifyAsync_ConnectionError_SetsConnectionErrorMessage()
    {
        _viewModel.Code = "123456";
        _mockAuthService.Setup(a => a.VerifyCodeAsync(1, "123456"))
            .ThrowsAsync(new Exception("Network unreachable"));

        await _viewModel.VerifyCommand.ExecuteAsync(null);

        Assert.That(_viewModel.ErrorMessage, Does.Contain("Connection error"));
    }

    [Test]
    public async Task VerifyAsync_WhitespaceCode_TrimsAndVerifies()
    {
        _viewModel.Code = "  123456  ";
        _mockAuthService.Setup(a => a.VerifyCodeAsync(1, "123456"))
            .ReturnsAsync((true, string.Empty, new LoginResponseDto()));

        await _viewModel.VerifyCommand.ExecuteAsync(null);

        _mockAuthService.Verify(a => a.VerifyCodeAsync(1, "123456"), Times.Once);
    }

    [Test]
    public async Task VerifyAsync_SetsBusyDuringOperation()
    {
        _viewModel.Code = "123456";
        var busyDuringCall = false;
        _mockAuthService.Setup(a => a.VerifyCodeAsync(1, "123456"))
            .ReturnsAsync(() =>
            {
                busyDuringCall = _viewModel.IsBusy;
                return (true, string.Empty, new LoginResponseDto());
            });

        await _viewModel.VerifyCommand.ExecuteAsync(null);

        Assert.That(busyDuringCall, Is.True, "Should be busy during the verify call");
        Assert.That(_viewModel.IsBusy, Is.False, "Should not be busy after the call completes");
    }

    #region ChangeEmail Tests

    [Test]
    public void ToggleChangeEmail_TogglesVisibility()
    {
        Assert.That(_viewModel.ShowChangeEmail, Is.False);

        _viewModel.ToggleChangeEmailCommand.Execute(null);
        Assert.That(_viewModel.ShowChangeEmail, Is.True);
        Assert.That(_viewModel.ChangeEmailToggleText, Is.EqualTo("Cancel"));

        _viewModel.ToggleChangeEmailCommand.Execute(null);
        Assert.That(_viewModel.ShowChangeEmail, Is.False);
        Assert.That(_viewModel.ChangeEmailToggleText, Is.EqualTo("Change Email"));
    }

    [Test]
    public void ToggleChangeEmail_SetsNewEmailToCurrentEmail()
    {
        _viewModel.Email = "current@test.com";

        _viewModel.ToggleChangeEmailCommand.Execute(null);

        Assert.That(_viewModel.NewEmail, Is.EqualTo("current@test.com"));
    }

    [Test]
    public async Task ChangeEmailAsync_EmptyNewEmail_SetsErrorMessage()
    {
        _viewModel.NewEmail = "";

        await _viewModel.ChangeEmailCommand.ExecuteAsync(null);

        Assert.That(_viewModel.ErrorMessage, Does.Contain("email"));
    }

    [Test]
    public async Task ChangeEmailAsync_SameEmail_SetsErrorMessage()
    {
        _viewModel.Email = "test@test.com";
        _viewModel.NewEmail = "test@test.com";

        await _viewModel.ChangeEmailCommand.ExecuteAsync(null);

        Assert.That(_viewModel.ErrorMessage, Does.Contain("different"));
    }

    [Test]
    public async Task ChangeEmailAsync_ServiceError_SetsErrorMessage()
    {
        _viewModel.Email = "old@test.com";
        _viewModel.NewEmail = "new@test.com";
        _mockAuthService.Setup(a => a.ChangeEmailAsync(1, "new@test.com"))
            .ReturnsAsync((false, "Email already in use."));

        await _viewModel.ChangeEmailCommand.ExecuteAsync(null);

        Assert.That(_viewModel.ErrorMessage, Is.EqualTo("Email already in use."));
    }

    [Test]
    public async Task ChangeEmailAsync_Success_UpdatesEmail()
    {
        _viewModel.Email = "old@test.com";
        _viewModel.NewEmail = "new@test.com";
        _viewModel.ShowChangeEmail = true;
        _mockAuthService.Setup(a => a.ChangeEmailAsync(1, "new@test.com"))
            .ReturnsAsync((true, string.Empty));

        await _viewModel.ChangeEmailCommand.ExecuteAsync(null);

        Assert.That(_viewModel.Email, Is.EqualTo("new@test.com"));
        Assert.That(_viewModel.ShowChangeEmail, Is.False);
        Assert.That(_viewModel.StatusMessage, Does.Contain("new@test.com"));
    }

    [Test]
    public async Task ChangeEmailAsync_ConnectionError_SetsErrorMessage()
    {
        _viewModel.Email = "old@test.com";
        _viewModel.NewEmail = "new@test.com";
        _mockAuthService.Setup(a => a.ChangeEmailAsync(1, "new@test.com"))
            .ThrowsAsync(new Exception("Network failure"));

        await _viewModel.ChangeEmailCommand.ExecuteAsync(null);

        Assert.That(_viewModel.ErrorMessage, Does.Contain("Connection error"));
    }

    #endregion

    #region Skip Tests

    [Test]
    public async Task SkipAsync_NavigatesToMusicLibrary()
    {
        await _viewModel.SkipCommand.ExecuteAsync(null);

        _mockNavigationService.Verify(n => n.GoToAsync(NavigationRoutes.MusicLibraryRoot), Times.Once);
    }

    #endregion

    #region OnAppearing Auto-Send Tests

    [Test]
    public async Task OnAppearingAsync_SendsCodeAutomatically()
    {
        _mockAuthService.Setup(a => a.ResendCodeAsync(1))
            .ReturnsAsync((true, string.Empty));

        await _viewModel.OnAppearingAsync();

        _mockAuthService.Verify(a => a.ResendCodeAsync(1), Times.Once);
        Assert.That(_viewModel.StatusMessage, Does.Contain("verification code"));
    }

    [Test]
    public async Task OnAppearingAsync_DoesNotSendWhenUserIdIsZero()
    {
        _viewModel.UserId = 0;

        await _viewModel.OnAppearingAsync();

        _mockAuthService.Verify(a => a.ResendCodeAsync(It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task OnAppearingAsync_OnlySendsOnce()
    {
        _mockAuthService.Setup(a => a.ResendCodeAsync(1))
            .ReturnsAsync((true, string.Empty));

        await _viewModel.OnAppearingAsync();
        await _viewModel.OnAppearingAsync();

        _mockAuthService.Verify(a => a.ResendCodeAsync(1), Times.Once);
    }

    [Test]
    public async Task OnAppearingAsync_FailureSilentlyIgnored()
    {
        _mockAuthService.Setup(a => a.ResendCodeAsync(1))
            .ThrowsAsync(new Exception("Network error"));

        await _viewModel.OnAppearingAsync();

        Assert.That(_viewModel.ErrorMessage, Is.Null);
    }

    [Test]
    public async Task OnAppearingAsync_CodeAlreadySent_SkipsResendAndSetsStatus()
    {
        _viewModel.CodeAlreadySent = true;

        await _viewModel.OnAppearingAsync();

        _mockAuthService.Verify(a => a.ResendCodeAsync(It.IsAny<int>()), Times.Never);
        Assert.That(_viewModel.StatusMessage, Does.Contain("verification code"));
    }

    [Test]
    public async Task OnAppearingAsync_CodeAlreadySent_False_StillCallsResend()
    {
        _viewModel.CodeAlreadySent = false;
        _mockAuthService.Setup(a => a.ResendCodeAsync(1))
            .ReturnsAsync((true, string.Empty));

        await _viewModel.OnAppearingAsync();

        _mockAuthService.Verify(a => a.ResendCodeAsync(1), Times.Once);
        Assert.That(_viewModel.StatusMessage, Does.Contain("verification code"));
    }

    [Test]
    public async Task OnAppearingAsync_CodeAlreadySent_OnlySetsStatusOnce()
    {
        _viewModel.CodeAlreadySent = true;

        await _viewModel.OnAppearingAsync();
        await _viewModel.OnAppearingAsync();

        _mockAuthService.Verify(a => a.ResendCodeAsync(It.IsAny<int>()), Times.Never);
        Assert.That(_viewModel.StatusMessage, Does.Contain("verification code"));
    }

    #endregion
}
