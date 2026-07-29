using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.Tests.Services;

/// <summary>
/// The free-space probe that guards every cache download.
///
/// Regression cover for a silent, total failure: the probe used to measure
/// <c>Path.GetPathRoot(cacheDirectory)</c>, which on any Unix-like OS is "/" - a different filesystem
/// from the one the cache lives on, and on Android a full read-only system partition reporting zero
/// bytes free. Every artwork download was rejected against the 1 GB reserve while the data partition
/// had 100 GB spare.
/// </summary>
[TestFixture]
public class CacheStorageProbeTests
{
    private string _directory = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "cache-probe-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Test]
    public void GetAvailableFreeSpaceBytes_ForARealDirectory_ReportsRealFreeSpace()
    {
        // The invariant that broke: a directory on a filesystem with space available must not report
        // zero, or the reserve check rejects every download forever.
        Assert.That(CacheStorageProbe.GetAvailableFreeSpaceBytes(_directory), Is.GreaterThan(0));
    }

    [Test]
    public void GetAvailableFreeSpaceBytes_ForANestedDirectory_MatchesItsParent()
    {
        // Both live on the same filesystem, so the probe must not be sensitive to how deep the path is.
        var nested = Path.Combine(_directory, "a", "b", "c");
        Directory.CreateDirectory(nested);

        var parent = CacheStorageProbe.GetAvailableFreeSpaceBytes(_directory);
        var child = CacheStorageProbe.GetAvailableFreeSpaceBytes(nested);

        // Free space can move slightly between the two calls, so compare within a wide tolerance.
        Assert.That((double)child, Is.EqualTo((double)parent).Within(0.05).Percent);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void GetAvailableFreeSpaceBytes_WithNoPath_DoesNotBlockCaching(string? path)
    {
        // "Unknown" has to mean "don't block". Returning 0 here would reproduce the original bug.
        Assert.That(CacheStorageProbe.GetAvailableFreeSpaceBytes(path), Is.EqualTo(long.MaxValue));
    }

    [Test]
    public void GetAvailableFreeSpaceBytes_WithAnUnusablePath_DoesNotThrowOrBlockCaching()
    {
        var nonsense = Path.Combine(_directory, "does", "not", "exist", new string('x', 200));

        Assert.That(
            CacheStorageProbe.GetAvailableFreeSpaceBytes(nonsense),
            Is.GreaterThan(0),
            "an unmeasurable path must not be reported as a full disk");
    }
}
