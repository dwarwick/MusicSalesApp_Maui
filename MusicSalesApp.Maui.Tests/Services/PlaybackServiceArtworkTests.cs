using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Tests.Services;

/// <summary>
/// Artwork URI resolution for the lock screen / notification. Kept separate from the main
/// PlaybackServiceTests fixture because it needs only a minimal service instance.
/// </summary>
[TestFixture]
public class PlaybackServiceArtworkTests
{
    private const string RemoteAlbumArt = "https://storage.test/images/1.jpg?sig=aaa";
    private const string RemotePersonaImage = "https://storage.test/personas/1.jpg?sig=bbb";
    private const string RemoteAlbumArtUrlShared = RemoteAlbumArt;
    private const string RemoteAlbumArtThumb = "https://storage.test/images/1_320.jpg?sig=ccc";
    private const string RemoteAlbumArtHero = "https://storage.test/images/1_640.jpg?sig=ddd";
    private const string RemotePersonaImageThumb = "https://storage.test/personas/1_320.jpg?sig=eee";
    private const string RemotePersonaImageHero = "https://storage.test/personas/1_640.jpg?sig=fff";

    private Mock<IImageCacheService> _imageCache = null!;
    private TestNetworkStatusService _networkStatus = null!;
    private PlaybackService _service = null!;
    private string _cachedImagePath = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _imageCache = new Mock<IImageCacheService>();
        _networkStatus = new TestNetworkStatusService();
        // A real rooted path, so new Uri(...) produces a valid file:// URI on this platform.
        _cachedImagePath = Path.Combine(Path.GetTempPath(), "image-cache", "album.jpg");

        _service = new PlaybackService(
            new Mock<IAuthService>().Object,
            new Mock<IMusicService>().Object,
            new Mock<IPlatformPlaybackRuntime>().Object,
            new Mock<IAudioCacheService>().Object,
            new Mock<IQueuePreparationService>().Object,
            new Mock<IPlaybackKeepAliveService>().Object,
            NullLogger<PlaybackService>.Instance,
            anonymousFeaturedStreamStore: null,
            networkStatusService: _networkStatus,
            imageCacheService: _imageCache.Object);
    }

    private static SongDto CreateSong() => new()
    {
        Id = 1,
        AlbumArtUrl = RemoteAlbumArt,
        PersonaImageUrl = RemotePersonaImage
    };

    [Test]
    public void ResolveAlbumImageUri_PrefersTheCachedFileUri()
    {
        // Media3's default bitmap loader resolves file:// through FileDataSource, so notification
        // artwork renders with no network involved.
        _imageCache.Setup(c => c.TryGetCachedImagePath(RemoteAlbumArt)).Returns(_cachedImagePath);

        var resolved = _service.ResolveAlbumImageUri(CreateSong());

        Assert.That(resolved, Does.StartWith("file://"));
        Assert.That(resolved, Is.EqualTo(new Uri(_cachedImagePath).AbsoluteUri));
    }

    [Test]
    public void ResolveAlbumImageUri_FallsBackToTheCachedPersonaImage()
    {
        _imageCache.Setup(c => c.TryGetCachedImagePath(RemoteAlbumArt)).Returns((string?)null);
        _imageCache.Setup(c => c.TryGetCachedImagePath(RemotePersonaImage)).Returns(_cachedImagePath);

        Assert.That(_service.ResolveAlbumImageUri(CreateSong()), Does.StartWith("file://"));
    }

    [Test]
    public void ResolveAlbumImageUri_OnlineWithNothingCached_UsesTheRemoteUrl()
    {
        _imageCache.Setup(c => c.TryGetCachedImagePath(It.IsAny<string>())).Returns((string?)null);

        Assert.That(_service.ResolveAlbumImageUri(CreateSong()), Is.EqualTo(RemoteAlbumArt));
    }

    [Test]
    public void ResolveAlbumImageUri_OfflineWithNothingCached_ReturnsEmpty()
    {
        // Returning the remote URL here would stall Media3's bitmap loader on the media thread waiting
        // for a request that cannot succeed.
        _imageCache.Setup(c => c.TryGetCachedImagePath(It.IsAny<string>())).Returns((string?)null);
        _networkStatus.SetOffline(true);

        Assert.That(_service.ResolveAlbumImageUri(CreateSong()), Is.Empty);
    }

    [Test]
    public void ResolveAlbumImageUri_OfflineWithCachedArt_StillReturnsTheCachedFileUri()
    {
        _imageCache.Setup(c => c.TryGetCachedImagePath(RemoteAlbumArt)).Returns(_cachedImagePath);
        _networkStatus.SetOffline(true);

        Assert.That(_service.ResolveAlbumImageUri(CreateSong()), Does.StartWith("file://"));
    }

    [Test]
    public void ResolveAlbumImageUri_SongWithNoArtwork_ReturnsEmpty()
    {
        _imageCache.Setup(c => c.TryGetCachedImagePath(It.IsAny<string>())).Returns((string?)null);

        Assert.That(_service.ResolveAlbumImageUri(new SongDto { Id = 1 }), Is.Empty);
    }

    /// <summary>
    /// A song with every rendition the server can generate, for the tier-preference tests.
    /// </summary>
    private static SongDto CreateSongWithAllRenditions() => new()
    {
        Id = 1,
        AlbumArtUrl = RemoteAlbumArt,
        AlbumArtThumbUrl = RemoteAlbumArtThumb,
        AlbumArtHeroUrl = RemoteAlbumArtHero,
        PersonaImageUrl = RemotePersonaImage,
        PersonaImageThumbUrl = RemotePersonaImageThumb,
        PersonaImageHeroUrl = RemotePersonaImageHero
    };

    [Test]
    public void ResolveAlbumImageUri_IgnoresTheHeroRenditionEvenWhenItIsCached()
    {
        // The Android no-regression guard. Media3 decodes whatever this returns on the media thread
        // for a notification icon a couple of hundred pixels wide; promoting Android to the hero
        // rendition would put a multi-megabyte decode back on that thread at track start.
        _imageCache.Setup(c => c.TryGetCachedImagePath(It.IsAny<string>(), It.IsAny<int>())).Returns((string?)null);
        _imageCache.Setup(c => c.TryGetCachedImagePath(RemoteAlbumArtHero, 0)).Returns(_cachedImagePath);

        Assert.That(_service.ResolveAlbumImageUri(CreateSongWithAllRenditions()), Is.EqualTo(RemoteAlbumArtThumb));
    }

    [Test]
    public void ResolveNowPlayingArtworkUri_PrefersTheCachedHeroRendition()
    {
        _imageCache.Setup(c => c.TryGetCachedImagePath(It.IsAny<string>(), It.IsAny<int>())).Returns(_cachedImagePath);

        var resolved = _service.ResolveNowPlayingArtworkUri(CreateSongWithAllRenditions());

        Assert.That(resolved, Is.EqualTo(new Uri(_cachedImagePath).AbsoluteUri));
        _imageCache.Verify(c => c.TryGetCachedImagePath(RemoteAlbumArtHero, 0), Times.Once);
    }

    [Test]
    public void ResolveNowPlayingArtworkUri_FallsBackToTheCachedThumbWhenNoHeroIsCached()
    {
        _imageCache.Setup(c => c.TryGetCachedImagePath(It.IsAny<string>(), It.IsAny<int>())).Returns((string?)null);
        _imageCache.Setup(c => c.TryGetCachedImagePath(RemoteAlbumArtThumb, 0)).Returns(_cachedImagePath);

        Assert.That(
            _service.ResolveNowPlayingArtworkUri(CreateSongWithAllRenditions()),
            Is.EqualTo(new Uri(_cachedImagePath).AbsoluteUri));
    }

    [Test]
    public void ResolveNowPlayingArtworkUri_FallsBackToTheCachedPersonaHero()
    {
        _imageCache.Setup(c => c.TryGetCachedImagePath(It.IsAny<string>(), It.IsAny<int>())).Returns((string?)null);
        _imageCache.Setup(c => c.TryGetCachedImagePath(RemotePersonaImageHero, 0)).Returns(_cachedImagePath);

        Assert.That(
            _service.ResolveNowPlayingArtworkUri(CreateSongWithAllRenditions()),
            Is.EqualTo(new Uri(_cachedImagePath).AbsoluteUri));
    }

    [Test]
    public void ResolveNowPlayingArtworkUri_OnlineWithNothingCached_UsesTheRemoteHeroUrl()
    {
        _imageCache.Setup(c => c.TryGetCachedImagePath(It.IsAny<string>(), It.IsAny<int>())).Returns((string?)null);

        Assert.That(
            _service.ResolveNowPlayingArtworkUri(CreateSongWithAllRenditions()),
            Is.EqualTo(RemoteAlbumArtHero));
    }

    [Test]
    public void ResolveNowPlayingArtworkUri_OfflineWithNothingCached_StillReturnsTheRemoteUrl()
    {
        // Deliberately unlike ResolveAlbumImageUri_OfflineWithNothingCached_ReturnsEmpty. That gate
        // exists because Media3 would stall its bitmap loader on the media thread. Nothing analogous
        // applies here: NowPlayingArtworkCoordinator declines to fetch a remote URI with no network
        // access - and does so without consuming a retry attempt - so keeping the URL is what lets
        // artwork appear on its own when connectivity returns mid-track.
        _imageCache.Setup(c => c.TryGetCachedImagePath(It.IsAny<string>(), It.IsAny<int>())).Returns((string?)null);
        _networkStatus.SetOffline(true);

        Assert.That(
            _service.ResolveNowPlayingArtworkUri(CreateSongWithAllRenditions()),
            Is.EqualTo(RemoteAlbumArtHero));
    }

    [Test]
    public void ResolveNowPlayingArtworkUri_SongWithNoArtwork_ReturnsEmpty()
    {
        _imageCache.Setup(c => c.TryGetCachedImagePath(It.IsAny<string>(), It.IsAny<int>())).Returns((string?)null);

        Assert.That(_service.ResolveNowPlayingArtworkUri(new SongDto { Id = 1 }), Is.Empty);
    }

    [Test]
    public void ResolveNowPlayingArtworkUri_HonoursTheContentVersionForEachAssetFamily()
    {
        // Album and persona renditions version independently; a version-less lookup would serve
        // pre-crop artwork forever, because a re-crop overwrites the same blob path in place.
        _imageCache.Setup(c => c.TryGetCachedImagePath(It.IsAny<string>(), It.IsAny<int>())).Returns((string?)null);
        var song = CreateSongWithAllRenditions();
        song.AlbumArtVersion = 3;
        song.PersonaImageVersion = 7;

        _service.ResolveNowPlayingArtworkUri(song);

        _imageCache.Verify(c => c.TryGetCachedImagePath(RemoteAlbumArtHero, 3), Times.Once);
        _imageCache.Verify(c => c.TryGetCachedImagePath(RemotePersonaImageHero, 7), Times.Once);
    }

    [Test]
    public void ResolveArtworkUris_ProbesEachRenditionAtMostOnceAcrossBothLadders()
    {
        // The two ladders overlap in four of six candidates and every probe is a real File.Exists,
        // so building a long queue would otherwise pay for the same stat twice per track.
        _imageCache.Setup(c => c.TryGetCachedImagePath(It.IsAny<string>(), It.IsAny<int>())).Returns((string?)null);

        var (mediaSessionUri, nowPlayingUri, _) = _service.ResolveArtworkUris(CreateSongWithAllRenditions());

        Assert.That(mediaSessionUri, Is.EqualTo(RemoteAlbumArtThumb), "Media3 keeps the thumb");
        Assert.That(nowPlayingUri, Is.EqualTo(RemoteAlbumArtHero), "the now-playing surface gets the hero");

        foreach (var url in new[]
                 {
                     RemoteAlbumArtThumb, RemoteAlbumArtUrlShared, RemotePersonaImageThumb, RemotePersonaImage
                 })
        {
            _imageCache.Verify(c => c.TryGetCachedImagePath(url, It.IsAny<int>()), Times.Once, url);
        }
    }

    [Test]
    public void ResolveNowPlayingArtwork_CarriesTheVersionOfTheRenditionThatWon()
    {
        // Album and persona renditions version independently, so the version cannot be inferred from
        // the URI downstream - it has to travel with it for the image cache to key correctly.
        _imageCache.Setup(c => c.TryGetCachedImagePath(It.IsAny<string>(), It.IsAny<int>())).Returns((string?)null);
        var song = CreateSongWithAllRenditions();
        song.AlbumArtVersion = 3;
        song.PersonaImageVersion = 7;

        var albumWinner = _service.ResolveNowPlayingArtwork(song);
        Assert.That(albumWinner.Uri, Is.EqualTo(RemoteAlbumArtHero));
        Assert.That(albumWinner.ContentVersion, Is.EqualTo(3));

        // Strip the album renditions so a persona candidate wins instead.
        song.AlbumArtHeroUrl = null;
        song.AlbumArtThumbUrl = null;
        song.AlbumArtUrl = null;

        var personaWinner = _service.ResolveNowPlayingArtwork(song);
        Assert.That(personaWinner.Uri, Is.EqualTo(RemotePersonaImageHero));
        Assert.That(personaWinner.ContentVersion, Is.EqualTo(7));
    }

    [Test]
    public void ResolveAlbumImageUri_WithNoImageCacheService_KeepsThePreExistingBehaviour()
    {
        var serviceWithoutImageCache = new PlaybackService(
            new Mock<IAuthService>().Object,
            new Mock<IMusicService>().Object,
            new Mock<IPlatformPlaybackRuntime>().Object,
            new Mock<IAudioCacheService>().Object,
            new Mock<IQueuePreparationService>().Object,
            new Mock<IPlaybackKeepAliveService>().Object,
            NullLogger<PlaybackService>.Instance);

        Assert.That(serviceWithoutImageCache.ResolveAlbumImageUri(CreateSong()), Is.EqualTo(RemoteAlbumArt));
    }
}
