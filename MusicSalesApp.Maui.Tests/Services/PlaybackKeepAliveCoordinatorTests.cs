using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.Tests.Services;

[TestFixture]
public class PlaybackKeepAliveCoordinatorTests
{
    [Test]
    public void Constructor_WithNullActivate_Throws()
    {
        Assert.That(() => new PlaybackKeepAliveCoordinator(null!, () => { }), Throws.ArgumentNullException);
    }

    [Test]
    public void Constructor_WithNullDeactivate_Throws()
    {
        Assert.That(() => new PlaybackKeepAliveCoordinator(() => { }, null!), Throws.ArgumentNullException);
    }

    [Test]
    public void SetPlaybackActive_WhenActivating_CallsActivateOnce()
    {
        var activateCalls = 0;
        var deactivateCalls = 0;
        var coordinator = new PlaybackKeepAliveCoordinator(() => activateCalls++, () => deactivateCalls++);

        coordinator.SetPlaybackActive(true);
        coordinator.SetPlaybackActive(true);

        Assert.Multiple(() =>
        {
            Assert.That(activateCalls, Is.EqualTo(1));
            Assert.That(deactivateCalls, Is.Zero);
        });
    }

    [Test]
    public void SetPlaybackActive_WhenDeactivating_CallsDeactivateOnce()
    {
        var activateCalls = 0;
        var deactivateCalls = 0;
        var coordinator = new PlaybackKeepAliveCoordinator(() => activateCalls++, () => deactivateCalls++);

        coordinator.SetPlaybackActive(true);
        coordinator.SetPlaybackActive(false);
        coordinator.SetPlaybackActive(false);

        Assert.Multiple(() =>
        {
            Assert.That(activateCalls, Is.EqualTo(1));
            Assert.That(deactivateCalls, Is.EqualTo(1));
        });
    }

    [Test]
    public void Dispose_WhenActive_CallsDeactivateOnce()
    {
        var activateCalls = 0;
        var deactivateCalls = 0;
        var coordinator = new PlaybackKeepAliveCoordinator(() => activateCalls++, () => deactivateCalls++);

        coordinator.SetPlaybackActive(true);
        coordinator.Dispose();
        coordinator.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(activateCalls, Is.EqualTo(1));
            Assert.That(deactivateCalls, Is.EqualTo(1));
        });
    }

    [Test]
    public void SetPlaybackActive_AfterDispose_DoesNothing()
    {
        var activateCalls = 0;
        var deactivateCalls = 0;
        var coordinator = new PlaybackKeepAliveCoordinator(() => activateCalls++, () => deactivateCalls++);

        coordinator.Dispose();
        coordinator.SetPlaybackActive(true);
        coordinator.SetPlaybackActive(false);

        Assert.Multiple(() =>
        {
            Assert.That(activateCalls, Is.Zero);
            Assert.That(deactivateCalls, Is.Zero);
        });
    }
}