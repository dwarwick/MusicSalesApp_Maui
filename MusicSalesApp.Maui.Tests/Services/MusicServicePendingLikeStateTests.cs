using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Networking;
using Moq;
using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Tests.Services;

/// <summary>
/// The offline like/dislike queue. Mirrors the pending stream-record machinery, but coalesces one
/// terminal intent per song rather than appending every tap - replaying two toggles would flip the
/// user's opinion back.
/// </summary>
[TestFixture]
public class MusicServicePendingLikeStateTests
{
    private const string PendingLikeStatesPreferenceKey = "pending_like_states_v1";
    private const int SongId = 42;

    private Mock<IHttpClientFactory> _httpClientFactory = null!;
    private Mock<IAppSettingsService> _appSettingsService = null!;
    private Mock<ILogger<MusicService>> _logger = null!;
    private InMemoryPreferenceStore _preferenceStore = null!;
    private TestConnectivity _connectivity = null!;
    private ScriptedHttpMessageHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _httpClientFactory = new Mock<IHttpClientFactory>();
        _appSettingsService = new Mock<IAppSettingsService>();
        _logger = new Mock<ILogger<MusicService>>();
        _preferenceStore = new InMemoryPreferenceStore();
        _connectivity = new TestConnectivity();
        _handler = new ScriptedHttpMessageHandler();

        _httpClientFactory
            .Setup(f => f.CreateClient("MusicSalesApi"))
            .Returns(() => new HttpClient(_handler, disposeHandler: false)
            {
                BaseAddress = new Uri("https://test.example.com/")
            });
    }

    [TearDown]
    public void TearDown() => _handler.Dispose();

    private MusicService CreateService(TimeSpan? retryInterval = null) => new(
        _httpClientFactory.Object,
        _appSettingsService.Object,
        _preferenceStore,
        _connectivity,
        _logger.Object,
        retryInterval ?? TimeSpan.FromMilliseconds(20));

    private void GivenOffline()
    {
        _connectivity.NetworkAccess = NetworkAccess.None;
        _handler.RespondWith(_ => throw new HttpRequestException("Unable to resolve host"));
    }

    private void GivenServerAccepts(bool? userLikeStatus, int likeCount = 10, int dislikeCount = 3)
    {
        _connectivity.NetworkAccess = NetworkAccess.Internet;
        _handler.RespondWith(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                userLikeStatus,
                likeCount,
                dislikeCount
            })
        });
    }

    private string? StoredQueue => _preferenceStore.GetString(PendingLikeStatesPreferenceKey);

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var timeoutAt = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < timeoutAt)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.Fail("Condition was not met within the timeout.");
    }

    // --- Queueing ---

    [Test]
    public async Task SetLikeStateAsync_WhileOffline_QueuesTheIntent()
    {
        GivenOffline();
        var service = CreateService();

        var outcome = await service.SetLikeStateAsync(SongId, true);

        Assert.That(outcome.Queued, Is.True);
        Assert.That(outcome.Result, Is.Null);
        Assert.That(await service.GetPendingLikeStatesAsync(), Is.EqualTo(new Dictionary<int, bool?> { [SongId] = true }));
    }

    [Test]
    public async Task SetLikeStateAsync_TappedTwiceOffline_ReplacesRatherThanAppends()
    {
        // The whole reason the queue stores intents instead of taps: replaying two toggles would put
        // the user back where they started on the server while the UI showed the opposite.
        GivenOffline();
        var service = CreateService();

        await service.SetLikeStateAsync(SongId, true);
        await service.SetLikeStateAsync(SongId, null);

        var pending = await service.GetPendingLikeStatesAsync();
        Assert.That(pending, Has.Count.EqualTo(1));
        Assert.That(pending[SongId], Is.Null);
    }

    [Test]
    public async Task SetLikeStateAsync_DifferentSongs_AreQueuedIndependently()
    {
        GivenOffline();
        var service = CreateService();

        await service.SetLikeStateAsync(1, true);
        await service.SetLikeStateAsync(2, false);

        var pending = await service.GetPendingLikeStatesAsync();
        Assert.That(pending, Has.Count.EqualTo(2));
        Assert.That(pending[1], Is.True);
        Assert.That(pending[2], Is.False);
    }

    [Test]
    public async Task SetLikeStateAsync_QueuePersistsAcrossServiceInstances()
    {
        // Survives an app restart, so an optimistic tap is not silently lost.
        GivenOffline();
        await CreateService().SetLikeStateAsync(SongId, true);

        var restarted = CreateService();

        Assert.That((await restarted.GetPendingLikeStatesAsync())[SongId], Is.True);
    }

    // --- Flushing ---

    [Test]
    public async Task FlushPendingLikeStatesAsync_ReplaysAndClearsTheQueue()
    {
        GivenOffline();
        var service = CreateService();
        await service.SetLikeStateAsync(SongId, true);

        GivenServerAccepts(true);
        await service.FlushPendingLikeStatesAsync();

        Assert.That(await service.GetPendingLikeStatesAsync(), Is.Empty);
        Assert.That(StoredQueue, Is.Null);
    }

    [Test]
    public async Task FlushPendingLikeStatesAsync_SendsTheCoalescedIntentOnlyOnce()
    {
        GivenOffline();
        var service = CreateService();
        await service.SetLikeStateAsync(SongId, true);
        await service.SetLikeStateAsync(SongId, false);
        await service.SetLikeStateAsync(SongId, null);

        GivenServerAccepts(null);
        // Count only what the flush sends; the three offline attempts also hit the handler.
        _handler.ResetRequestCount();
        await service.FlushPendingLikeStatesAsync();

        Assert.That(_handler.RequestCount, Is.EqualTo(1));
    }

    [Test]
    public async Task SetLikeStateAsync_WhenTheServerAcceptsIt_DropsTheQueuedIntentForThatSong()
    {
        // The queued intent is stale the moment a newer one reaches the server; replaying it later
        // silently reverts the choice the user just made.
        //
        // Two songs are queued and the first one keeps failing, so the flush breaks before it could
        // reach the second. That is the case the removal has to cover on its own - with a healthy
        // server the opportunistic flush would drain the queue regardless and hide the bug.
        const int stuckSongId = SongId + 1;
        GivenOffline();
        var service = CreateService(TimeSpan.FromMinutes(5));
        await service.SetLikeStateAsync(stuckSongId, true);
        await service.SetLikeStateAsync(SongId, true);

        _connectivity.NetworkAccess = NetworkAccess.Internet;
        _handler.RespondWith(request =>
            request.RequestUri!.AbsolutePath.EndsWith($"/{stuckSongId}", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { userLikeStatus = (bool?)false, likeCount = 1, dislikeCount = 0 })
                });

        await service.SetLikeStateAsync(SongId, false);

        var pending = await service.GetPendingLikeStatesAsync();
        Assert.Multiple(() =>
        {
            Assert.That(pending, Does.Not.ContainKey(SongId), "the song just set on the server must not stay queued");
            Assert.That(pending, Does.ContainKey(stuckSongId), "an unrelated queued song must survive");
        });
    }

    [Test]
    public async Task SetLikeStateAsync_WhenTheServerAcceptsIt_LeavesOtherSongsQueued()
    {
        GivenOffline();
        // A long retry interval: a song deliberately left queued at the end of the test would otherwise
        // keep its 20ms retry loop firing into whichever handler the next test installs.
        var service = CreateService(TimeSpan.FromMinutes(5));
        await service.SetLikeStateAsync(SongId, true);
        await service.SetLikeStateAsync(SongId + 1, false);

        GivenServerAccepts(null);
        await service.SetLikeStateAsync(SongId, null);

        var pending = await service.GetPendingLikeStatesAsync();
        Assert.Multiple(() =>
        {
            Assert.That(pending, Does.Not.ContainKey(SongId));
            Assert.That(pending[SongId + 1], Is.False);
        });
    }

    [Test]
    public async Task FlushPendingLikeStatesAsync_StillOffline_KeepsTheQueue()
    {
        GivenOffline();
        var service = CreateService();
        await service.SetLikeStateAsync(SongId, true);
        _handler.ResetRequestCount();

        await service.FlushPendingLikeStatesAsync();

        Assert.That(await service.GetPendingLikeStatesAsync(), Has.Count.EqualTo(1));
    }

    [Test]
    public async Task ConnectivityRestored_TriggersAFlush()
    {
        GivenOffline();
        var service = CreateService();
        await service.SetLikeStateAsync(SongId, true);

        GivenServerAccepts(true);
        _connectivity.RaiseConnectivityChanged();

        await WaitForAsync(() => StoredQueue == null);
        Assert.That(await service.GetPendingLikeStatesAsync(), Is.Empty);
    }

    [Test]
    public async Task RetryLoop_DrainsTheQueueOnceTheServerRecovers()
    {
        GivenOffline();
        var service = CreateService(TimeSpan.FromMilliseconds(20));
        await service.SetLikeStateAsync(SongId, true);

        GivenServerAccepts(true);

        await WaitForAsync(() => StoredQueue == null);
    }

    // --- Non-retryable responses ---

    [TestCase(HttpStatusCode.BadRequest)]
    [TestCase(HttpStatusCode.Unauthorized)]
    [TestCase(HttpStatusCode.Forbidden)]
    public async Task FlushPendingLikeStatesAsync_NonRetryableStatus_DropsTheQueuedIntent(HttpStatusCode statusCode)
    {
        // 401/403 matter specifically: this endpoint requires auth, so a JWT that expires while an
        // intent is queued would otherwise be retried every 15 seconds forever.
        GivenOffline();
        var service = CreateService();
        await service.SetLikeStateAsync(SongId, true);

        _connectivity.NetworkAccess = NetworkAccess.Internet;
        _handler.RespondWith(_ => new HttpResponseMessage(statusCode));
        await service.FlushPendingLikeStatesAsync();

        Assert.That(await service.GetPendingLikeStatesAsync(), Is.Empty);
    }

    [Test]
    public async Task FlushPendingLikeStatesAsync_ServerError_KeepsTheQueuedIntent()
    {
        GivenOffline();
        var service = CreateService();
        await service.SetLikeStateAsync(SongId, true);

        _connectivity.NetworkAccess = NetworkAccess.Internet;
        _handler.RespondWith(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        await service.FlushPendingLikeStatesAsync();

        Assert.That(await service.GetPendingLikeStatesAsync(), Has.Count.EqualTo(1));
    }

    [Test]
    public async Task FlushPendingLikeStatesAsync_ARejectedIntent_DoesNotBlockTheRestOfTheQueue()
    {
        // The flush stops at the first failure, so a permanently unwritable intent - a song deleted
        // while the user was offline - would strand every intent queued behind it. The server returns
        // 400 for that case precisely so this drains instead.
        const int deletedSongId = SongId + 1;
        GivenOffline();
        var service = CreateService(TimeSpan.FromMinutes(5));
        await service.SetLikeStateAsync(deletedSongId, true);
        await service.SetLikeStateAsync(SongId, true);

        _connectivity.NetworkAccess = NetworkAccess.Internet;
        _handler.RespondWith(request =>
            request.RequestUri!.AbsolutePath.EndsWith($"/{deletedSongId}", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.BadRequest)
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { userLikeStatus = (bool?)true, likeCount = 1, dislikeCount = 0 })
                });

        await service.FlushPendingLikeStatesAsync();

        Assert.That(await service.GetPendingLikeStatesAsync(), Is.Empty);
    }

    // --- Online path ---

    [Test]
    public async Task SetLikeStateAsync_WhileOnline_AppliesImmediatelyAndQueuesNothing()
    {
        GivenServerAccepts(true, likeCount: 11, dislikeCount: 3);
        var service = CreateService();

        var outcome = await service.SetLikeStateAsync(SongId, true);

        Assert.That(outcome.Result, Is.Not.Null);
        Assert.That(outcome.Result!.UserLikeStatus, Is.True);
        Assert.That(outcome.Result.LikeCount, Is.EqualTo(11));
        Assert.That(await service.GetPendingLikeStatesAsync(), Is.Empty);
    }

    [Test]
    public async Task SetLikeStateAsync_UsesThePutSetStateEndpoint()
    {
        GivenServerAccepts(true);

        await CreateService().SetLikeStateAsync(SongId, true);

        Assert.That(_handler.LastRequest!.Method, Is.EqualTo(HttpMethod.Put));
        Assert.That(_handler.LastRequest.RequestUri!.AbsolutePath, Is.EqualTo($"/api/music/like-state/{SongId}"));
    }

    [Test]
    public async Task SetLikeStateAsync_SuccessfulCall_OpportunisticallyDrainsAnOlderQueue()
    {
        GivenOffline();
        var service = CreateService();
        await service.SetLikeStateAsync(1, true);

        GivenServerAccepts(false);
        await service.SetLikeStateAsync(2, false);

        await WaitForAsync(() => StoredQueue == null);
    }

    // --- Fallback for an older server ---

    [Test]
    public async Task SetLikeStateAsync_WhenSetStateEndpointIsMissing_FallsBackToTheToggleEndpoint()
    {
        // Lets the app ship before the server change is deployed.
        _connectivity.NetworkAccess = NetworkAccess.Internet;
        _handler.RespondWith(request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("like-state"))
                return new HttpResponseMessage(HttpStatusCode.NotFound);

            if (request.RequestUri.AbsolutePath.Contains("likes/user-status"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new[] { new { songMetadataId = SongId, userLikeStatus = (bool?)null } })
                };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { isLiked = true, isDisliked = false, likeCount = 11, dislikeCount = 3 })
            };
        });

        var outcome = await CreateService().SetLikeStateAsync(SongId, true);

        Assert.That(outcome.Result, Is.Not.Null);
        Assert.That(outcome.Result!.UserLikeStatus, Is.True);
        Assert.That(outcome.Result.LikeCount, Is.EqualTo(11));
    }

    [Test]
    public async Task SetLikeStateAsync_FallbackPath_SkipsTheToggleWhenTheServerAlreadyMatches()
    {
        // The toggle endpoints flip relative to stored state, so toggling here would undo the intent.
        _connectivity.NetworkAccess = NetworkAccess.Internet;
        var togglesIssued = 0;
        _handler.RespondWith(request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("like-state"))
                return new HttpResponseMessage(HttpStatusCode.NotFound);

            if (request.RequestUri.AbsolutePath.Contains("likes/user-status"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new[] { new { songMetadataId = SongId, userLikeStatus = (bool?)true } })
                };

            if (request.RequestUri.AbsolutePath.Contains("likes/bulk"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new[] { new { songMetadataId = SongId, likeCount = 11, dislikeCount = 3 } })
                };

            togglesIssued++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { isLiked = false, isDisliked = false, likeCount = 10, dislikeCount = 3 })
            };
        });

        var outcome = await CreateService().SetLikeStateAsync(SongId, true);

        Assert.That(togglesIssued, Is.Zero);
        Assert.That(outcome.Result!.UserLikeStatus, Is.True);
        Assert.That(outcome.Result.LikeCount, Is.EqualTo(11));
    }

    [Test]
    public async Task SetLikeStateAsync_FallbackNoOp_ReportsNoCountsWhenTheCountsFetchFails()
    {
        // Nothing was written, so the state is right - but the counts are unknown. Reporting zero here
        // would be applied as authoritative and blank the visible totals.
        _connectivity.NetworkAccess = NetworkAccess.Internet;
        _handler.RespondWith(request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("like-state"))
                return new HttpResponseMessage(HttpStatusCode.NotFound);

            if (request.RequestUri.AbsolutePath.Contains("likes/user-status"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new[] { new { songMetadataId = SongId, userLikeStatus = (bool?)true } })
                };

            // The counts call fails, so no entry comes back for this song.
            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });

        var outcome = await CreateService().SetLikeStateAsync(SongId, true);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Result!.UserLikeStatus, Is.True);
            Assert.That(outcome.Result.LikeCount, Is.Null);
            Assert.That(outcome.Result.DislikeCount, Is.Null);
        });
    }

    // --- Logout ---

    [Test]
    public async Task ClearPendingLikeStatesAsync_EmptiesTheQueue()
    {
        GivenOffline();
        var service = CreateService();
        await service.SetLikeStateAsync(SongId, true);

        await service.ClearPendingLikeStatesAsync();

        Assert.That(await service.GetPendingLikeStatesAsync(), Is.Empty);
    }

    [Test]
    public async Task GetPendingLikeStatesAsync_WithCorruptStoredJson_SelfHeals()
    {
        _preferenceStore.SetString(PendingLikeStatesPreferenceKey, "{not json");

        Assert.That(await CreateService().GetPendingLikeStatesAsync(), Is.Empty);
    }
}

/// <summary>
/// Handler that answers each request from a caller-supplied function, so a single test can script
/// different responses per endpoint.
/// </summary>
internal sealed class ScriptedHttpMessageHandler : HttpMessageHandler
{
    private Func<HttpRequestMessage, HttpResponseMessage> _respond =
        _ => new HttpResponseMessage(HttpStatusCode.OK);

    public int RequestCount { get; private set; }

    public HttpRequestMessage? LastRequest { get; private set; }

    public void RespondWith(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

    public void ResetRequestCount() => RequestCount = 0;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        RequestCount++;
        LastRequest = request;

        try
        {
            return Task.FromResult(_respond(request));
        }
        catch (Exception ex)
        {
            return Task.FromException<HttpResponseMessage>(ex);
        }
    }
}
