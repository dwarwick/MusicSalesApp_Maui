using Moq;
using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.Tests.Services;

[TestFixture]
public class OfflineCacheSettingsServiceTests
{
    private Mock<IAppPreferenceStore> _preferenceStore = null!;
    private OfflineCacheSettingsService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _preferenceStore = new Mock<IAppPreferenceStore>();
        _service = new OfflineCacheSettingsService(_preferenceStore.Object);
    }

    [Test]
    public void GetOfflineCacheLimitMb_WhenUnset_ReturnsOneGigabyteDefault()
    {
        _preferenceStore
            .Setup(p => p.GetInt(MobilePreferenceKeys.OfflineCacheLimitMb, 1024))
            .Returns(1024);

        Assert.That(_service.GetOfflineCacheLimitMb(), Is.EqualTo(1024));
    }

    [Test]
    public void SetOfflineCacheLimitMb_ClampsToAllowedRange()
    {
        _service.SetOfflineCacheLimitMb(50);
        _service.SetOfflineCacheLimitMb(7000);

        _preferenceStore.Verify(
            p => p.SetInt(MobilePreferenceKeys.OfflineCacheLimitMb, 100),
            Times.Once);
        _preferenceStore.Verify(
            p => p.SetInt(MobilePreferenceKeys.OfflineCacheLimitMb, 5120),
            Times.Once);
    }

    [Test]
    public void DeviceFreeSpaceReserve_IsOneGigabyte()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_service.DeviceFreeSpaceReserveMb, Is.EqualTo(1024));
            Assert.That(_service.GetDeviceFreeSpaceReserveBytes(), Is.EqualTo(1024L * 1024L * 1024L));
        });
    }
}
