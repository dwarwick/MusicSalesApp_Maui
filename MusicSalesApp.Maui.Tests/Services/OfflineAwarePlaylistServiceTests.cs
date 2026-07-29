using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Maui.Networking;
using Moq;
using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Tests.Services;

[TestFixture]
public class OfflineAwarePlaylistServiceTests
{
    private Mock<IPlaylistService> _inner = null!;
    private Mock<IOfflinePlaylistStore> _store = null!;
    private Mock<ITrackCacheService> _trackCache = null!;
    private TestConnectivity _connectivity = null!;
    private OfflineAwarePlaylistService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _inner = new Mock<IPlaylistService>();
        _store = new Mock<IOfflinePlaylistStore>();
        _trackCache = new Mock<ITrackCacheService>();
        _connectivity = new TestConnectivity();

        _store.Setup(s => s.LoadMyPlaylistsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);

        _service = new OfflineAwarePlaylistService(
            _inner.Object,
            _store.Object,
            _trackCache.Object,
            _connectivity,
            NullLogger<OfflineAwarePlaylistService>.Instance);
    }

    private static PlaylistSongDto CreatePlaylistSong(int songMetadataId) => new()
    {
        Id = songMetadataId,
        SongMetadataId = songMetadataId,
        UserPlaylistId = 900 + songMetadataId,
        SongTitle = $"Song {songMetadataId}",
        StreamUrl = $"https://storage.test/songs/{songMetadataId}.mp3"
    };

    private static PlaylistSongsDto CreatePlaylistSongs(params int[] songIds) => new()
    {
        PlaylistId = 5,
        PlaylistName = "My Mix",
        IsSystemGenerated = false,
        Songs = songIds.Select(CreatePlaylistSong).ToList()
    };

    private void GivenLocallyReady(params int[] readySongIds)
        => _trackCache
            .Setup(s => s.GetCacheStatusesAsync(It.IsAny<IReadOnlyList<SongDto>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<SongDto> songs, CancellationToken _) => songs.ToDictionary(
                song => song.Id,
                song => new TrackCacheStatus(
                    song.Id,
                    $"song-{song.Id}",
                    readySongIds.Contains(song.Id) ? $"/cache/{song.Id}.mp3" : null,
                    readySongIds.Contains(song.Id),
                    false)));

    // --- Playlist songs by id ---

    [Test]
    public async Task GetPlaylistSongsAsync_LiveSuccess_SnapshotsAndReportsLive()
    {
        var live = CreatePlaylistSongs(1, 2);
        _inner.Setup(s => s.GetPlaylistSongsAsync(5)).ReturnsAsync(live);

        var result = await _service.GetPlaylistSongsAsync(5);

        Assert.That(result, Is.EqualTo(live));
        Assert.That(_service.LastPlaylistSource, Is.EqualTo(PlaylistDataSource.Live));
        _store.Verify(s => s.SavePlaylistSongsAsync(5, live, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task GetPlaylistSongsAsync_LiveFailure_RestoresOnlyDownloadedSongs()
    {
        _inner.Setup(s => s.GetPlaylistSongsAsync(5)).ReturnsAsync((PlaylistSongsDto?)null);
        _store.Setup(s => s.LoadPlaylistSongsAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreatePlaylistSongs(1, 2, 3));
        GivenLocallyReady(1, 3);

        var result = await _service.GetPlaylistSongsAsync(5);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Songs.Select(s => s.SongMetadataId), Is.EqualTo(new[] { 1, 3 }));
        Assert.That(_service.LastPlaylistSource, Is.EqualTo(PlaylistDataSource.OfflineCache));
    }

    [Test]
    public async Task GetPlaylistSongsAsync_RestoredPlaylist_KeepsItsNameAndUserPlaylistIds()
    {
        // UserPlaylistId is what remove/reorder key on; losing it would silently break both.
        _inner.Setup(s => s.GetPlaylistSongsAsync(5)).ReturnsAsync((PlaylistSongsDto?)null);
        _store.Setup(s => s.LoadPlaylistSongsAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreatePlaylistSongs(1));
        GivenLocallyReady(1);

        var result = await _service.GetPlaylistSongsAsync(5);

        Assert.Multiple(() =>
        {
            Assert.That(result!.PlaylistName, Is.EqualTo("My Mix"));
            Assert.That(result.PlaylistId, Is.EqualTo(5));
            Assert.That(result.IsSystemGenerated, Is.False);
            Assert.That(result.Songs[0].UserPlaylistId, Is.EqualTo(901));
        });
    }

    [Test]
    public async Task GetPlaylistSongsAsync_NoSongsDownloaded_ReportsUnavailable()
    {
        _inner.Setup(s => s.GetPlaylistSongsAsync(5)).ReturnsAsync((PlaylistSongsDto?)null);
        _store.Setup(s => s.LoadPlaylistSongsAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreatePlaylistSongs(1, 2));
        GivenLocallyReady();

        var result = await _service.GetPlaylistSongsAsync(5);

        Assert.That(result, Is.Null);
        Assert.That(_service.LastPlaylistSource, Is.EqualTo(PlaylistDataSource.Unavailable));
    }

    [Test]
    public async Task GetPlaylistSongsAsync_NoNetwork_SkipsTheLiveCall()
    {
        _connectivity.NetworkAccess = NetworkAccess.None;
        _store.Setup(s => s.LoadPlaylistSongsAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreatePlaylistSongs(1));
        GivenLocallyReady(1);

        await _service.GetPlaylistSongsAsync(5);

        _inner.Verify(s => s.GetPlaylistSongsAsync(It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task GetPlaylistSongsAsync_NothingSnapshotted_ReportsUnavailable()
    {
        _inner.Setup(s => s.GetPlaylistSongsAsync(5)).ReturnsAsync((PlaylistSongsDto?)null);
        _store.Setup(s => s.LoadPlaylistSongsAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlaylistSongsDto?)null);

        Assert.That(await _service.GetPlaylistSongsAsync(5), Is.Null);
        Assert.That(_service.LastPlaylistSource, Is.EqualTo(PlaylistDataSource.Unavailable));
    }

    // --- Recommended ---

    [Test]
    public async Task GetRecommendedSongsAsync_LiveSuccess_Snapshots()
    {
        var live = CreatePlaylistSongs(1);
        _inner.Setup(s => s.GetRecommendedSongsAsync()).ReturnsAsync(live);

        await _service.GetRecommendedSongsAsync();

        _store.Verify(s => s.SaveRecommendedSongsAsync(live, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task GetRecommendedSongsAsync_LiveFailure_RestoresOnlyDownloadedSongs()
    {
        _inner.Setup(s => s.GetRecommendedSongsAsync()).ReturnsAsync((PlaylistSongsDto?)null);
        _store.Setup(s => s.LoadRecommendedSongsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreatePlaylistSongs(1, 2));
        GivenLocallyReady(2);

        var result = await _service.GetRecommendedSongsAsync();

        Assert.That(result!.Songs.Select(s => s.SongMetadataId), Is.EqualTo(new[] { 2 }));
        Assert.That(_service.LastPlaylistSource, Is.EqualTo(PlaylistDataSource.OfflineCache));
    }

    // --- Home playlists ---

    [Test]
    public async Task GetHomePlaylistsAsync_LiveSuccess_Snapshots()
    {
        var live = new HomePlaylistsDto { Recommended = new PlaylistDto { Id = 1, Name = "Recommended" } };
        _inner.Setup(s => s.GetHomePlaylistsAsync()).ReturnsAsync(live);

        await _service.GetHomePlaylistsAsync();

        _store.Verify(s => s.SaveHomePlaylistsAsync(live, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task GetHomePlaylistsAsync_LiveFailure_RestoresTheSnapshot()
    {
        var cached = new HomePlaylistsDto { LikedSongs = new PlaylistDto { Id = 2, Name = "Liked Songs" } };
        _inner.Setup(s => s.GetHomePlaylistsAsync()).ReturnsAsync((HomePlaylistsDto?)null);
        _store.Setup(s => s.LoadHomePlaylistsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(cached);

        Assert.That(await _service.GetHomePlaylistsAsync(), Is.EqualTo(cached));
        Assert.That(_service.LastPlaylistSource, Is.EqualTo(PlaylistDataSource.OfflineCache));
    }

    // --- My playlists ---

    [Test]
    public async Task GetMyPlaylistsAsync_LiveSuccess_Snapshots()
    {
        var live = new List<PlaylistDto> { new() { Id = 1, Name = "Road Trip" } };
        _inner.Setup(s => s.GetMyPlaylistsAsync()).ReturnsAsync(live);

        Assert.That(await _service.GetMyPlaylistsAsync(), Is.EqualTo(live));
        _store.Verify(s => s.SaveMyPlaylistsAsync(live, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task GetMyPlaylistsAsync_EmptyWhileOnline_IsTrustedAsGenuinelyEmpty()
    {
        // PlaylistService returns [] for both "no playlists" and "request failed", so an empty result is
        // only believed when connectivity is definitely up.
        _inner.Setup(s => s.GetMyPlaylistsAsync()).ReturnsAsync([]);
        _store.Setup(s => s.LoadMyPlaylistsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PlaylistDto { Id = 1 }]);

        var result = await _service.GetMyPlaylistsAsync();

        Assert.That(result, Is.Empty);
        Assert.That(_service.LastPlaylistSource, Is.EqualTo(PlaylistDataSource.Live));
    }

    [Test]
    public async Task GetMyPlaylistsAsync_EmptyWithAmbiguousConnectivity_FallsBackToTheSnapshot()
    {
        _connectivity.NetworkAccess = NetworkAccess.Unknown;
        _inner.Setup(s => s.GetMyPlaylistsAsync()).ReturnsAsync([]);
        _store.Setup(s => s.LoadMyPlaylistsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PlaylistDto { Id = 1, Name = "Road Trip" }]);

        var result = await _service.GetMyPlaylistsAsync();

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(_service.LastPlaylistSource, Is.EqualTo(PlaylistDataSource.OfflineCache));
    }

    [Test]
    public async Task GetMyPlaylistsAsync_NoNetworkAndNothingSnapshotted_ReportsUnavailable()
    {
        _connectivity.NetworkAccess = NetworkAccess.None;

        Assert.That(await _service.GetMyPlaylistsAsync(), Is.Empty);
        Assert.That(_service.LastPlaylistSource, Is.EqualTo(PlaylistDataSource.Unavailable));
    }

    // --- Snapshot resilience ---

    [Test]
    public async Task SnapshotFailure_DoesNotBreakALiveRead()
    {
        var live = CreatePlaylistSongs(1);
        _inner.Setup(s => s.GetPlaylistSongsAsync(5)).ReturnsAsync(live);
        _store.Setup(s => s.SavePlaylistSongsAsync(5, live, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("disk full"));

        Assert.That(await _service.GetPlaylistSongsAsync(5), Is.EqualTo(live));
    }

    [Test]
    public async Task SnapshotReadFailure_DegradesToUnavailable()
    {
        _inner.Setup(s => s.GetPlaylistSongsAsync(5)).ReturnsAsync((PlaylistSongsDto?)null);
        _store.Setup(s => s.LoadPlaylistSongsAsync(5, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("unreadable"));

        Assert.That(await _service.GetPlaylistSongsAsync(5), Is.Null);
        Assert.That(_service.LastPlaylistSource, Is.EqualTo(PlaylistDataSource.Unavailable));
    }

    // --- Writes ---

    [Test]
    public async Task Writes_WhileOffline_FailWithAClearMessageAndNeverReachTheServer()
    {
        _connectivity.NetworkAccess = NetworkAccess.None;

        var results = new[]
        {
            (PlaylistOperationResult)await _service.CreatePlaylistAsync("New"),
            await _service.RenamePlaylistAsync(1, "Renamed"),
            await _service.DeletePlaylistAsync(1),
            await _service.AddSongAsync(1, 2),
            await _service.RemoveSongAsync(1, 2),
            await _service.ReorderAsync(1, [1, 2])
        };

        Assert.That(results.All(r => !r.Success), Is.True);
        Assert.That(results.All(r => r.ErrorMessage == OfflineAwarePlaylistService.OfflineEditMessage), Is.True);
        _inner.Verify(s => s.CreatePlaylistAsync(It.IsAny<string>()), Times.Never);
        _inner.Verify(s => s.RenamePlaylistAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
        _inner.Verify(s => s.DeletePlaylistAsync(It.IsAny<int>()), Times.Never);
        _inner.Verify(s => s.AddSongAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        _inner.Verify(s => s.RemoveSongAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        _inner.Verify(s => s.ReorderAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<int>>()), Times.Never);
    }

    [Test]
    public async Task Writes_WhileOnline_PassThroughUnchanged()
    {
        await _service.CreatePlaylistAsync("New");
        await _service.RenamePlaylistAsync(1, "Renamed");
        await _service.DeletePlaylistAsync(1);
        await _service.AddSongAsync(1, 2);
        await _service.RemoveSongAsync(1, 2);
        await _service.ReorderAsync(1, [1, 2]);
        await _service.GetAvailableSongsAsync(1);

        _inner.Verify(s => s.CreatePlaylistAsync("New"), Times.Once);
        _inner.Verify(s => s.RenamePlaylistAsync(1, "Renamed"), Times.Once);
        _inner.Verify(s => s.DeletePlaylistAsync(1), Times.Once);
        _inner.Verify(s => s.AddSongAsync(1, 2), Times.Once);
        _inner.Verify(s => s.RemoveSongAsync(1, 2), Times.Once);
        _inner.Verify(s => s.ReorderAsync(1, It.IsAny<IReadOnlyList<int>>()), Times.Once);
        _inner.Verify(s => s.GetAvailableSongsAsync(1), Times.Once);
    }
}
