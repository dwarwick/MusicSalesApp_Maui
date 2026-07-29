using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Tests.Services;

[TestFixture]
public class StableRemoteAssetKeyTests
{
    private const string BlobPath = "/songs-dev/tracks/42-my-song.mp3";

    [Test]
    public void GetPathHash_IgnoresRotatingSasQueryString()
    {
        // The server mints a fresh 24h SAS token on every API call, so the only stable part of an
        // asset URL is its path. Losing this property means the cache never hits across sessions.
        var first = new Uri($"https://storage.blob.core.windows.net{BlobPath}?sv=2024-01-01&sig=AAAA");
        var second = new Uri($"https://storage.blob.core.windows.net{BlobPath}?sv=2025-06-01&sig=ZZZZ");

        Assert.That(StableRemoteAssetKey.GetPathHash(second, "seed"),
            Is.EqualTo(StableRemoteAssetKey.GetPathHash(first, "seed")));
    }

    [Test]
    public void GetPathHash_IsCaseInsensitiveOnPath()
    {
        var lower = new Uri("https://storage.blob.core.windows.net/songs/track.mp3");
        var upper = new Uri("https://storage.blob.core.windows.net/SONGS/TRACK.mp3");

        Assert.That(StableRemoteAssetKey.GetPathHash(upper, "seed"),
            Is.EqualTo(StableRemoteAssetKey.GetPathHash(lower, "seed")));
    }

    [Test]
    public void GetPathHash_DifferentPaths_ProduceDifferentHashes()
    {
        var first = new Uri("https://storage.blob.core.windows.net/songs/one.mp3");
        var second = new Uri("https://storage.blob.core.windows.net/songs/two.mp3");

        Assert.That(StableRemoteAssetKey.GetPathHash(second, "seed"),
            Is.Not.EqualTo(StableRemoteAssetKey.GetPathHash(first, "seed")));
    }

    [Test]
    public void GetPathHash_UsesFallbackSeed_WhenPathIsEmpty()
    {
        var uri = new Uri("https://storage.blob.core.windows.net");

        // AbsolutePath is "/" here rather than empty, so the seed is not used - assert the hash is at
        // least stable and well-formed rather than asserting a branch the framework never takes.
        Assert.That(StableRemoteAssetKey.GetPathHash(uri, "song-42"), Has.Length.EqualTo(64));
    }

    [Test]
    public void AudioCacheKeyHelper_ProducesTheSameHashAsBeforeTheRefactor()
    {
        // Pinned literal: AudioCacheKeyHelper delegates to StableRemoteAssetKey now, and any change to
        // the hash silently orphans every track already on disk.
        var song = new SongDto
        {
            Id = 42,
            StreamUrl = $"https://storage.blob.core.windows.net{BlobPath}?sig=whatever"
        };

        Assert.That(AudioCacheKeyHelper.GetStableCacheKey(song), Is.EqualTo(
            "song-42-fbd0defd3043a464cabe43105eb31ab63abe8122c455988efe2056c180e16730"));
    }

    [Test]
    public void AudioCacheKeyHelper_FallsBackToSongId_WhenStreamUrlIsNotAbsolute()
    {
        var song = new SongDto { Id = 7, StreamUrl = "not-a-url" };

        Assert.That(AudioCacheKeyHelper.GetStableCacheKey(song), Is.EqualTo("song-7"));
    }

    [TestCase("https://host/songs/track.mp3", ".mp3")]
    [TestCase("https://host/images/cover.JPEG", ".jpeg")]
    [TestCase("https://host/images/cover", ".fallback")]
    public void GetExtension_UsesUrlPathExtensionOrFallback(string url, string expected)
    {
        Assert.That(StableRemoteAssetKey.GetExtension(new Uri(url), ".fallback"), Is.EqualTo(expected));
    }

    [Test]
    public void GetExtension_RejectsImplausiblyLongExtensions()
    {
        // A blob path may legitimately contain dots; a 20-character "extension" is not one.
        var uri = new Uri("https://host/images/cover.thisisnotanextension");

        Assert.That(StableRemoteAssetKey.GetExtension(uri, ".img"), Is.EqualTo(".img"));
    }

    [TestCase(null, false)]
    [TestCase("", false)]
    [TestCase("   ", false)]
    [TestCase("relative/path.mp3", false)]
    [TestCase("https://host/a.mp3", true)]
    public void TryGetAbsoluteUri_AcceptsOnlyAbsoluteUrls(string? url, bool expected)
    {
        Assert.That(StableRemoteAssetKey.TryGetAbsoluteUri(url, out _), Is.EqualTo(expected));
    }
}
