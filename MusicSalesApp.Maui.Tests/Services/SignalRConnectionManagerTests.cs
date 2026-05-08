using Microsoft.Extensions.Logging;
using Microsoft.Maui.Networking;
using Moq;
using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.Tests.Services;

[TestFixture]
public class SignalRConnectionManagerTests
{
    private Mock<ISignalRService> _mockSignalRService = default!;
    private Mock<IConnectivity> _mockConnectivity = default!;
    private Mock<ILogger<SignalRConnectionManager>> _mockLogger = default!;

    [SetUp]
    public void Setup()
    {
        _mockSignalRService = new Mock<ISignalRService>();
        _mockSignalRService.Setup(service => service.StartAsync()).Returns(Task.CompletedTask);

        _mockConnectivity = new Mock<IConnectivity>();
        _mockConnectivity.SetupGet(connectivity => connectivity.NetworkAccess).Returns(NetworkAccess.Internet);
        _mockConnectivity.SetupGet(connectivity => connectivity.ConnectionProfiles).Returns(Array.Empty<ConnectionProfile>());

        _mockLogger = new Mock<ILogger<SignalRConnectionManager>>();
    }

    [Test]
    public async Task InitializeAsync_StartsSignalR()
    {
        var manager = CreateManager();

        await manager.InitializeAsync();

        _mockSignalRService.Verify(service => service.StartAsync(), Times.Once);
    }

    [Test]
    public async Task HandleAppResumeAsync_StartsSignalR()
    {
        var manager = CreateManager();

        await manager.HandleAppResumeAsync();

        _mockSignalRService.Verify(service => service.StartAsync(), Times.Once);
    }

    [Test]
    public async Task ConnectivityChanged_WithInternet_StartsSignalR()
    {
        var startObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _mockSignalRService
            .Setup(service => service.StartAsync())
            .Returns(() =>
            {
                startObserved.TrySetResult();
                return Task.CompletedTask;
            });

        var manager = CreateManager();

        _mockConnectivity.Raise(
            connectivity => connectivity.ConnectivityChanged += null,
            new ConnectivityChangedEventArgs(NetworkAccess.Internet, Array.Empty<ConnectionProfile>()));

        await startObserved.Task.WaitAsync(TimeSpan.FromSeconds(1));

        _mockSignalRService.Verify(service => service.StartAsync(), Times.Once);
        manager.Dispose();
    }

    [Test]
    public void ConnectivityChanged_WithoutInternet_DoesNotStartSignalR()
    {
        var manager = CreateManager();

        _mockConnectivity.Raise(
            connectivity => connectivity.ConnectivityChanged += null,
            new ConnectivityChangedEventArgs(NetworkAccess.None, Array.Empty<ConnectionProfile>()));

        _mockSignalRService.Verify(service => service.StartAsync(), Times.Never);
        manager.Dispose();
    }

    private SignalRConnectionManager CreateManager() => new(
        _mockSignalRService.Object,
        _mockConnectivity.Object,
        _mockLogger.Object);
}