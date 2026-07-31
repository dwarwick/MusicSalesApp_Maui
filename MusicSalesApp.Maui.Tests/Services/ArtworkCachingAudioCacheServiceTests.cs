using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Tests.Services;

[TestFixture]
public class ArtworkCachingAudioCacheServiceTests
{
    private Mock<IAudioCacheService> _inner = null!;
    private Mock<IImageCacheService> _imageCache = null!;
    private TestNetworkStatusService _networkStatus = null!;
    private ArtworkCachingAudioCacheService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _inner = new Mock<IAudioCacheService>();
        _imageCache = new Mock<IImageCacheService>();
        _networkStatus = new TestNetworkStatusService();

        // Downloads succeed by default. The decorator keys its retry decision off the outcome, so a
        // loose mock's default would read as a failed download in every test.
        _imageCache.Setup(c => c.TryEnsureCachedAsync(It.IsAny<string>(), It.IsAny<ImageCachePriority>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Cached);

        _service = CreateService();
    }

    private ArtworkCachingAudioCacheService CreateService() => new(
        _inner.Object,
        _imageCache.Object,
        NullLogger<ArtworkCachingAudioCacheService>.Instance,
        _networkStatus);

    private static SongDto CreateSong(int id = 1) => new()
    {
        Id = id,
        StreamUrl = $"https://storage.test/songs/{id}.mp3",
        AlbumArtUrl = $"https://storage.test/images/{id}.jpg?sig=aaa",
        PersonaImageUrl = $"https://storage.test/personas/{id}.jpg?sig=bbb"
    };

    private static TrackCacheStatus Status(int songId, bool isLocalReady)
        => new(songId, $"song-{songId}", isLocalReady ? $"/cache/{songId}.mp3" : null, isLocalReady, false);

    private static readonly ImageCacheOutcome Cached =
        new("/cache/image-cache/cover.jpg", ImageCacheResult.Cached);

    private static readonly ImageCacheOutcome DownloadFailed =
        new(null, ImageCacheResult.Failed);

    /// <summary>Turned away by the budget - nothing wrong with the image.</summary>
    private static readonly ImageCacheOutcome Declined =
        new(null, ImageCacheResult.Declined);

    /// <summary>Artwork caching is fire-and-forget, so tests need a moment for it to land.</summary>
    private async Task WaitForBackfillAsync() => await Task.Delay(100);

    // --- EnsureCachedAsync ---

    [Test]
    public async Task EnsureCachedAsync_WhenAudioIsReady_CachesBothImages()
    {
        var song = CreateSong();
        _inner.Setup(s => s.EnsureCachedAsync(song, It.IsAny<CachePinScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Status(song.Id, isLocalReady: true));

        await _service.EnsureCachedAsync(song, CachePinScope.ActiveQueue);
        await WaitForBackfillAsync();

        _imageCache.Verify(c => c.TryEnsureCachedAsync(song.AlbumArtUrl, It.IsAny<ImageCachePriority>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        _imageCache.Verify(c => c.TryEnsureCachedAsync(song.PersonaImageUrl, It.IsAny<ImageCachePriority>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task EnsureCachedAsync_WhenAudioIsNotReady_CachesNothing()
    {
        // Artwork is cached only for songs that are actually playable offline.
        var song = CreateSong();
        _inner.Setup(s => s.EnsureCachedAsync(song, It.IsAny<CachePinScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Status(song.Id, isLocalReady: false));

        await _service.EnsureCachedAsync(song, CachePinScope.TemporaryWarm);
        await WaitForBackfillAsync();

        _imageCache.Verify(c => c.TryEnsureCachedAsync(It.IsAny<string>(), It.IsAny<ImageCachePriority>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task EnsureCachedAsync_ReturnsTheInnerStatusUnchanged()
    {
        var song = CreateSong();
        var expected = Status(song.Id, isLocalReady: true);
        _inner.Setup(s => s.EnsureCachedAsync(song, It.IsAny<CachePinScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        Assert.That(await _service.EnsureCachedAsync(song, CachePinScope.ActiveQueue), Is.EqualTo(expected));
    }

    [Test]
    public async Task EnsureCachedAsync_ImageCacheFailure_DoesNotPropagate()
    {
        var song = CreateSong();
        _inner.Setup(s => s.EnsureCachedAsync(song, It.IsAny<CachePinScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Status(song.Id, isLocalReady: true));
        _imageCache.Setup(c => c.TryEnsureCachedAsync(It.IsAny<string>(), It.IsAny<ImageCachePriority>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("disk full"));

        Assert.That(async () => await _service.EnsureCachedAsync(song, CachePinScope.ActiveQueue), Throws.Nothing);
        await WaitForBackfillAsync();
    }

    // --- GetCacheStatusesAsync backfill ---

    [Test]
    public async Task GetCacheStatusesAsync_BackfillsArtworkOnlyForReadySongs()
    {
        var ready = CreateSong(1);
        var notReady = CreateSong(2);
        _inner.Setup(s => s.GetCacheStatusesAsync(It.IsAny<IReadOnlyList<SongDto>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<int, TrackCacheStatus>
            {
                [1] = Status(1, isLocalReady: true),
                [2] = Status(2, isLocalReady: false)
            });

        await _service.GetCacheStatusesAsync([ready, notReady]);
        await WaitForBackfillAsync();

        _imageCache.Verify(c => c.TryEnsureCachedAsync(ready.AlbumArtUrl, It.IsAny<ImageCachePriority>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        _imageCache.Verify(c => c.TryEnsureCachedAsync(notReady.AlbumArtUrl, It.IsAny<ImageCachePriority>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task GetCacheStatusesAsync_DoesNotReDownloadAnImageItAlreadyCached()
    {
        var song = CreateSong();
        _inner.Setup(s => s.GetCacheStatusesAsync(It.IsAny<IReadOnlyList<SongDto>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<int, TrackCacheStatus> { [1] = Status(1, isLocalReady: true) });

        await _service.GetCacheStatusesAsync([song]);
        await WaitForBackfillAsync();
        await _service.GetCacheStatusesAsync([song]);
        await WaitForBackfillAsync();

        _imageCache.Verify(c => c.TryEnsureCachedAsync(song.AlbumArtUrl, It.IsAny<ImageCachePriority>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task GetCacheStatusesAsync_TreatsARotatedSasTokenAsTheSameImage()
    {
        _inner.Setup(s => s.GetCacheStatusesAsync(It.IsAny<IReadOnlyList<SongDto>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<int, TrackCacheStatus> { [1] = Status(1, isLocalReady: true) });
        var first = CreateSong();
        var second = CreateSong();
        second.AlbumArtUrl = "https://storage.test/images/1.jpg?sig=zzz-rotated";
        second.PersonaImageUrl = "https://storage.test/personas/1.jpg?sig=zzz-rotated";

        await _service.GetCacheStatusesAsync([first]);
        await WaitForBackfillAsync();
        await _service.GetCacheStatusesAsync([second]);
        await WaitForBackfillAsync();

        _imageCache.Verify(c => c.TryEnsureCachedAsync(It.IsAny<string>(), It.IsAny<ImageCachePriority>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Test]
    public async Task GetCacheStatusesAsync_RetriesAnImageThatFailedOnAnEarlierRefresh()
    {
        // A momentary blip must not blank a cover for the rest of the session: the next refresh, on a
        // working connection, has to try again.
        var song = CreateSong();
        _inner.Setup(s => s.GetCacheStatusesAsync(It.IsAny<IReadOnlyList<SongDto>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<int, TrackCacheStatus> { [1] = Status(1, isLocalReady: true) });
        _imageCache.Setup(c => c.TryEnsureCachedAsync(song.AlbumArtUrl, It.IsAny<ImageCachePriority>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DownloadFailed);

        await _service.GetCacheStatusesAsync([song]);
        await WaitForBackfillAsync();

        _imageCache.Setup(c => c.TryEnsureCachedAsync(song.AlbumArtUrl, It.IsAny<ImageCachePriority>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Cached);

        await _service.GetCacheStatusesAsync([song]);
        await WaitForBackfillAsync();

        _imageCache.Verify(
            c => c.TryEnsureCachedAsync(song.AlbumArtUrl, It.IsAny<ImageCachePriority>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Test]
    public async Task GetCacheStatusesAsync_StopsRetryingAnImageThatKeepsFailing()
    {
        // A genuinely broken image must not cost a request on every single list refresh.
        var song = CreateSong();
        _inner.Setup(s => s.GetCacheStatusesAsync(It.IsAny<IReadOnlyList<SongDto>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<int, TrackCacheStatus> { [1] = Status(1, isLocalReady: true) });
        _imageCache.Setup(c => c.TryEnsureCachedAsync(song.AlbumArtUrl, It.IsAny<ImageCachePriority>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DownloadFailed);

        for (var refresh = 0; refresh < 6; refresh++)
        {
            await _service.GetCacheStatusesAsync([song]);
            await WaitForBackfillAsync();
        }

        _imageCache.Verify(
            c => c.TryEnsureCachedAsync(song.AlbumArtUrl, It.IsAny<ImageCachePriority>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Test]
    public async Task GetCacheStatusesAsync_AnImageTheBudgetDeclinedIsNeverBlacklisted()
    {
        // A hero turned away by the budget is not a broken image, and counting it against the retry
        // limit would blacklist heroes for the rest of the session after three declines - permanently,
        // because the prune that frees the space is exactly what would have let them through.
        var song = CreateSong();
        _inner.Setup(s => s.GetCacheStatusesAsync(It.IsAny<IReadOnlyList<SongDto>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<int, TrackCacheStatus> { [1] = Status(1, isLocalReady: true) });
        _imageCache.Setup(c => c.TryEnsureCachedAsync(song.AlbumArtUrl, It.IsAny<ImageCachePriority>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Declined);

        for (var refresh = 0; refresh < 6; refresh++)
        {
            await _service.GetCacheStatusesAsync([song]);
            await WaitForBackfillAsync();
        }

        _imageCache.Verify(
            c => c.TryEnsureCachedAsync(song.AlbumArtUrl, It.IsAny<ImageCachePriority>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Exactly(6));
    }

    [Test]
    public async Task GetCacheStatusesAsync_AnImageSkippedWhileOfflineIsNeverBlacklisted()
    {
        var song = CreateSong();
        _inner.Setup(s => s.GetCacheStatusesAsync(It.IsAny<IReadOnlyList<SongDto>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<int, TrackCacheStatus> { [1] = Status(1, isLocalReady: true) });
        _imageCache.Setup(c => c.TryEnsureCachedAsync(song.AlbumArtUrl, It.IsAny<ImageCachePriority>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImageCacheOutcome(null, ImageCacheResult.Offline));

        for (var refresh = 0; refresh < 6; refresh++)
        {
            await _service.GetCacheStatusesAsync([song]);
            await WaitForBackfillAsync();
        }

        _imageCache.Verify(
            c => c.TryEnsureCachedAsync(song.AlbumArtUrl, It.IsAny<ImageCachePriority>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Exactly(6));
    }

    [Test]
    public async Task GetCacheStatusesAsync_AnImageThatThrows_IsAlsoRetried()
    {
        var song = CreateSong();
        _inner.Setup(s => s.GetCacheStatusesAsync(It.IsAny<IReadOnlyList<SongDto>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<int, TrackCacheStatus> { [1] = Status(1, isLocalReady: true) });
        _imageCache.Setup(c => c.TryEnsureCachedAsync(song.AlbumArtUrl, It.IsAny<ImageCachePriority>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("transient"));

        await _service.GetCacheStatusesAsync([song]);
        await WaitForBackfillAsync();
        await _service.GetCacheStatusesAsync([song]);
        await WaitForBackfillAsync();

        _imageCache.Verify(
            c => c.TryEnsureCachedAsync(song.AlbumArtUrl, It.IsAny<ImageCachePriority>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Test]
    public async Task GetCacheStatusesAsync_WhileOffline_DoesNotBackfill()
    {
        _networkStatus.SetOffline(true);
        _inner.Setup(s => s.GetCacheStatusesAsync(It.IsAny<IReadOnlyList<SongDto>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<int, TrackCacheStatus> { [1] = Status(1, isLocalReady: true) });

        await _service.GetCacheStatusesAsync([CreateSong()]);
        await WaitForBackfillAsync();

        _imageCache.Verify(c => c.TryEnsureCachedAsync(It.IsAny<string>(), It.IsAny<ImageCachePriority>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task GetCacheStatusesAsync_ReturnsTheInnerStatusesUnchanged()
    {
        var statuses = new Dictionary<int, TrackCacheStatus> { [1] = Status(1, isLocalReady: true) };
        _inner.Setup(s => s.GetCacheStatusesAsync(It.IsAny<IReadOnlyList<SongDto>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(statuses);

        Assert.That(await _service.GetCacheStatusesAsync([CreateSong()]), Is.EqualTo(statuses));
    }

    [Test]
    public async Task GetCacheStatusesAsync_SongsWithoutArtwork_AreSkipped()
    {
        var song = CreateSong();
        song.AlbumArtUrl = null;
        song.PersonaImageUrl = null;
        _inner.Setup(s => s.GetCacheStatusesAsync(It.IsAny<IReadOnlyList<SongDto>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<int, TrackCacheStatus> { [1] = Status(1, isLocalReady: true) });

        await _service.GetCacheStatusesAsync([song]);
        await WaitForBackfillAsync();

        _imageCache.Verify(c => c.TryEnsureCachedAsync(It.IsAny<string>(), It.IsAny<ImageCachePriority>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // --- Usage reporting ---

    [Test]
    public async Task GetCacheUsageBytesAsync_SumsAudioAndImageUsage()
    {
        _inner.Setup(s => s.GetCacheUsageBytesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1000);
        _imageCache.Setup(c => c.GetCacheUsageBytesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(250);

        Assert.That(await _service.GetCacheUsageBytesAsync(), Is.EqualTo(1250));
    }

    [Test]
    public async Task GetCacheUsageBytesAsync_ImageCacheFailure_StillReportsAudioUsage()
    {
        _inner.Setup(s => s.GetCacheUsageBytesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1000);
        _imageCache.Setup(c => c.GetCacheUsageBytesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("unreadable"));

        Assert.That(await _service.GetCacheUsageBytesAsync(), Is.EqualTo(1000));
    }

    // --- Pass-throughs ---

    [Test]
    public async Task PassThroughMembers_DelegateToTheInnerService()
    {
        var song = CreateSong();
        _service.GetStableCacheKey(song);
        await _service.GetCacheStatusAsync(song);
        await _service.ResolvePlaybackUriAsync(song);
        _service.PinActiveQueue([song]);

        _inner.Verify(s => s.GetStableCacheKey(song), Times.Once);
        _inner.Verify(s => s.GetCacheStatusAsync(song, It.IsAny<CancellationToken>()), Times.Once);
        _inner.Verify(s => s.ResolvePlaybackUriAsync(song, It.IsAny<CancellationToken>()), Times.Once);
        _inner.Verify(s => s.PinActiveQueue(It.IsAny<IReadOnlyList<SongDto>>()), Times.Once);
    }
}
