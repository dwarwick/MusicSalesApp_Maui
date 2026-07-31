using Microsoft.Extensions.Logging.Abstractions;
using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Tests.Services;

/// <summary>
/// Adding the rendition URLs to <see cref="SongDto"/> must not disturb the offline catalog written
/// by an earlier build. The file version is deliberately <em>not</em> bumped: the new properties are
/// purely additive, and bumping it would discard every user's offline library on upgrade for no
/// benefit whatsoever.
/// </summary>
[TestFixture]
public class OfflineSongCatalogStoreUpgradeTests
{
    private string _catalogDirectory = string.Empty;
    private OfflineSongCatalogStore _store = null!;

    [SetUp]
    public void SetUp()
    {
        _catalogDirectory = Path.Combine(
            Path.GetTempPath(), "offline-catalog-upgrade-tests", Guid.NewGuid().ToString("N"));
        _store = new OfflineSongCatalogStore(_catalogDirectory, NullLogger<OfflineSongCatalogStore>.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_catalogDirectory))
        {
            Directory.Delete(_catalogDirectory, recursive: true);
        }
    }

    private string CatalogFilePath => Path.Combine(_catalogDirectory, "song-catalog-v1.json");

    /// <summary>
    /// A snapshot exactly as the previous app version wrote it: no rendition properties anywhere.
    /// </summary>
    private async Task WritePreviousVersionSnapshotAsync()
    {
        Directory.CreateDirectory(_catalogDirectory);
        await File.WriteAllTextAsync(CatalogFilePath, """
        {
          "version": 1,
          "updatedUtc": "2026-07-01T12:00:00+00:00",
          "songs": [
            {
              "stableCacheKey": "song-42",
              "song": {
                "id": 42,
                "songTitle": "Night Drive",
                "artistName": "Nova",
                "genre": "Synthwave",
                "albumArtUrl": "https://storage.test/images/42.jpg?sig=aaa",
                "personaImageUrl": "https://storage.test/personas/42.jpg?sig=bbb",
                "personaBio": "Bio",
                "streamUrl": "https://storage.test/songs/42.mp3?sig=ccc",
                "streamQualifyingSeconds": 30,
                "trackLengthSeconds": 180.5,
                "displayOnHomePage": true,
                "displayOrder": 3,
                "isAiGenerated": true,
                "isAiVocals": false,
                "isAiLyrics": true,
                "creatorId": 9,
                "creatorUserId": 11,
                "streamCount": 100,
                "likeCount": 7,
                "dislikeCount": 2,
                "userLikeStatus": true
              }
            }
          ]
        }
        """);
    }

    [Test]
    public async Task ASnapshotFromThePreviousVersionStillLoads()
    {
        await WritePreviousVersionSnapshotAsync();

        var songs = await _store.LoadAsync();

        Assert.Multiple(() =>
        {
            Assert.That(songs, Has.Count.EqualTo(1));
            Assert.That(songs[0].SongTitle, Is.EqualTo("Night Drive"));
            Assert.That(songs[0].AlbumArtUrl, Is.EqualTo("https://storage.test/images/42.jpg?sig=aaa"));
        });
    }

    [Test]
    public async Task ASnapshotFromThePreviousVersionIsNotDeleted()
    {
        // A JSON parse failure deletes the file and returns an empty catalog. Additive properties
        // cannot cause one, and this is what proves it: the user keeps their offline library.
        await WritePreviousVersionSnapshotAsync();

        await _store.LoadAsync();

        Assert.That(File.Exists(CatalogFilePath), Is.True);
    }

    [Test]
    public async Task MissingRenditionUrlsLoadAsNullAndFallBackToTheOriginals()
    {
        await WritePreviousVersionSnapshotAsync();

        var song = (await _store.LoadAsync()).Single();

        Assert.Multiple(() =>
        {
            Assert.That(song.AlbumArtThumbUrl, Is.Null);
            Assert.That(song.AlbumArtHeroUrl, Is.Null);
            Assert.That(song.PersonaImageThumbUrl, Is.Null);
            // And the display chains therefore behave exactly as the previous build did.
            Assert.That(song.AlbumArtThumbDisplaySource, Is.EqualTo(song.AlbumArtUrl));
            Assert.That(song.AlbumArtHeroDisplaySource, Is.EqualTo(song.AlbumArtUrl));
            Assert.That(song.PersonaImageThumbDisplaySource, Is.EqualTo(song.PersonaImageUrl));
        });
    }

    [Test]
    public async Task RenditionUrlsSurviveARoundTrip()
    {
        var song = new SongDto
        {
            Id = 1,
            SongTitle = "Night Drive",
            StreamUrl = "https://storage.test/songs/1.mp3",
            AlbumArtUrl = "https://cdn/cover.jpg",
            AlbumArtThumbUrl = "https://cdn/cover.jpg.w320.webp",
            AlbumArtHeroUrl = "https://cdn/cover.jpg.w640.webp",
            PersonaImageUrl = "https://cdn/persona.png",
            PersonaImageThumbUrl = "https://cdn/persona.png.w320.webp"
        };

        await _store.SaveAsync([song]);
        var restored = (await _store.LoadAsync()).Single();

        Assert.Multiple(() =>
        {
            Assert.That(restored.AlbumArtThumbUrl, Is.EqualTo(song.AlbumArtThumbUrl));
            Assert.That(restored.AlbumArtHeroUrl, Is.EqualTo(song.AlbumArtHeroUrl));
            Assert.That(restored.PersonaImageThumbUrl, Is.EqualTo(song.PersonaImageThumbUrl));
        });
    }

    [Test]
    public async Task DeviceSpecificCachePathsAreNotPersisted()
    {
        // Cache paths are per-device and per-install; writing them into a portable snapshot would
        // point a restored catalog at files that may no longer exist.
        var song = new SongDto
        {
            Id = 1,
            SongTitle = "Night Drive",
            StreamUrl = "https://storage.test/songs/1.mp3",
            AlbumArtUrl = "https://cdn/cover.jpg",
            CachedAlbumArtThumbPath = "/local/thumb.webp",
            CachedAlbumArtHeroPath = "/local/hero.webp",
            CachedPersonaImageThumbPath = "/local/persona.webp"
        };

        await _store.SaveAsync([song]);
        var json = await File.ReadAllTextAsync(CatalogFilePath);

        Assert.That(json, Does.Not.Contain("/local/"));
    }
}
