using Microsoft.Extensions.Logging;
using Moq;
using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.Tests.Services;

[TestFixture]
public class AppActivationCoordinatorTests
{
    private Mock<IMusicService> _mockMusicService = default!;
    private Mock<ISignalRConnectionManager> _mockSignalRConnectionManager = default!;
    private Mock<ILogger<AppActivationCoordinator>> _mockLogger = default!;

    [SetUp]
    public void Setup()
    {
        _mockMusicService = new Mock<IMusicService>();
        _mockMusicService
            .Setup(service => service.FlushPendingStreamRecordsAsync())
            .Returns(Task.CompletedTask);

        _mockSignalRConnectionManager = new Mock<ISignalRConnectionManager>();
        _mockSignalRConnectionManager
            .Setup(service => service.HandleAppResumeAsync())
            .Returns(Task.CompletedTask);

        _mockLogger = new Mock<ILogger<AppActivationCoordinator>>();
    }

    [Test]
    public async Task HandleActivationAsync_FlushesPendingStreams_AndRestartsSignalR()
    {
        var coordinator = CreateCoordinator();

        await coordinator.HandleActivationAsync();

        _mockMusicService.Verify(service => service.FlushPendingStreamRecordsAsync(), Times.Once);
        _mockSignalRConnectionManager.Verify(service => service.HandleAppResumeAsync(), Times.Once);
    }

    [Test]
    public async Task HandleActivationAsync_WhenFlushingFails_StillRestartsSignalR()
    {
        _mockMusicService
            .Setup(service => service.FlushPendingStreamRecordsAsync())
            .ThrowsAsync(new InvalidOperationException("flush failed"));

        var coordinator = CreateCoordinator();

        await coordinator.HandleActivationAsync();

        _mockSignalRConnectionManager.Verify(service => service.HandleAppResumeAsync(), Times.Once);
    }

    private AppActivationCoordinator CreateCoordinator() => new(
        _mockMusicService.Object,
        _mockSignalRConnectionManager.Object,
        _mockLogger.Object);
}