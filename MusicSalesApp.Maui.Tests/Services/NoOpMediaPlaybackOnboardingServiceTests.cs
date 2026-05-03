using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.Tests.Services;

[TestFixture]
public class NoOpMediaPlaybackOnboardingServiceTests
{
    [Test]
    public async Task EnsureBackgroundPlaybackExplainedAsync_Completes()
    {
        var service = new NoOpMediaPlaybackOnboardingService();

        await service.EnsureBackgroundPlaybackExplainedAsync();

        Assert.Pass();
    }

    [Test]
    public async Task EnsureMicrophonePermissionAsync_ReturnsTrue()
    {
        var service = new NoOpMediaPlaybackOnboardingService();

        var granted = await service.EnsureMicrophonePermissionAsync();

        Assert.That(granted, Is.True);
    }
}