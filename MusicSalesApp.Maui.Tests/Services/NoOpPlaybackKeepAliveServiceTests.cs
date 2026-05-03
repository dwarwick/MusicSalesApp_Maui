using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.Tests.Services;

[TestFixture]
public class NoOpPlaybackKeepAliveServiceTests
{
    [Test]
    public void SetPlaybackActive_DoesNotThrow()
    {
        var service = new NoOpPlaybackKeepAliveService();

        Assert.That(() => service.SetPlaybackActive(true), Throws.Nothing);
        Assert.That(() => service.SetPlaybackActive(false), Throws.Nothing);
    }
}