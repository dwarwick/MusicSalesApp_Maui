using Moq;
using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Tests.ViewModels;

[TestFixture]
public class AccountSettingsViewModelTests
{
    private Mock<IAuthService> _mockAuthService;
    private Mock<IAlertService> _mockAlertService;
    private Mock<INavigationService> _mockNavigationService;
    private Mock<IMusicService> _mockMusicService;
    private AccountSettingsViewModel _viewModel;

    [SetUp]
    public void Setup()
    {
        _mockAuthService = new Mock<IAuthService>();
        _mockAlertService = new Mock<IAlertService>();
        _mockNavigationService = new Mock<INavigationService>();
        _mockMusicService = new Mock<IMusicService>();

        _mockAuthService.Setup(a => a.Email).Returns("test@example.com");
        _mockAuthService.Setup(a => a.HasActiveSubscription).Returns(false);

        _viewModel = CreateViewModel();
    }

    private AccountSettingsViewModel CreateViewModel()
    {
        return new AccountSettingsViewModel(
            _mockAuthService.Object,
            _mockAlertService.Object,
            _mockNavigationService.Object,
            _mockMusicService.Object);
    }

    [Test]
    public void InitialState_PropertiesSetFromAuthService()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.UserEmail, Is.EqualTo("test@example.com"));
            Assert.That(_viewModel.HasActiveSubscription, Is.False);
            Assert.That(_viewModel.ShowDeleteConfirmation, Is.False);
            Assert.That(_viewModel.ConfirmationText, Is.EqualTo(string.Empty));
            Assert.That(_viewModel.ErrorMessage, Is.EqualTo(string.Empty));
            Assert.That(_viewModel.IsCancelling, Is.False);
            Assert.That(_viewModel.IsDeleting, Is.False);
        });
    }

    [Test]
    public void ShowCancelSubscription_TrueWhenHasActiveSubscription()
    {
        _viewModel.HasActiveSubscription = true;
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
        Assert.That(_viewModel.CanDeleteAccount, Is.False);
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
        _mockAlertService.Setup(a => a.ShowConfirmAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);
        _mockMusicService.Setup(m => m.CancelSubscriptionAsync())
            .ReturnsAsync((true, (DateTime?)DateTime.UtcNow.AddDays(30)));

        await _viewModel.CancelSubscriptionCommand.ExecuteAsync(null);

        _mockAuthService.Verify(a => a.RefreshUserStatusAsync(), Times.Once);
        _mockAlertService.Verify(a => a.DisplayAlertAsync("Subscription Cancelled", It.IsAny<string>(), "OK"), Times.Once);
    }

    [Test]
    public async Task CancelSubscriptionCommand_Failure_ShowsError()
    {
        _mockAlertService.Setup(a => a.ShowConfirmAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);
        _mockMusicService.Setup(m => m.CancelSubscriptionAsync())
            .ReturnsAsync((false, (DateTime?)null));

        await _viewModel.CancelSubscriptionCommand.ExecuteAsync(null);

        _mockAlertService.Verify(a => a.DisplayAlertAsync("Error", It.Is<string>(s => s.Contains("Failed to cancel")), "OK"), Times.Once);
        _mockAuthService.Verify(a => a.RefreshUserStatusAsync(), Times.Never);
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
    public void AuthStateChanged_RefreshesProperties()
    {
        _mockAuthService.Setup(a => a.Email).Returns("new@example.com");
        _mockAuthService.Setup(a => a.HasActiveSubscription).Returns(true);

        _mockAuthService.Raise(a => a.AuthStateChanged += null);

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.UserEmail, Is.EqualTo("new@example.com"));
            Assert.That(_viewModel.HasActiveSubscription, Is.True);
        });
    }
}
