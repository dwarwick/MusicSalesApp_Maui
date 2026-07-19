using Moq;
using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.Tests.Services;

[TestFixture]
public class AudioVisualizerLifecycleCoordinatorTests
{
    [Test]
    public void LifecycleEvents_BeforeVisualizerRegistration_DoNothing()
    {
        var coordinator = new AudioVisualizerLifecycleCoordinator();

        Assert.DoesNotThrow(() =>
        {
            coordinator.OnApplicationStopped();
            coordinator.OnApplicationResumed();
        });
    }

    [Test]
    public void RegisteredVisualizer_ReceivesStopAndResume()
    {
        var visualizer = new Mock<IAudioVisualizerService>();
        visualizer.Setup(service => service.EnsureInitializedAsync()).Returns(Task.CompletedTask);
        var coordinator = new AudioVisualizerLifecycleCoordinator();
        coordinator.Register(visualizer.Object);

        coordinator.OnApplicationStopped();
        coordinator.OnApplicationResumed();

        visualizer.Verify(service => service.Suspend(), Times.Once);
        visualizer.Verify(service => service.EnsureInitializedAsync(), Times.Once);
    }

    [Test]
    public void UnregisteredVisualizer_DoesNotReceiveLifecycleEvents()
    {
        var visualizer = new Mock<IAudioVisualizerService>();
        var coordinator = new AudioVisualizerLifecycleCoordinator();
        coordinator.Register(visualizer.Object);
        coordinator.Unregister(visualizer.Object);

        coordinator.OnApplicationStopped();
        coordinator.OnApplicationResumed();

        visualizer.Verify(service => service.Suspend(), Times.Never);
        visualizer.Verify(service => service.EnsureInitializedAsync(), Times.Never);
    }

    [Test]
    public void UnregisteringDifferentVisualizer_KeepsRegisteredVisualizerActive()
    {
        var registered = new Mock<IAudioVisualizerService>();
        registered.Setup(service => service.EnsureInitializedAsync()).Returns(Task.CompletedTask);
        var different = new Mock<IAudioVisualizerService>();
        var coordinator = new AudioVisualizerLifecycleCoordinator();
        coordinator.Register(registered.Object);

        coordinator.Unregister(different.Object);
        coordinator.OnApplicationResumed();

        registered.Verify(service => service.EnsureInitializedAsync(), Times.Once);
    }
}
