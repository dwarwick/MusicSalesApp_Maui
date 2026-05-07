using System.Globalization;
using Moq;
using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Tests.Services;

[TestFixture]
public class AdminMessageCoordinatorTests
{
    private Mock<IAdminMessageApiService> _mockAdminMessageApiService = default!;
    private Mock<IAlertService> _mockAlertService = default!;
    private Mock<IAuthService> _mockAuthService = default!;
    private Mock<ISignalRService> _mockSignalRService = default!;
    private Mock<Microsoft.Extensions.Logging.ILogger<AdminMessageCoordinator>> _mockLogger = default!;
    private AdminMessageCoordinator _coordinator = default!;

    [SetUp]
    public void SetUp()
    {
        _mockAdminMessageApiService = new Mock<IAdminMessageApiService>();
        _mockAlertService = new Mock<IAlertService>();
        _mockAuthService = new Mock<IAuthService>();
        _mockSignalRService = new Mock<ISignalRService>();
        _mockLogger = new Mock<Microsoft.Extensions.Logging.ILogger<AdminMessageCoordinator>>();

        _mockAuthService.SetupGet(x => x.IsLoggedIn).Returns(true);
        _mockAuthService.SetupGet(x => x.UserId).Returns(42);
        _mockSignalRService.Setup(x => x.SyncAdminMessageConnectionAsync()).Returns(Task.CompletedTask);
        _mockAlertService.Setup(x => x.DisplayAlertAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        _mockAdminMessageApiService.Setup(x => x.AcknowledgeMessageAsync(It.IsAny<int>())).ReturnsAsync(true);

        _coordinator = new AdminMessageCoordinator(
            _mockAdminMessageApiService.Object,
            _mockAlertService.Object,
            _mockAuthService.Object,
            _mockSignalRService.Object,
            _mockLogger.Object);
    }

    [Test]
    public async Task InitializeAsync_SyncsAdminSignalRConnection()
    {
        await _coordinator.InitializeAsync();

        _mockSignalRService.Verify(x => x.SyncAdminMessageConnectionAsync(), Times.Once);
    }

    [Test]
    public async Task ProcessPendingMessagesAsync_ShowsLocalizedDateAndAcknowledgesEachMessage()
    {
        var createdUtc = new DateTime(2026, 5, 3, 23, 0, 0, DateTimeKind.Utc);
        _mockAdminMessageApiService.Setup(x => x.GetPendingDialogMessagesAsync())
            .ReturnsAsync(new List<PendingAdminMessageDto>
            {
                new() { MessageId = 10, Subject = "Testing subject", MessageText = "Important update", CreatedAtUtc = createdUtc }
            });

        await _coordinator.InitializeAsync();

        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("en-US");
            CultureInfo.CurrentUICulture = new CultureInfo("en-US");

            await _coordinator.ProcessPendingMessagesAsync();
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }

        _mockAlertService.Verify(x => x.DisplayAlertAsync(
            "Testing subject",
            It.Is<string>(body => body.Contains("Message from StreamTunes") && body.Contains("Created: 05/03/2026") && body.Contains("Important update")),
            "Acknowledge"), Times.Once);
        _mockAdminMessageApiService.Verify(x => x.AcknowledgeMessageAsync(10), Times.Once);
    }

    [Test]
    public async Task ProcessPendingMessagesAsync_DoesNothing_WhenUserIsLoggedOut()
    {
        _mockAuthService.SetupGet(x => x.IsLoggedIn).Returns(false);
        _mockAuthService.SetupGet(x => x.UserId).Returns((int?)null);

        await _coordinator.InitializeAsync();
        await _coordinator.ProcessPendingMessagesAsync();

        _mockAdminMessageApiService.Verify(x => x.GetPendingDialogMessagesAsync(), Times.Never);
        _mockAlertService.Verify(x => x.DisplayAlertAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task InitializeAsync_WhenSignalRAdminMessageUpdateRaised_ProcessesPendingMessages()
    {
        var shown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _mockAdminMessageApiService.Setup(x => x.GetPendingDialogMessagesAsync())
            .ReturnsAsync(new List<PendingAdminMessageDto>
            {
                new()
                {
                    MessageId = 20,
                    Subject = "Live subject",
                    MessageText = "Opened app should receive this via SignalR.",
                    CreatedAtUtc = new DateTime(2026, 5, 6, 12, 0, 0, DateTimeKind.Utc)
                }
            });

        _mockAlertService.Setup(x => x.DisplayAlertAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback(() => shown.TrySetResult())
            .Returns(Task.CompletedTask);

        await _coordinator.InitializeAsync();

        _mockSignalRService.Raise(x => x.OnAdminMessagesUpdated += null);
        await shown.Task.WaitAsync(TimeSpan.FromSeconds(1));

        _mockAdminMessageApiService.Verify(x => x.GetPendingDialogMessagesAsync(), Times.Once);
        _mockAdminMessageApiService.Verify(x => x.AcknowledgeMessageAsync(20), Times.Once);
    }
}