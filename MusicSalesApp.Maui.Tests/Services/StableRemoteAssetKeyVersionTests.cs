using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.Tests.Services;

[TestFixture]
public class StableRemoteAssetKeyVersionTests
{
    private static Uri Blob(string path = "/images/cover.jpg", string sas = "sig=aaa")
        => new($"https://storage.test{path}?{sas}");

    [Test]
    public void VersionZeroKeysExactlyAsTheUnversionedCacheAlwaysDid()
    {
        // Audio caching shares this method and has no version of its own. If zero changed the hash,
        // every downloaded track would be orphaned and a user's whole offline library would silently
        // re-download.
        var uri = Blob();

        Assert.That(
            StableRemoteAssetKey.GetPathHash(uri, uri.AbsoluteUri, 0),
            Is.EqualTo(StableRemoteAssetKey.GetPathHash(uri, uri.AbsoluteUri)));
    }

    [Test]
    public void ANegativeVersionIsTreatedAsUnversioned()
    {
        var uri = Blob();

        Assert.That(
            StableRemoteAssetKey.GetPathHash(uri, uri.AbsoluteUri, -1),
            Is.EqualTo(StableRemoteAssetKey.GetPathHash(uri, uri.AbsoluteUri)));
    }

    [Test]
    public void ADifferentVersionAtTheSamePathProducesADifferentKey()
    {
        // The whole point: cover art under the GUID scheme keeps one fixed path that a re-crop
        // overwrites in place, so the path alone cannot tell new pixels from old.
        var uri = Blob();

        Assert.That(
            StableRemoteAssetKey.GetPathHash(uri, uri.AbsoluteUri, 4),
            Is.Not.EqualTo(StableRemoteAssetKey.GetPathHash(uri, uri.AbsoluteUri, 3)));
    }

    [Test]
    public void TheSameVersionAtTheSamePathIsStable()
    {
        Assert.That(
            StableRemoteAssetKey.GetPathHash(Blob(), "seed", 3),
            Is.EqualTo(StableRemoteAssetKey.GetPathHash(Blob(), "seed", 3)));
    }

    [Test]
    public void ARotatedSasTokenStillHitsTheSameKey()
    {
        // The original reason this helper exists - the server mints a fresh SAS on every API call -
        // must survive the addition of versioning.
        Assert.That(
            StableRemoteAssetKey.GetPathHash(Blob(sas: "sig=first"), "seed", 3),
            Is.EqualTo(StableRemoteAssetKey.GetPathHash(Blob(sas: "sig=second"), "seed", 3)));
    }

    [Test]
    public void DifferentRenditionsOfTheSameImageStillKeySeparately()
    {
        var thumb = Blob("/images/cover.jpg.w320.webp");
        var hero = Blob("/images/cover.jpg.w640.webp");

        Assert.That(
            StableRemoteAssetKey.GetPathHash(thumb, "seed", 3),
            Is.Not.EqualTo(StableRemoteAssetKey.GetPathHash(hero, "seed", 3)));
    }

    [Test]
    public void ACachedImageReferenceConvertsFromABareUrlAtVersionZero()
    {
        // Keeps every call site that has no version available working unchanged.
        CachedImageReference reference = "https://storage.test/images/cover.jpg";

        Assert.Multiple(() =>
        {
            Assert.That(reference.Url, Is.EqualTo("https://storage.test/images/cover.jpg"));
            Assert.That(reference.Version, Is.Zero);
        });
    }
}
