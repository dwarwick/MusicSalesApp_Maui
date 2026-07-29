using Microsoft.Extensions.Logging.Abstractions;
using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Tests.Services;

[TestFixture]
public class OfflinePlaylistStoreTests
{
    private string _storeDirectory = string.Empty;
    private OfflinePlaylistStore _store = null!;

    [SetUp]
    public void SetUp()
    {
        _storeDirectory = Path.Combine(Path.GetTempPath(), "offline-playlist-tests", Guid.NewGuid().ToString("N"));
        _store = new OfflinePlaylistStore(_storeDirectory, NullLogger<OfflinePlaylistStore>.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_storeDirectory))
        {
            Directory.Delete(_storeDirectory, recursive: true);
        }
    }

    private string StoreFilePath => Path.Combine(_storeDirectory, "playlists-v1.json");

    private static PlaylistSongsDto CreatePlaylistSongs(int playlistId = 5) => new()
    {
        PlaylistId = playlistId,
        PlaylistName = "My Mix",
        IsSystemGenerated = false,
        Songs =
        [
            new PlaylistSongDto
            {
                Id = 1,
                SongMetadataId = 1,
                UserPlaylistId = 901,
                SongTitle = "Song One",
                ArtistName = "Artist",
                StreamUrl = "https://storage.test/songs/1.mp3",
                AlbumArtUrl = "https://storage.test/images/1.jpg"
            }
        ]
    };

    [Test]
    public async Task PlaylistSongs_RoundTripIncludingUserPlaylistId()
    {
        await _store.SavePlaylistSongsAsync(5, CreatePlaylistSongs());

        var restored = await _store.LoadPlaylistSongsAsync(5);

        Assert.That(restored, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(restored!.PlaylistName, Is.EqualTo("My Mix"));
            Assert.That(restored.IsSystemGenerated, Is.False);
            Assert.That(restored.Songs, Has.Count.EqualTo(1));
            Assert.That(restored.Songs[0].UserPlaylistId, Is.EqualTo(901));
            Assert.That(restored.Songs[0].StreamUrl, Is.EqualTo("https://storage.test/songs/1.mp3"));
        });
    }

    [Test]
    public async Task PlaylistSongs_AreKeyedByPlaylistId()
    {
        await _store.SavePlaylistSongsAsync(5, CreatePlaylistSongs(5));
        await _store.SavePlaylistSongsAsync(6, CreatePlaylistSongs(6));

        Assert.That((await _store.LoadPlaylistSongsAsync(5))!.PlaylistId, Is.EqualTo(5));
        Assert.That((await _store.LoadPlaylistSongsAsync(6))!.PlaylistId, Is.EqualTo(6));
        Assert.That(await _store.LoadPlaylistSongsAsync(7), Is.Null);
    }

    [Test]
    public async Task RecommendedSongs_AreStoredSeparatelyFromPlaylistsById()
    {
        // Recommended has no stable playlist id of its own, so it must not collide with a real one.
        await _store.SavePlaylistSongsAsync(5, CreatePlaylistSongs(5));
        await _store.SaveRecommendedSongsAsync(new PlaylistSongsDto { PlaylistName = "Recommended" });

        Assert.That((await _store.LoadRecommendedSongsAsync())!.PlaylistName, Is.EqualTo("Recommended"));
        Assert.That((await _store.LoadPlaylistSongsAsync(5))!.PlaylistName, Is.EqualTo("My Mix"));
    }

    [Test]
    public async Task HomePlaylists_RoundTrip()
    {
        await _store.SaveHomePlaylistsAsync(new HomePlaylistsDto
        {
            Recommended = new PlaylistDto { Id = 1, Name = "Recommended", Kind = PlaylistKinds.Recommended },
            LikedSongs = new PlaylistDto { Id = 2, Name = "Liked Songs", Kind = PlaylistKinds.LikedSongs }
        });

        var restored = await _store.LoadHomePlaylistsAsync();

        Assert.That(restored!.Recommended!.Name, Is.EqualTo("Recommended"));
        Assert.That(restored.LikedSongs!.Kind, Is.EqualTo(PlaylistKinds.LikedSongs));
    }

    [Test]
    public async Task MyPlaylists_RoundTrip()
    {
        await _store.SaveMyPlaylistsAsync([
            new PlaylistDto { Id = 1, Name = "Road Trip", SongCount = 12 },
            new PlaylistDto { Id = 2, Name = "Focus", SongCount = 3 }
        ]);

        var restored = await _store.LoadMyPlaylistsAsync();

        Assert.That(restored.Select(p => p.Name), Is.EqualTo(new[] { "Road Trip", "Focus" }));
        Assert.That(restored[0].SongCount, Is.EqualTo(12));
    }

    [Test]
    public async Task SectionsDoNotOverwriteEachOther()
    {
        // Each save is a read-modify-write of one shared document; a naive implementation would drop
        // whatever was written before it.
        await _store.SaveMyPlaylistsAsync([new PlaylistDto { Id = 1, Name = "Road Trip" }]);
        await _store.SaveHomePlaylistsAsync(new HomePlaylistsDto { LikedSongs = new PlaylistDto { Id = 2 } });
        await _store.SavePlaylistSongsAsync(5, CreatePlaylistSongs());
        await _store.SaveRecommendedSongsAsync(new PlaylistSongsDto { PlaylistName = "Recommended" });

        Assert.Multiple(async () =>
        {
            Assert.That(await _store.LoadMyPlaylistsAsync(), Has.Count.EqualTo(1));
            Assert.That((await _store.LoadHomePlaylistsAsync())!.LikedSongs, Is.Not.Null);
            Assert.That(await _store.LoadPlaylistSongsAsync(5), Is.Not.Null);
            Assert.That(await _store.LoadRecommendedSongsAsync(), Is.Not.Null);
        });
    }

    [Test]
    public async Task LoadingBeforeAnySave_ReturnsEmptyOrNull()
    {
        Assert.Multiple(async () =>
        {
            Assert.That(await _store.LoadHomePlaylistsAsync(), Is.Null);
            Assert.That(await _store.LoadMyPlaylistsAsync(), Is.Empty);
            Assert.That(await _store.LoadPlaylistSongsAsync(5), Is.Null);
            Assert.That(await _store.LoadRecommendedSongsAsync(), Is.Null);
        });
    }

    [Test]
    public async Task CorruptFile_SelfHealsByClearingIt()
    {
        Directory.CreateDirectory(_storeDirectory);
        await File.WriteAllTextAsync(StoreFilePath, "{ not json at all");

        Assert.That(await _store.LoadMyPlaylistsAsync(), Is.Empty);
        Assert.That(File.Exists(StoreFilePath), Is.False);
    }

    [Test]
    public async Task ClearAsync_RemovesEverything()
    {
        await _store.SaveMyPlaylistsAsync([new PlaylistDto { Id = 1 }]);

        await _store.ClearAsync();

        Assert.That(await _store.LoadMyPlaylistsAsync(), Is.Empty);
        Assert.That(File.Exists(StoreFilePath), Is.False);
    }

    [Test]
    public async Task MyPlaylists_AreCappedToTheEntryLimit()
    {
        var playlists = Enumerable.Range(1, OfflinePlaylistStore.MaxCachedPlaylists + 10)
            .Select(id => new PlaylistDto { Id = id, Name = $"Playlist {id}" })
            .ToList();

        await _store.SaveMyPlaylistsAsync(playlists);

        Assert.That(await _store.LoadMyPlaylistsAsync(),
            Has.Count.EqualTo(OfflinePlaylistStore.MaxCachedPlaylists));
    }

    [Test]
    public async Task ConcurrentSaves_LeaveAValidFile()
    {
        await Task.WhenAll(Enumerable.Range(0, 10).Select(i =>
            _store.SavePlaylistSongsAsync(i, CreatePlaylistSongs(i))));

        Assert.That(await _store.LoadPlaylistSongsAsync(9), Is.Not.Null);
        Assert.That(File.Exists(StoreFilePath + ".tmp"), Is.False);
    }
}
