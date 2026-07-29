using System.Text.Json;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Tests.ViewModels;

[TestFixture]
public class SongDtoArtworkTests
{
    private const string RemoteAlbumArt = "https://storage.test/images/1.jpg?sig=aaa";
    private const string RemotePersonaImage = "https://storage.test/personas/1.jpg?sig=bbb";
    private const string LocalAlbumArt = "/cache/image-cache/album.jpg";
    private const string LocalPersonaImage = "/cache/image-cache/persona.jpg";

    private static SongDto CreateSong() => new()
    {
        Id = 1,
        AlbumArtUrl = RemoteAlbumArt,
        PersonaImageUrl = RemotePersonaImage
    };

    [Test]
    public void DisplaySources_DefaultToTheRemoteUrls()
    {
        // Load-bearing: a code path that never hydrates behaves exactly as it did before this feature.
        var song = CreateSong();

        Assert.Multiple(() =>
        {
            Assert.That(song.AlbumArtDisplaySource, Is.EqualTo(RemoteAlbumArt));
            Assert.That(song.PersonaImageDisplaySource, Is.EqualTo(RemotePersonaImage));
        });
    }

    [Test]
    public void DisplaySources_PreferTheCachedPaths()
    {
        var song = CreateSong();
        song.CachedAlbumArtPath = LocalAlbumArt;
        song.CachedPersonaImagePath = LocalPersonaImage;

        Assert.Multiple(() =>
        {
            Assert.That(song.AlbumArtDisplaySource, Is.EqualTo(LocalAlbumArt));
            Assert.That(song.PersonaImageDisplaySource, Is.EqualTo(LocalPersonaImage));
        });
    }

    [Test]
    public void DisplaySources_SuppressRemoteUrlsWhenSuppressionIsSet()
    {
        var song = CreateSong();
        song.SuppressRemoteArtwork = true;

        Assert.Multiple(() =>
        {
            Assert.That(song.AlbumArtDisplaySource, Is.Null);
            Assert.That(song.PersonaImageDisplaySource, Is.Null);
        });
    }

    [Test]
    public void DisplaySources_CachedPathWinsOverSuppression()
    {
        var song = CreateSong();
        song.CachedAlbumArtPath = LocalAlbumArt;
        song.SuppressRemoteArtwork = true;

        Assert.That(song.AlbumArtDisplaySource, Is.EqualTo(LocalAlbumArt));
    }

    [Test]
    public void DisplaySources_AreNullWhenThereIsNoArtworkAtAll()
    {
        var song = new SongDto { Id = 1 };

        Assert.Multiple(() =>
        {
            Assert.That(song.AlbumArtDisplaySource, Is.Null);
            Assert.That(song.PersonaImageDisplaySource, Is.Null);
        });
    }

    [TestCase(nameof(SongDto.CachedAlbumArtPath), nameof(SongDto.AlbumArtDisplaySource))]
    [TestCase(nameof(SongDto.CachedPersonaImagePath), nameof(SongDto.PersonaImageDisplaySource))]
    public void SettingACachedPath_RaisesPropertyChangedForItsDisplaySource(string setProperty, string expectedNotification)
    {
        var song = CreateSong();
        var raised = new List<string?>();
        song.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        if (setProperty == nameof(SongDto.CachedAlbumArtPath))
            song.CachedAlbumArtPath = LocalAlbumArt;
        else
            song.CachedPersonaImagePath = LocalPersonaImage;

        Assert.That(raised, Does.Contain(expectedNotification));
    }

    [Test]
    public void SettingSuppression_RaisesPropertyChangedForBothDisplaySources()
    {
        var song = CreateSong();
        var raised = new List<string?>();
        song.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        song.SuppressRemoteArtwork = true;

        Assert.Multiple(() =>
        {
            Assert.That(raised, Does.Contain(nameof(SongDto.AlbumArtDisplaySource)));
            Assert.That(raised, Does.Contain(nameof(SongDto.PersonaImageDisplaySource)));
        });
    }

    [Test]
    public void ArtworkResolutionProperties_AreExcludedFromSerialization()
    {
        // These are per-device paths; persisting them into the offline catalog would bloat it and
        // could resurrect a path that no longer exists.
        var song = CreateSong();
        song.CachedAlbumArtPath = LocalAlbumArt;
        song.CachedPersonaImagePath = LocalPersonaImage;
        song.SuppressRemoteArtwork = true;

        var json = JsonSerializer.Serialize(song, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Not.Contain("cachedAlbumArtPath"));
            Assert.That(json, Does.Not.Contain("cachedPersonaImagePath"));
            Assert.That(json, Does.Not.Contain("suppressRemoteArtwork"));
            Assert.That(json, Does.Not.Contain("albumArtDisplaySource"));
            Assert.That(json, Does.Contain("albumArtUrl"));
        });
    }
}
