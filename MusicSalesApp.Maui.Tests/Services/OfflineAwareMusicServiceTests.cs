using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Maui.Networking;
using Moq;
using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Tests.Services;

[TestFixture]
public class OfflineAwareMusicServiceTests
{
    private const string LiveError = "Unable to load data from https://example.test/api/music/songs: boom";

    private Mock<IMusicService> _inner = null!;
    private Mock<IOfflineSongCatalogStore> _catalogStore = null!;
    private Mock<ITrackCacheService> _trackCache = null!;
    private TestConnectivity _connectivity = null!;
    private OfflineAwareMusicService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _inner = new Mock<IMusicService>();
        _catalogStore = new Mock<IOfflineSongCatalogStore>();
        _trackCache = new Mock<ITrackCacheService>();
        _connectivity = new TestConnectivity();

        _catalogStore.Setup(s => s.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _service = new OfflineAwareMusicService(
            _inner.Object,
            _catalogStore.Object,
            _trackCache.Object,
            _connectivity,
            NullLogger<OfflineAwareMusicService>.Instance);
    }

    private static SongDto CreateSong(int id) => new()
    {
        Id = id,
        SongTitle = $"Song {id}",
        StreamUrl = $"https://storage.test/songs/{id}.mp3"
    };

    private void GivenLiveResult(List<SongDto> songs, string? error = null)
    {
        _inner.Setup(s => s.GetSongsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(songs);
        _inner.SetupGet(s => s.LastSongsError).Returns(error);
    }

    private void GivenCachedCatalog(IEnumerable<SongDto> stored, params int[] locallyReadyIds)
    {
        var storedSongs = stored.ToList();
        _catalogStore.Setup(s => s.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(storedSongs);
        _trackCache.Setup(s => s.GetCacheStatusesAsync(It.IsAny<IReadOnlyList<SongDto>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(storedSongs.ToDictionary(
                song => song.Id,
                song => new TrackCacheStatus(
                    song.Id,
                    $"song-{song.Id}",
                    locallyReadyIds.Contains(song.Id) ? $"/cache/{song.Id}.mp3" : null,
                    locallyReadyIds.Contains(song.Id),
                    false)));
    }

    // --- Live path ---

    [Test]
    public async Task GetSongsAsync_LiveSuccess_ReturnsLiveSongsAndSnapshotsThem()
    {
        var songs = new List<SongDto> { CreateSong(1), CreateSong(2) };
        GivenLiveResult(songs);

        var result = await _service.GetSongsAsync();

        Assert.That(result, Is.EqualTo(songs));
        Assert.That(_service.LastSongsSource, Is.EqualTo(SongCatalogSource.Live));
        Assert.That(_service.LastSongsError, Is.Null);
        _catalogStore.Verify(s => s.SaveAsync(songs, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task GetSongsAsync_LiveSuccessWithNoSongs_DoesNotSnapshotOrFallBack()
    {
        // A genuinely empty catalog is not a failure, and must never overwrite a good snapshot.
        GivenLiveResult([]);
        GivenCachedCatalog([CreateSong(9)], 9);

        var result = await _service.GetSongsAsync();

        Assert.That(result, Is.Empty);
        Assert.That(_service.LastSongsSource, Is.EqualTo(SongCatalogSource.Live));
        _catalogStore.Verify(s => s.SaveAsync(It.IsAny<IReadOnlyList<SongDto>>(), It.IsAny<CancellationToken>()), Times.Never);
        _catalogStore.Verify(s => s.LoadAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task GetSongsAsync_SnapshotFailure_StillReturnsTheLiveResult()
    {
        var songs = new List<SongDto> { CreateSong(1) };
        GivenLiveResult(songs);
        _catalogStore.Setup(s => s.SaveAsync(It.IsAny<IReadOnlyList<SongDto>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("disk full"));

        var result = await _service.GetSongsAsync();

        Assert.That(result, Is.EqualTo(songs));
        Assert.That(_service.LastSongsSource, Is.EqualTo(SongCatalogSource.Live));
    }

    // --- The result carries its own source ---

    [Test]
    public async Task GetSongsAsync_TagsTheReturnedListWithItsOwnSourceAndError()
    {
        // LastSongsSource/LastSongsError are shared by every caller, and the library, home and the
        // playlist player all reload on the same connectivity change. Reading the source off the list
        // is what stops one of them acting on another's load.
        GivenLiveResult([], LiveError);
        GivenCachedCatalog([CreateSong(1)], locallyReadyIds: [1]);

        var result = await _service.GetSongsAsync();

        var outcome = SongCatalogOutcome.For(result, _inner.Object);
        Assert.Multiple(() =>
        {
            Assert.That(outcome.Source, Is.EqualTo(SongCatalogSource.OfflineCache));
            Assert.That(outcome.Error, Is.Null);
        });
    }

    [Test]
    public async Task GetSongsAsync_TaggedSourceSurvivesALaterLoadOverwritingTheSharedState()
    {
        // The exact race: this caller's await has completed, then a second caller's load lands and
        // rewrites the shared properties before the first one gets to read them.
        GivenLiveResult([], LiveError);
        GivenCachedCatalog([CreateSong(1)], locallyReadyIds: [1]);
        var offlineResult = await _service.GetSongsAsync();

        GivenLiveResult([CreateSong(1), CreateSong(2)]);
        await _service.GetSongsAsync();

        Assert.Multiple(() =>
        {
            Assert.That(
                SongCatalogOutcome.For(offlineResult, _service).Source,
                Is.EqualTo(SongCatalogSource.OfflineCache),
                "the first caller's result must still describe its own load");
            Assert.That(_service.LastSongsSource, Is.EqualTo(SongCatalogSource.Live));
        });
    }

    [Test]
    public void SongCatalogOutcome_ForAnUntaggedList_FallsBackToTheServiceProperties()
    {
        // Keeps the undecorated MusicService and loose test doubles behaving exactly as before.
        _inner.SetupGet(s => s.LastSongsSource).Returns(SongCatalogSource.Unavailable);
        _inner.SetupGet(s => s.LastSongsError).Returns(LiveError);

        var outcome = SongCatalogOutcome.For([CreateSong(1)], _inner.Object);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Source, Is.EqualTo(SongCatalogSource.Unavailable));
            Assert.That(outcome.Error, Is.EqualTo(LiveError));
        });
    }

    // --- Offline fallback ---

    [Test]
    public async Task GetSongsAsync_LiveFailure_ReturnsOnlyLocallyCachedSongs()
    {
        GivenLiveResult([], LiveError);
        GivenCachedCatalog([CreateSong(1), CreateSong(2), CreateSong(3)], locallyReadyIds: [1, 3]);

        var result = await _service.GetSongsAsync();

        Assert.That(result.Select(s => s.Id), Is.EqualTo(new[] { 1, 3 }));
        Assert.That(_service.LastSongsSource, Is.EqualTo(SongCatalogSource.OfflineCache));
    }

    [Test]
    public async Task GetSongsAsync_OfflineFallback_ClearsTheRawApiErrorMessage()
    {
        // This is what stops the library showing "Unable to load data from https://.../api/music/songs".
        GivenLiveResult([], LiveError);
        GivenCachedCatalog([CreateSong(1)], locallyReadyIds: [1]);

        await _service.GetSongsAsync();

        Assert.That(_service.LastSongsError, Is.Null);
    }

    [Test]
    public async Task GetSongsAsync_NoNetworkAtAll_SkipsTheLiveCallEntirely()
    {
        // Avoids burning the full songs-request timeout on a DNS lookup that cannot resolve.
        _connectivity.NetworkAccess = NetworkAccess.None;
        GivenCachedCatalog([CreateSong(1)], locallyReadyIds: [1]);

        var result = await _service.GetSongsAsync();

        Assert.That(result.Select(s => s.Id), Is.EqualTo(new[] { 1 }));
        _inner.Verify(s => s.GetSongsAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestCase(NetworkAccess.Unknown)]
    [TestCase(NetworkAccess.ConstrainedInternet)]
    public async Task GetSongsAsync_AmbiguousNetworkState_StillAttemptsTheLiveCall(NetworkAccess networkAccess)
    {
        // Only NetworkAccess.None is treated as definitely-offline; anything else gets a real attempt.
        _connectivity.NetworkAccess = networkAccess;
        GivenLiveResult([CreateSong(1)]);

        await _service.GetSongsAsync();

        _inner.Verify(s => s.GetSongsAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task GetSongsAsync_CachedSongsExistButNoneAreDownloaded_ReportsUnavailable()
    {
        GivenLiveResult([], LiveError);
        GivenCachedCatalog([CreateSong(1), CreateSong(2)], locallyReadyIds: []);

        var result = await _service.GetSongsAsync();

        Assert.That(result, Is.Empty);
        Assert.That(_service.LastSongsSource, Is.EqualTo(SongCatalogSource.Unavailable));
    }

    [Test]
    public async Task GetSongsAsync_ServerBrokenButOnline_PreservesTheDiagnosticMessage()
    {
        // Online-but-failing keeps today's behaviour: the user should see why it broke.
        GivenLiveResult([], LiveError);

        await _service.GetSongsAsync();

        Assert.That(_service.LastSongsError, Is.EqualTo(LiveError));
        Assert.That(_service.LastSongsSource, Is.EqualTo(SongCatalogSource.Unavailable));
    }

    [Test]
    public async Task GetSongsAsync_OfflineWithNothingDownloaded_SuppressesTheDiagnosticMessage()
    {
        _connectivity.NetworkAccess = NetworkAccess.None;

        await _service.GetSongsAsync();

        Assert.That(_service.LastSongsError, Is.Null);
        Assert.That(_service.LastSongsSource, Is.EqualTo(SongCatalogSource.Unavailable));
    }

    [Test]
    public async Task GetSongsAsync_CatalogStoreThrows_DegradesToUnavailable()
    {
        GivenLiveResult([], LiveError);
        _catalogStore.Setup(s => s.LoadAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("unreadable"));

        var result = await _service.GetSongsAsync();

        Assert.That(result, Is.Empty);
        Assert.That(_service.LastSongsSource, Is.EqualTo(SongCatalogSource.Unavailable));
    }

    [Test]
    public async Task GetSongsAsync_EmptySnapshot_DoesNotQueryTheAudioCache()
    {
        GivenLiveResult([], LiveError);

        await _service.GetSongsAsync();

        _trackCache.Verify(
            s => s.GetCacheStatusesAsync(It.IsAny<IReadOnlyList<SongDto>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task GetSongsAsync_RecoveringFromOffline_ReportsLiveAgain()
    {
        GivenLiveResult([], LiveError);
        GivenCachedCatalog([CreateSong(1)], locallyReadyIds: [1]);
        await _service.GetSongsAsync();

        GivenLiveResult([CreateSong(1), CreateSong(2)]);
        await _service.GetSongsAsync();

        Assert.That(_service.LastSongsSource, Is.EqualTo(SongCatalogSource.Live));
        Assert.That(_service.LastSongsError, Is.Null);
    }

    // --- Pass-throughs ---

    [Test]
    public async Task PassThroughMembers_DelegateToTheInnerService()
    {
        await _service.GetSongByTitleAsync("title");
        await _service.GetStreamQualifyingSecondsAsync();
        await _service.RecordStreamAsync(5);
        await _service.FlushPendingStreamRecordsAsync();
        await _service.ClearPendingStreamRecordsAsync();
        await _service.GetBulkLikeCountsAsync([1]);
        await _service.GetBulkUserLikeStatusAsync([1]);
        await _service.ToggleLikeAsync(1);
        await _service.ToggleDislikeAsync(1);
        await _service.GetSubscriptionStatusAsync();
        await _service.CancelSubscriptionAsync();
        await _service.ReportSongAsync(1, "Copyright Violation");
        await _service.VerifyGooglePlayPurchaseAsync("token", "order");

        _inner.Verify(s => s.GetSongByTitleAsync("title"), Times.Once);
        _inner.Verify(s => s.GetStreamQualifyingSecondsAsync(), Times.Once);
        _inner.Verify(s => s.RecordStreamAsync(5), Times.Once);
        _inner.Verify(s => s.FlushPendingStreamRecordsAsync(), Times.Once);
        _inner.Verify(s => s.ClearPendingStreamRecordsAsync(), Times.Once);
        _inner.Verify(s => s.GetBulkLikeCountsAsync(It.IsAny<IEnumerable<int>>()), Times.Once);
        _inner.Verify(s => s.GetBulkUserLikeStatusAsync(It.IsAny<IEnumerable<int>>()), Times.Once);
        _inner.Verify(s => s.ToggleLikeAsync(1), Times.Once);
        _inner.Verify(s => s.ToggleDislikeAsync(1), Times.Once);
        _inner.Verify(s => s.GetSubscriptionStatusAsync(), Times.Once);
        _inner.Verify(s => s.CancelSubscriptionAsync(), Times.Once);
        _inner.Verify(s => s.ReportSongAsync(1, "Copyright Violation"), Times.Once);
        _inner.Verify(s => s.VerifyGooglePlayPurchaseAsync("token", "order"), Times.Once);
    }

    [Test]
    public void OnStreamCountRecorded_SubscriptionsForwardToTheInnerService()
    {
        void Handler(int songId, int count) { }

        _service.OnStreamCountRecorded += Handler;
        _service.OnStreamCountRecorded -= Handler;

        _inner.VerifyAdd(s => s.OnStreamCountRecorded += It.IsAny<Action<int, int>>(), Times.Once);
        _inner.VerifyRemove(s => s.OnStreamCountRecorded -= It.IsAny<Action<int, int>>(), Times.Once);
    }
}
