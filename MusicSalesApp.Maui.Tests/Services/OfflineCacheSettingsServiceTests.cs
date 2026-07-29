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
    public void GetImageCacheLimitBytes_AtTheDefaultCacheLimit_IsATwentiethOfIt()
    {
        GivenConfiguredLimitMb(1024);

        Assert.That(_service.GetImageCacheLimitBytes(), Is.EqualTo(1024L * 1024 * 1024 / 20));
    }

    [Test]
    public void GetImageCacheLimitBytes_AtTheSmallestCacheLimit_IsClampedToTheFloor()
    {
        // 100 MB / 20 = 5 MB, below the 8 MB floor - artwork still gets a workable budget.
        GivenConfiguredLimitMb(100);

        Assert.That(_service.GetImageCacheLimitBytes(), Is.EqualTo(8L * 1024 * 1024));
    }

    [Test]
    public void GetImageCacheLimitBytes_AtTheLargestCacheLimit_IsClampedToTheCeiling()
    {
        // 5 GB / 20 = 256 MB, above the 64 MB ceiling - artwork never crowds out audio.
        GivenConfiguredLimitMb(5120);

        Assert.That(_service.GetImageCacheLimitBytes(), Is.EqualTo(64L * 1024 * 1024));
    }

    [Test]
    public void GetImageCacheLimitBytes_IsAlwaysWellUnderTheAudioBudget()
    {
        foreach (var limitMb in new[] { 100, 512, 1024, 2048, 5120 })
        {
            GivenConfiguredLimitMb(limitMb);
            Assert.That(
                _service.GetImageCacheLimitBytes(),
                Is.LessThan(_service.GetOfflineCacheLimitBytes()),
                $"image budget must stay below the total cache limit at {limitMb} MB");
        }
    }

    [Test]
    public void GetAudioCacheLimitBytes_IsTheConfiguredLimitLessTheArtworkCarveOut()
    {
        // Reported cache usage sums audio and artwork, so the two budgets have to add up to the
        // configured limit - otherwise the settings screen can show usage above its own limit.
        foreach (var limitMb in new[] { 100, 512, 1024, 2048, 5120 })
        {
            GivenConfiguredLimitMb(limitMb);
            Assert.That(
                _service.GetAudioCacheLimitBytes() + _service.GetImageCacheLimitBytes(),
                Is.EqualTo(_service.GetOfflineCacheLimitBytes()),
                $"budgets must sum to the configured limit at {limitMb} MB");
        }
    }

    [Test]
    public void GetAudioCacheLimitBytes_KeepsTheOverwhelmingMajorityOfTheBudget()
    {
        GivenConfiguredLimitMb(1024);

        Assert.That(
            _service.GetAudioCacheLimitBytes(),
            Is.GreaterThan(_service.GetOfflineCacheLimitBytes() * 9 / 10));
    }

    // --- The static budget math, which the Android cache evictor is sized from ---

    [Test]
    public void ComputeAudioCacheLimitBytes_MatchesTheInstanceApi()
    {
        // The Android Media3 cache builds its evictor from a static context and cannot reach DI, so it
        // calls the static overload. If the two ever disagreed, the cache would trim itself to a
        // different ceiling than the one gating downloads - and could sit permanently just above the
        // download ceiling, which silently stops all downloading.
        foreach (var limitMb in new[] { 100, 512, 1024, 2048, 5120 })
        {
            GivenConfiguredLimitMb(limitMb);
            Assert.That(
                OfflineCacheSettingsService.ComputeAudioCacheLimitBytes(limitMb),
                Is.EqualTo(_service.GetAudioCacheLimitBytes()),
                $"static and instance audio budgets must agree at {limitMb} MB");
        }
    }

    [Test]
    public void ComputeAudioCacheLimitBytes_ClampsAnOutOfRangeLimitTheSameWayTheServiceDoes()
    {
        // The evictor reads the raw stored preference, which predates any clamping.
        GivenConfiguredLimitMb(50_000);

        Assert.That(
            OfflineCacheSettingsService.ComputeAudioCacheLimitBytes(50_000),
            Is.EqualTo(_service.GetAudioCacheLimitBytes()));
    }

    [Test]
    public void ComputeAudioCacheLimitBytes_IsAlwaysPositive()
    {
        // A zero or negative evictor size would evict everything the moment anything was written.
        foreach (var limitMb in new[] { int.MinValue, 0, 1, 100, 5120, int.MaxValue })
        {
            Assert.That(
                OfflineCacheSettingsService.ComputeAudioCacheLimitBytes(limitMb),
                Is.GreaterThan(0),
                $"audio budget must stay positive at {limitMb} MB");
        }
    }

    private void GivenConfiguredLimitMb(int limitMb)
        => _preferenceStore
            .Setup(p => p.GetInt(MobilePreferenceKeys.OfflineCacheLimitMb, It.IsAny<int>()))
            .Returns(limitMb);

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
