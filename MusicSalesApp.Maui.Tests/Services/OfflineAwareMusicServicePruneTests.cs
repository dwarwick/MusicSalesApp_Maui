using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Maui.Networking;
using Moq;
using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Tests.Services;

/// <summary>
/// The artwork prune deletes every cached file whose name is not derived from the retained URL set.
/// Each pre-resized rendition is a separate blob path and therefore a separate cache entry, so
/// leaving one out of that set would delete it after every catalog load and re-download it moments
/// later - a silent loop that would quietly burn the user's data allowance forever.
/// </summary>
[TestFixture]
public class OfflineAwareMusicServicePruneTests
{
    private Mock<IMusicService> _inner = null!;
    private Mock<IOfflineSongCatalogStore> _catalogStore = null!;
    private Mock<ITrackCacheService> _trackCache = null!;
    private Mock<IImageCacheService> _imageCache = null!;
    private OfflineAwareMusicService _service = null!;

    private IReadOnlyCollection<CachedImageReference>? _retained;

    [SetUp]
    public void SetUp()
    {
        // NUnit reuses the fixture instance across tests, so this has to be cleared explicitly -
        // otherwise the wait loop below sees a previous test's value and returns immediately.
        _retained = null;

        _inner = new Mock<IMusicService>();
        _catalogStore = new Mock<IOfflineSongCatalogStore>();
        _trackCache = new Mock<ITrackCacheService>();
        _imageCache = new Mock<IImageCacheService>();

        _catalogStore.Setup(s => s.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);

        _imageCache
            .Setup(c => c.PruneAsync(It.IsAny<IReadOnlyCollection<CachedImageReference>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyCollection<CachedImageReference>, CancellationToken>((images, _) => _retained = images)
            .Returns(Task.CompletedTask);

        _service = new OfflineAwareMusicService(
            _inner.Object,
            _catalogStore.Object,
            _trackCache.Object,
            new TestConnectivity(),
            NullLogger<OfflineAwareMusicService>.Instance,
            _imageCache.Object);
    }

    private static SongDto SongWithEveryArtworkUrl() => new()
    {
        Id = 1,
        SongTitle = "Night Drive",
        StreamUrl = "https://storage.test/songs/1.mp3",
        AlbumArtUrl = "https://cdn/cover.jpg",
        AlbumArtThumbUrl = "https://cdn/cover.jpg.w320.webp",
        AlbumArtHeroUrl = "https://cdn/cover.jpg.w640.webp",
        AlbumArtVersion = 3,
        PersonaImageUrl = "https://cdn/persona.png",
        PersonaImageThumbUrl = "https://cdn/persona.png.w320.webp",
        PersonaImageHeroUrl = "https://cdn/persona.png.w640.webp",
        PersonaImageVersion = 2
    };

    private async Task LoadLiveAsync(params SongDto[] songs)
    {
        _inner.Setup(s => s.GetSongsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(songs.ToList());
        _inner.SetupGet(s => s.LastSongsError).Returns((string?)null);

        await _service.GetSongsAsync();

        // The prune is deliberately fire-and-forget so it never delays a list load.
        for (var attempt = 0; attempt < 50 && _retained == null; attempt++)
        {
            await Task.Delay(10);
        }
    }

    /// <summary>What the prune keys on; the supersession hints are asserted separately.</summary>
    private IEnumerable<(string Url, int Version)> RetainedKeys()
        => _retained!.Select(image => (image.Url, image.Version));

    [Test]
    public async Task TheRetainedSetContainsEveryArtworkUrlIncludingTheRenditions()
    {
        await LoadLiveAsync(SongWithEveryArtworkUrl());

        Assert.That(_retained, Is.Not.Null, "the prune should have run after a successful live load");
        Assert.That(RetainedKeys(), Is.EquivalentTo(new[]
        {
            ("https://cdn/cover.jpg", 3),
            ("https://cdn/cover.jpg.w320.webp", 3),
            ("https://cdn/cover.jpg.w640.webp", 3),
            ("https://cdn/persona.png", 2),
            ("https://cdn/persona.png.w320.webp", 2),
            ("https://cdn/persona.png.w640.webp", 2)
        }));
    }

    [Test]
    public async Task TheFullSizeOriginalsAreRetainedOnlyUntilTheirThumbsAreCached()
    {
        // Keeping a multi-megabyte master permanently beside a twenty-kilobyte rendition would eat
        // the budget the renditions exist to free, and the budget has no eviction to recover from it.
        // It stays reachable while the thumb is missing, because the display chain still falls back.
        await LoadLiveAsync(SongWithEveryArtworkUrl());

        var cover = _retained!.Single(image => image.Url == "https://cdn/cover.jpg");
        var persona = _retained!.Single(image => image.Url == "https://cdn/persona.png");

        Assert.Multiple(() =>
        {
            Assert.That(cover.SupersededBy, Is.EqualTo("https://cdn/cover.jpg.w320.webp"));
            Assert.That(cover.SupersededByVersion, Is.EqualTo(3));
            Assert.That(persona.SupersededBy, Is.EqualTo("https://cdn/persona.png.w320.webp"));
            Assert.That(persona.SupersededByVersion, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task EachUrlIsRetainedAtItsOwnImagesVersion()
    {
        // Cover art and persona images are versioned independently - replacing one must not
        // invalidate the other's cached copy.
        await LoadLiveAsync(SongWithEveryArtworkUrl());

        Assert.Multiple(() =>
        {
            Assert.That(
                _retained!.Where(i => i.Url.Contains("cover")).Select(i => i.Version),
                Is.All.EqualTo(3));
            Assert.That(
                _retained!.Where(i => i.Url.Contains("persona")).Select(i => i.Version),
                Is.All.EqualTo(2));
        });
    }

    [Test]
    public async Task ASongWithNoRenditionsRetainsOnlyItsOriginals()
    {
        var song = SongWithEveryArtworkUrl();
        song.AlbumArtThumbUrl = null;
        song.AlbumArtHeroUrl = null;
        song.PersonaImageThumbUrl = null;
        song.PersonaImageHeroUrl = null;

        await LoadLiveAsync(song);

        Assert.That(RetainedKeys(), Is.EquivalentTo(new[]
        {
            ("https://cdn/cover.jpg", 3),
            ("https://cdn/persona.png", 2)
        }));
    }

    [Test]
    public async Task WithNoThumbToSupersedeIt_TheOriginalIsRetainedUnconditionally()
    {
        // Nothing has replaced it, so it must never be treated as dead weight.
        var song = SongWithEveryArtworkUrl();
        song.AlbumArtThumbUrl = null;

        await LoadLiveAsync(song);

        var cover = _retained!.Single(image => image.Url == "https://cdn/cover.jpg");

        Assert.Multiple(() =>
        {
            Assert.That(cover.SupersededBy, Is.Null);
            Assert.That(cover.SupersededByVersion, Is.Zero);
        });
    }

    [Test]
    public async Task DuplicateUrlsAcrossSongsAreCollapsed()
    {
        // Every song by one creator shares a persona image, and the cache stores it once.
        var first = SongWithEveryArtworkUrl();
        var second = SongWithEveryArtworkUrl();
        second.Id = 2;

        await LoadLiveAsync(first, second);

        Assert.That(_retained, Has.Count.EqualTo(6));
    }
}
