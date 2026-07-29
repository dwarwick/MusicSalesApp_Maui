using Microsoft.Extensions.Logging.Abstractions;
using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Tests.Services;

[TestFixture]
public class OfflineSongCatalogStoreTests
{
    private string _catalogDirectory = string.Empty;
    private OfflineSongCatalogStore _store = null!;

    [SetUp]
    public void SetUp()
    {
        _catalogDirectory = Path.Combine(Path.GetTempPath(), "offline-catalog-tests", Guid.NewGuid().ToString("N"));
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

    private static SongDto CreateSong(int id = 42) => new()
    {
        Id = id,
        SongTitle = $"Song {id}",
        ArtistName = "Test Artist",
        Genre = "Rock",
        AlbumArtUrl = $"https://storage.blob.core.windows.net/images/{id}.jpg?sig=aaa",
        PersonaImageUrl = $"https://storage.blob.core.windows.net/personas/{id}.jpg?sig=bbb",
        PersonaBio = "Bio",
        StreamUrl = $"https://storage.blob.core.windows.net/songs/{id}.mp3?sig=ccc",
        StreamQualifyingSeconds = 30,
        TrackLengthSeconds = 180.5,
        DisplayOnHomePage = true,
        DisplayOrder = 3,
        IsAiGenerated = true,
        IsAiVocals = false,
        IsAiLyrics = true,
        CreatorId = 9,
        CreatorUserId = 11,
        StreamCount = 100,
        LikeCount = 7,
        DislikeCount = 2,
        UserLikeStatus = true
    };

    [Test]
    public async Task SaveThenLoad_RoundTripsEveryField()
    {
        var song = CreateSong();

        await _store.SaveAsync([song]);
        var loaded = await _store.LoadAsync();

        Assert.That(loaded, Has.Count.EqualTo(1));
        var restored = loaded[0];
        Assert.Multiple(() =>
        {
            Assert.That(restored.Id, Is.EqualTo(song.Id));
            Assert.That(restored.SongTitle, Is.EqualTo(song.SongTitle));
            Assert.That(restored.ArtistName, Is.EqualTo(song.ArtistName));
            Assert.That(restored.Genre, Is.EqualTo(song.Genre));
            Assert.That(restored.AlbumArtUrl, Is.EqualTo(song.AlbumArtUrl));
            Assert.That(restored.PersonaImageUrl, Is.EqualTo(song.PersonaImageUrl));
            Assert.That(restored.PersonaBio, Is.EqualTo(song.PersonaBio));
            Assert.That(restored.StreamUrl, Is.EqualTo(song.StreamUrl));
            Assert.That(restored.StreamQualifyingSeconds, Is.EqualTo(song.StreamQualifyingSeconds));
            Assert.That(restored.TrackLengthSeconds, Is.EqualTo(song.TrackLengthSeconds));
            Assert.That(restored.DisplayOnHomePage, Is.EqualTo(song.DisplayOnHomePage));
            Assert.That(restored.DisplayOrder, Is.EqualTo(song.DisplayOrder));
            Assert.That(restored.IsAiGenerated, Is.EqualTo(song.IsAiGenerated));
            Assert.That(restored.IsAiLyrics, Is.EqualTo(song.IsAiLyrics));
            Assert.That(restored.CreatorId, Is.EqualTo(song.CreatorId));
            Assert.That(restored.CreatorUserId, Is.EqualTo(song.CreatorUserId));
        });
    }

    [Test]
    public async Task SaveThenLoad_RoundTripsObservableProperties()
    {
        // The offline library shows last-known like counts, so the source-generated ObservableProperty
        // members have to survive serialization like any other public property.
        await _store.SaveAsync([CreateSong()]);

        var restored = (await _store.LoadAsync())[0];

        Assert.Multiple(() =>
        {
            Assert.That(restored.StreamCount, Is.EqualTo(100));
            Assert.That(restored.LikeCount, Is.EqualTo(7));
            Assert.That(restored.DislikeCount, Is.EqualTo(2));
            Assert.That(restored.UserLikeStatus, Is.True);
        });
    }

    [Test]
    public async Task LoadAsync_WithNoStoredCatalog_ReturnsEmpty()
    {
        Assert.That(await _store.LoadAsync(), Is.Empty);
    }

    [Test]
    public async Task SaveAsync_WithEmptyList_DoesNotOverwriteAGoodSnapshot()
    {
        // A transient failure surfacing as "no songs" must never be able to wipe the offline library.
        await _store.SaveAsync([CreateSong()]);

        await _store.SaveAsync([]);

        Assert.That(await _store.LoadAsync(), Has.Count.EqualTo(1));
    }

    [Test]
    public async Task SaveAsync_ReplacesThePreviousSnapshotEntirely()
    {
        await _store.SaveAsync([CreateSong(1), CreateSong(2), CreateSong(3)]);

        await _store.SaveAsync([CreateSong(2)]);

        var loaded = await _store.LoadAsync();
        Assert.That(loaded.Select(s => s.Id), Is.EquivalentTo(new[] { 2 }));
    }

    [Test]
    public async Task SaveAsync_StoresTheStableCacheKeyAlongsideEachSong()
    {
        var song = CreateSong();

        await _store.SaveAsync([song]);

        var json = await File.ReadAllTextAsync(CatalogFilePath);
        Assert.That(json, Does.Contain(AudioCacheKeyHelper.GetStableCacheKey(song)));
    }

    [Test]
    public async Task LoadAsync_WithCorruptJson_SelfHealsByClearingTheFile()
    {
        Directory.CreateDirectory(_catalogDirectory);
        await File.WriteAllTextAsync(CatalogFilePath, "{ this is not json");

        var loaded = await _store.LoadAsync();

        Assert.That(loaded, Is.Empty);
        Assert.That(File.Exists(CatalogFilePath), Is.False);
    }

    [Test]
    public async Task SaveAsync_EnforcesTheEntryCap()
    {
        var songs = Enumerable.Range(1, OfflineSongCatalogStore.MaxCatalogEntries + 25)
            .Select(CreateSong)
            .ToList();

        await _store.SaveAsync(songs);

        Assert.That(await _store.LoadAsync(), Has.Count.EqualTo(OfflineSongCatalogStore.MaxCatalogEntries));
    }

    [Test]
    public async Task GetLastUpdatedUtcAsync_ReturnsNullBeforeAnySave()
    {
        Assert.That(await _store.GetLastUpdatedUtcAsync(), Is.Null);
    }

    [Test]
    public async Task GetLastUpdatedUtcAsync_ReturnsTheSaveTimestamp()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        await _store.SaveAsync([CreateSong()]);

        Assert.That(await _store.GetLastUpdatedUtcAsync(), Is.GreaterThanOrEqualTo(before));
    }

    [Test]
    public async Task ClearAsync_RemovesTheStoredCatalog()
    {
        await _store.SaveAsync([CreateSong()]);

        await _store.ClearAsync();

        Assert.That(await _store.LoadAsync(), Is.Empty);
        Assert.That(File.Exists(CatalogFilePath), Is.False);
    }

    [Test]
    public async Task ClearUserLikeStatesAsync_ForgetsTheVotesButKeepsTheCatalog()
    {
        // Logout: the songs are public, only the opinion on them is personal - and dropping the catalog
        // would take offline playback away with it.
        var liked = CreateSong(1);
        liked.UserLikeStatus = true;
        var disliked = CreateSong(2);
        disliked.UserLikeStatus = false;
        await _store.SaveAsync([liked, disliked]);

        await _store.ClearUserLikeStatesAsync();

        var restored = await _store.LoadAsync();
        Assert.Multiple(() =>
        {
            Assert.That(restored, Has.Count.EqualTo(2));
            Assert.That(restored.Select(song => song.UserLikeStatus), Is.All.Null);
        });
    }

    [Test]
    public async Task ClearUserLikeStatesAsync_KeepsTheLikeCounts()
    {
        // The counts are public totals, not the user's own vote, and are what the offline library shows.
        var song = CreateSong(1);
        song.UserLikeStatus = true;
        song.LikeCount = 12;
        song.DislikeCount = 3;
        await _store.SaveAsync([song]);

        await _store.ClearUserLikeStatesAsync();

        var restored = (await _store.LoadAsync()).Single();
        Assert.Multiple(() =>
        {
            Assert.That(restored.LikeCount, Is.EqualTo(12));
            Assert.That(restored.DislikeCount, Is.EqualTo(3));
        });
    }

    [Test]
    public void ClearUserLikeStatesAsync_WithNoStoredCatalog_DoesNotThrow()
        => Assert.That(async () => await _store.ClearUserLikeStatesAsync(), Throws.Nothing);

    [Test]
    public async Task ConcurrentSaves_LeaveAValidCatalog()
    {
        await Task.WhenAll(Enumerable.Range(0, 12)
            .Select(i => _store.SaveAsync([CreateSong(i + 1), CreateSong(i + 100)])));

        // Whichever writer landed last, the file must be parseable and complete - never half-written.
        Assert.That(await _store.LoadAsync(), Has.Count.EqualTo(2));
        Assert.That(File.Exists(CatalogFilePath + ".tmp"), Is.False);
    }
}
