using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.Tests.Services;

[TestFixture]
public class NoAudioVisualizerServiceTests
{
    [Test]
    public async Task EnsureInitializedAsync_KeepsVisualizationUnavailable()
    {
        var service = new NoAudioVisualizerService();

        await service.EnsureInitializedAsync();

        Assert.That(service.IsVisualizationAvailable, Is.False);
        Assert.That(service.Levels, Is.Empty);
    }

    [Test]
    public void Suspend_KeepsVisualizationUnavailable()
    {
        var service = new NoAudioVisualizerService();

        service.Suspend();

        Assert.That(service.IsVisualizationAvailable, Is.False);
        Assert.That(service.Levels, Is.Empty);
    }
}