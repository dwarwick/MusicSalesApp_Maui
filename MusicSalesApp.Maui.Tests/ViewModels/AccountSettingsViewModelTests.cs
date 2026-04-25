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
    private Mock<IBillingService> _mockBillingService;
    private AccountSettingsViewModel _viewModel;

    [SetUp]
    public void Setup()
    {
        _mockAuthService = new Mock<IAuthService>();
        _mockAlertService = new Mock<IAlertService>();
        _mockNavigationService = new Mock<INavigationService>();
        _mockMusicService = new Mock<IMusicService>();
        _mockBillingService = new Mock<IBillingService>();

        _mockAuthService.Setup(a => a.Email).Returns("test@example.com");
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
            _mockMusicService.Object,
            _mockBillingService.Object);
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
        });
    }

    [Test]
    public void ShowCancelSubscription_TrueWhenHasActiveSubscription()
    {
        _viewModel.HasActiveSubscription = true;
        _viewModel.SubscriptionEndDate = null;
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
    public async Task LoadCommand_CancelledSubscriptionWithRemainingAccess_HidesCancelButtonAndAllowsNewSubscription()
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
            Assert.That(_viewModel.CanCreateSubscription, Is.True);
            Assert.That(_viewModel.CanDeleteAccount, Is.True);
            Assert.That(_viewModel.SubscriptionStatusText, Is.EqualTo("Cancelled"));
        });
    }

    [Test]
    public async Task LoadCommand_ExpiredSubscription_ShowsExpiredStateAndAllowsNewSubscription()
    {
        _mockMusicService.Setup(m => m.GetSubscriptionStatusAsync())
            .ReturnsAsync(new SubscriptionStatusDto
            {
                HasSubscription = false,
                Status = "EXPIRED",
                EndDate = DateTime.UtcNow.AddDays(-1),
                BillingSource = "GooglePlay"
            });

        await _viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.HasActiveSubscription, Is.False);
            Assert.That(_viewModel.ShowCancelSubscription, Is.False);
            Assert.That(_viewModel.CanCreateSubscription, Is.True);
            Assert.That(_viewModel.SubscriptionStatusText, Is.EqualTo("Expired"));
        });
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
        _mockMusicService.Setup(m => m.VerifyGooglePlayPurchaseAsync("test-token", "order-123"))
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
