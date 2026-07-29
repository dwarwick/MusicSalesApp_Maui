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
