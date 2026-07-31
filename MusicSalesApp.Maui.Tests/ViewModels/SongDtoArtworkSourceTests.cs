using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Tests.ViewModels;

/// <summary>
/// The display-source chains are what keep this app working against a server that has not been
/// updated, and against images whose renditions have not been generated. Each fallback step is a
/// real scenario, not a formality.
/// </summary>
[TestFixture]
public class SongDtoArtworkSourceTests
{
    private static SongDto FullyPopulated() => new()
    {
        AlbumArtUrl = "https://cdn/cover.jpg",
        AlbumArtThumbUrl = "https://cdn/cover.jpg.w320.webp",
        AlbumArtHeroUrl = "https://cdn/cover.jpg.w640.webp",
        PersonaImageUrl = "https://cdn/persona.png",
        PersonaImageThumbUrl = "https://cdn/persona.png.w320.webp"
    };

    [Test]
    public void WithEverythingAvailable_EachSurfacePicksItsOwnRendition()
    {
        var song = FullyPopulated();

        Assert.Multiple(() =>
        {
            Assert.That(song.AlbumArtThumbDisplaySource, Is.EqualTo("https://cdn/cover.jpg.w320.webp"));
            Assert.That(song.AlbumArtHeroDisplaySource, Is.EqualTo("https://cdn/cover.jpg.w640.webp"));
            Assert.That(song.PersonaImageThumbDisplaySource, Is.EqualTo("https://cdn/persona.png.w320.webp"));
        });
    }

    [Test]
    public void AgainstAnOlderServer_EverythingFallsBackToTheOriginalUrls()
    {
        // The whole backward-compatibility story. A server that predates this feature sends null for
        // every rendition URL, and the app must behave exactly as it did before.
        var song = new SongDto
        {
            AlbumArtUrl = "https://cdn/cover.jpg",
            PersonaImageUrl = "https://cdn/persona.png"
        };

        Assert.Multiple(() =>
        {
            Assert.That(song.AlbumArtThumbDisplaySource, Is.EqualTo("https://cdn/cover.jpg"));
            Assert.That(song.AlbumArtHeroDisplaySource, Is.EqualTo("https://cdn/cover.jpg"));
            Assert.That(song.PersonaImageThumbDisplaySource, Is.EqualTo("https://cdn/persona.png"));
            Assert.That(song.AlbumArtDisplaySource, Is.EqualTo("https://cdn/cover.jpg"));
        });
    }

    [Test]
    public void WithNoHeroRendition_TheHeroSurfaceUsesTheThumbBeforeTheOriginal()
    {
        // The steady state once the hero budget is reached. An upscaled thumb renders immediately;
        // the full-size original would be roughly ten times the bytes.
        var song = FullyPopulated();
        song.AlbumArtHeroUrl = null;

        Assert.That(song.AlbumArtHeroDisplaySource, Is.EqualTo("https://cdn/cover.jpg.w320.webp"));
    }

    [Test]
    public void CachedPathsWinOverRemoteUrls()
    {
        var song = FullyPopulated();
        song.CachedAlbumArtThumbPath = "/local/thumb.webp";
        song.CachedAlbumArtHeroPath = "/local/hero.webp";
        song.CachedPersonaImageThumbPath = "/local/persona.webp";

        Assert.Multiple(() =>
        {
            Assert.That(song.AlbumArtThumbDisplaySource, Is.EqualTo("/local/thumb.webp"));
            Assert.That(song.AlbumArtHeroDisplaySource, Is.EqualTo("/local/hero.webp"));
            Assert.That(song.PersonaImageThumbDisplaySource, Is.EqualTo("/local/persona.webp"));
        });
    }

    [Test]
    public void ACachedFullSizeCopyFromAnEarlierBuildIsStillUsed()
    {
        // Upgrading the app must not orphan artwork the previous version had already cached.
        var song = FullyPopulated();
        song.CachedAlbumArtPath = "/local/legacy-cover.jpg";

        Assert.Multiple(() =>
        {
            Assert.That(song.AlbumArtThumbDisplaySource, Is.EqualTo("/local/legacy-cover.jpg"));
            Assert.That(song.AlbumArtHeroDisplaySource, Is.EqualTo("/local/legacy-cover.jpg"));
        });
    }

    [Test]
    public void ACachedThumbWinsOverACachedFullSizeCopy()
    {
        var song = FullyPopulated();
        song.CachedAlbumArtPath = "/local/legacy-cover.jpg";
        song.CachedAlbumArtThumbPath = "/local/thumb.webp";

        Assert.That(song.AlbumArtThumbDisplaySource, Is.EqualTo("/local/thumb.webp"));
    }

    [Test]
    public void WhileOffline_RemoteUrlsAreSuppressedButCachedCopiesAreNot()
    {
        var song = FullyPopulated();
        song.SuppressRemoteArtwork = true;

        Assert.Multiple(() =>
        {
            Assert.That(song.AlbumArtThumbDisplaySource, Is.Null);
            Assert.That(song.AlbumArtHeroDisplaySource, Is.Null);
            Assert.That(song.PersonaImageThumbDisplaySource, Is.Null);
        });

        song.CachedAlbumArtThumbPath = "/local/thumb.webp";
        Assert.That(song.AlbumArtThumbDisplaySource, Is.EqualTo("/local/thumb.webp"));
    }

    [Test]
    public void SuppressRemoteArtwork_NotifiesEveryComputedSource()
    {
        // Without these notifications the bound Image keeps rendering a URL that will never load.
        var song = FullyPopulated();
        var changed = new List<string>();
        song.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        song.SuppressRemoteArtwork = true;

        Assert.That(changed, Is.SupersetOf(new[]
        {
            nameof(SongDto.AlbumArtDisplaySource),
            nameof(SongDto.PersonaImageDisplaySource),
            nameof(SongDto.AlbumArtThumbDisplaySource),
            nameof(SongDto.AlbumArtHeroDisplaySource),
            nameof(SongDto.PersonaImageThumbDisplaySource)
        }));
    }

    [Test]
    public void SettingACachedThumbNotifiesBothSurfacesThatDependOnIt()
    {
        var song = FullyPopulated();
        var changed = new List<string>();
        song.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        song.CachedAlbumArtThumbPath = "/local/thumb.webp";

        Assert.That(changed, Is.SupersetOf(new[]
        {
            nameof(SongDto.AlbumArtThumbDisplaySource),
            nameof(SongDto.AlbumArtHeroDisplaySource)
        }));
    }

    [Test]
    public void WithNoArtworkAtAll_EverySurfaceIsNull()
    {
        var song = new SongDto();

        Assert.Multiple(() =>
        {
            Assert.That(song.AlbumArtThumbDisplaySource, Is.Null);
            Assert.That(song.AlbumArtHeroDisplaySource, Is.Null);
            Assert.That(song.PersonaImageThumbDisplaySource, Is.Null);
        });
    }
}
