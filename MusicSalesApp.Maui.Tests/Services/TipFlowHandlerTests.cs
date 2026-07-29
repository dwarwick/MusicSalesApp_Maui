using Microsoft.Extensions.Logging;
using Moq;
using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Tests.Services;

[TestFixture]
public class TipFlowHandlerTests
{
    private Mock<IAuthService> _auth = null!;
    private Mock<ITipApiService> _tipApi = null!;
    private Mock<ITipAmountPicker> _tipAmountPicker = null!;
    private Mock<IAlertService> _alerts = null!;
    private Mock<IBrowserService> _browser = null!;
    private Mock<ILogger<TipFlowHandler>> _logger = null!;
    private TestNetworkStatusService _networkStatus = null!;
    private TipFlowHandler _handler = null!;

    [SetUp]
    public void Setup()
    {
        _auth = new Mock<IAuthService>();
        _tipApi = new Mock<ITipApiService>();
        _tipAmountPicker = new Mock<ITipAmountPicker>();
        _alerts = new Mock<IAlertService>();
        _browser = new Mock<IBrowserService>();
        _logger = new Mock<ILogger<TipFlowHandler>>();

        _auth.SetupGet(a => a.IsLoggedIn).Returns(true);
        _auth.SetupGet(a => a.IsValidatedUser).Returns(true);
        _auth.SetupGet(a => a.UserId).Returns(22);
        _auth.SetupGet(a => a.CreatorId).Returns((int?)null);

        _networkStatus = new TestNetworkStatusService();

        _handler = new TipFlowHandler(_auth.Object, _tipApi.Object, _tipAmountPicker.Object, _alerts.Object, _browser.Object, _logger.Object, _networkStatus);
    }

    [Test]
    public void CanShowTipButton_WhileOffline_ReturnsFalseEvenForAValidatedUser()
    {
        // Tipping opens a PayPal approval flow in the browser, so it cannot work offline.
        Assert.That(_handler.CanShowTipButton(7, 99), Is.True);

        _networkStatus.SetOffline(true);

        Assert.That(_handler.CanShowTipButton(7, 99), Is.False);
    }

    [Test]
    public void CanShowTipButton_AfterReconnecting_ReturnsTrueAgain()
    {
        _networkStatus.SetOffline(true);

        _networkStatus.SetOffline(false);

        Assert.That(_handler.CanShowTipButton(7, 99), Is.True);
    }

    [Test]
    public void CanShowTipButton_OnAConstrainedConnection_StaysVisible()
    {
        // IsOffline is true here, but the PayPal flow still works - hiding the button would take away a
        // working feature. The gate uses HasNoNetworkAccess for exactly this reason.
        _networkStatus.SetConstrained();

        Assert.That(_handler.CanShowTipButton(7, 99), Is.True);
    }

    [Test]
    public void CanShowTipButton_WithNoNetworkStatusService_KeepsThePreExistingBehaviour()
    {
        var handlerWithoutNetworkStatus = new TipFlowHandler(
            _auth.Object, _tipApi.Object, _tipAmountPicker.Object, _alerts.Object, _browser.Object, _logger.Object);

        Assert.That(handlerWithoutNetworkStatus.CanShowTipButton(7, 99), Is.True);
    }

    [Test]
    public void CanShowTipButton_WhenUserIsNotValidated_ReturnsFalse()
    {
        _auth.SetupGet(a => a.IsValidatedUser).Returns(false);

        var canShow = _handler.CanShowTipButton(7, 99);

        Assert.That(canShow, Is.False);
    }

    [Test]
    public void CanShowTipButton_WhenSongBelongsToCurrentCreator_ReturnsFalse()
    {
        _auth.SetupGet(a => a.CreatorId).Returns(7);

        var canShow = _handler.CanShowTipButton(7, 99);

        Assert.That(canShow, Is.False);
    }

    [Test]
    public async Task ShowAsync_WhenCreateOrderRequiresApproval_OpensPayPalExternally()
    {
        _tipAmountPicker.Setup(p => p.PickAmountAsync("Test Song")).ReturnsAsync(5.00m);
        _tipApi.Setup(t => t.CreateOrderAsync(7, 10, 5.00m, null))
            .ReturnsAsync(new TipOperationResponseDto
            {
                Success = true,
                ResultKind = TipResultKinds.RequiresApproval,
                ApprovalUrl = "https://paypal.test/approve"
            });

        await _handler.ShowAsync(10, "Test Song", 7, 88);

        _browser.Verify(b => b.OpenExternalAsync("https://paypal.test/approve"), Times.Once);
        _alerts.Verify(a => a.DisplayAlertAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task ShowAsync_WhenServerBlocksTip_ShowsReturnedMessage()
    {
        _tipAmountPicker.Setup(p => p.PickAmountAsync("Test Song")).ReturnsAsync(1.00m);
        _tipApi.Setup(t => t.CreateOrderAsync(7, 10, 1.00m, null))
            .ReturnsAsync(new TipOperationResponseDto
            {
                Success = false,
                ResultKind = TipResultKinds.ValidationBlocked,
                Message = "Your account must be at least 7 days old before sending tips."
            });

        await _handler.ShowAsync(10, "Test Song", 7, 88);

        _alerts.Verify(a => a.DisplayAlertAsync(
            "Can't send tip",
            "Your account must be at least 7 days old before sending tips.",
            "OK"), Times.Once);
        _browser.Verify(b => b.OpenExternalAsync(It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task ShowAsync_WhenPickerIsCancelled_DoesNotCallTipApi()
    {
        _tipAmountPicker.Setup(p => p.PickAmountAsync("Test Song")).ReturnsAsync((decimal?)null);

        await _handler.ShowAsync(10, "Test Song", 7, 88);

        _tipApi.Verify(t => t.CreateOrderAsync(It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<decimal>(), It.IsAny<string?>()), Times.Never);
        _browser.Verify(b => b.OpenExternalAsync(It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task HandleAppLinkAsync_WhenApproved_CapturesAndShowsSuccessAlert()
    {
        _tipApi.Setup(t => t.CaptureAsync("ORDER-1"))
            .ReturnsAsync(new TipOperationResponseDto
            {
                Success = true,
                ResultKind = TipResultKinds.Succeeded,
                Message = "Your $5.00 tip was sent successfully!"
            });

        var handled = await _handler.HandleAppLinkAsync(new Uri("streamtunes://tip?tip_status=approved&token=ORDER-1"));

        Assert.That(handled, Is.True);
        _tipApi.Verify(t => t.CaptureAsync("ORDER-1"), Times.Once);
        _alerts.Verify(a => a.DisplayAlertAsync("Tip Sent!", "Your $5.00 tip was sent successfully!", "OK"), Times.Once);
    }

    [Test]
    public async Task HandleAppLinkAsync_WhenCancelled_CancelsPendingTipAndShowsAlert()
    {
        _tipApi.Setup(t => t.CancelAsync("ORDER-2"))
            .ReturnsAsync(new TipOperationResponseDto
            {
                Success = true,
                ResultKind = TipResultKinds.Cancelled,
                Message = "Tip payment was cancelled."
            });

        var handled = await _handler.HandleAppLinkAsync(new Uri("streamtunes://tip?tip_status=cancelled&token=ORDER-2"));

        Assert.That(handled, Is.True);
        _tipApi.Verify(t => t.CancelAsync("ORDER-2"), Times.Once);
        _alerts.Verify(a => a.DisplayAlertAsync("Tip Cancelled", "Tip payment was cancelled.", "OK"), Times.Once);
    }
}