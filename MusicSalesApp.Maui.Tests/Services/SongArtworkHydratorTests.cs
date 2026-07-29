using Moq;
using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Tests.Services;

[TestFixture]
public class SongArtworkHydratorTests
{
    private const string AlbumArtUrl = "https://storage.test/images/1.jpg?sig=aaa";
    private const string PersonaImageUrl = "https://storage.test/personas/1.jpg?sig=bbb";
    private const string CachedAlbumArtPath = "/cache/image-cache/album.jpg";
    private const string CachedPersonaImagePath = "/cache/image-cache/persona.jpg";

    private Mock<IImageCacheService> _imageCache = null!;
    private TestNetworkStatusService _networkStatus = null!;
    private SongArtworkHydrator _hydrator = null!;

    [SetUp]
    public void SetUp()
    {
        _imageCache = new Mock<IImageCacheService>();
        _networkStatus = new TestNetworkStatusService();
        _hydrator = new SongArtworkHydrator(_imageCache.Object, _networkStatus);
    }

    private static SongDto CreateSong() => new()
    {
        Id = 1,
        AlbumArtUrl = AlbumArtUrl,
        PersonaImageUrl = PersonaImageUrl
    };

    private void GivenCached(string? albumArtPath, string? personaImagePath)
    {
        _imageCache.Setup(c => c.TryGetCachedImagePath(AlbumArtUrl)).Returns(albumArtPath);
        _imageCache.Setup(c => c.TryGetCachedImagePath(PersonaImageUrl)).Returns(personaImagePath);
    }

    [Test]
    public async Task HydrateAsync_PointsArtworkAtTheCachedCopies()
    {
        var song = CreateSong();
        GivenCached(CachedAlbumArtPath, CachedPersonaImagePath);

        await _hydrator.HydrateAsync([song]);

        Assert.Multiple(() =>
        {
            Assert.That(song.AlbumArtDisplaySource, Is.EqualTo(CachedAlbumArtPath));
            Assert.That(song.PersonaImageDisplaySource, Is.EqualTo(CachedPersonaImagePath));
        });
    }

    [Test]
    public async Task HydrateAsync_OnlineWithNothingCached_LeavesTheRemoteUrlsInPlace()
    {
        // The safe fallback: an unhydrated or uncached song behaves exactly as it did before.
        var song = CreateSong();
        GivenCached(null, null);

        await _hydrator.HydrateAsync([song]);

        Assert.Multiple(() =>
        {
            Assert.That(song.AlbumArtDisplaySource, Is.EqualTo(AlbumArtUrl));
            Assert.That(song.PersonaImageDisplaySource, Is.EqualTo(PersonaImageUrl));
        });
    }

    [Test]
    public async Task HydrateAsync_OfflineWithNothingCached_SuppressesTheRemoteUrls()
    {
        // Offline the remote URL can only fail to load, so the UI shows its placeholder instead.
        var song = CreateSong();
        GivenCached(null, null);
        _networkStatus.SetOffline(true);

        await _hydrator.HydrateAsync([song]);

        Assert.Multiple(() =>
        {
            Assert.That(song.AlbumArtDisplaySource, Is.Null);
            Assert.That(song.PersonaImageDisplaySource, Is.Null);
        });
    }

    [Test]
    public async Task HydrateAsync_OfflineWithCachedArt_StillShowsTheCachedCopy()
    {
        var song = CreateSong();
        GivenCached(CachedAlbumArtPath, null);
        _networkStatus.SetOffline(true);

        await _hydrator.HydrateAsync([song]);

        Assert.That(song.AlbumArtDisplaySource, Is.EqualTo(CachedAlbumArtPath));
        Assert.That(song.PersonaImageDisplaySource, Is.Null);
    }

    [Test]
    public async Task HydrateAsync_ComingBackOnline_ClearsTheSuppressionFlag()
    {
        var song = CreateSong();
        GivenCached(null, null);
        _networkStatus.SetOffline(true);
        await _hydrator.HydrateAsync([song]);

        _networkStatus.SetOffline(false);
        await _hydrator.HydrateAsync([song]);

        Assert.That(song.AlbumArtDisplaySource, Is.EqualTo(AlbumArtUrl));
    }

    [Test]
    public async Task HydrateAsync_RaisesPropertyChangedSoLiveBindingsRefresh()
    {
        var song = CreateSong();
        GivenCached(CachedAlbumArtPath, null);
        var raised = new List<string?>();
        song.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        await _hydrator.HydrateAsync([song]);

        Assert.That(raised, Does.Contain(nameof(SongDto.AlbumArtDisplaySource)));
    }

    [Test]
    public async Task HydrateAsync_WithNoSongs_DoesNothing()
    {
        await _hydrator.HydrateAsync([]);

        _imageCache.Verify(c => c.TryGetCachedImagePath(It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task HydrateAsync_HandlesEverySongInTheList()
    {
        var songs = Enumerable.Range(1, 5).Select(_ => CreateSong()).ToList();
        GivenCached(CachedAlbumArtPath, CachedPersonaImagePath);

        await _hydrator.HydrateAsync(songs);

        Assert.That(songs.All(s => s.AlbumArtDisplaySource == CachedAlbumArtPath), Is.True);
    }

    [Test]
    public async Task HydrateAsync_NeverTriggersADownload()
    {
        // Hydration is a pure lookup; downloads are the audio cache's job.
        GivenCached(null, null);

        await _hydrator.HydrateAsync([CreateSong()]);

        _imageCache.Verify(c => c.EnsureCachedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
