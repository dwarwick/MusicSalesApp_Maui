using Moq;
using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Tests.ViewModels;

[TestFixture]
public class OptimisticLikeStateUpdaterTests
{
    private Mock<IMusicService> _musicService = null!;

    [SetUp]
    public void SetUp() => _musicService = new Mock<IMusicService>();

    /// <summary>
    /// Streamed by default: rating a song requires having listened to it, so every test about what a
    /// tap does needs that precondition met. The tests that are about the rule itself pass false.
    /// </summary>
    private static SongDto CreateSong(
        bool? userLikeStatus = null,
        int likeCount = 10,
        int dislikeCount = 4,
        bool hasStreamed = true) => new()
    {
        Id = 42,
        UserLikeStatus = userLikeStatus,
        LikeCount = likeCount,
        DislikeCount = dislikeCount,
        HasStreamed = hasStreamed
    };

    private void GivenServerReturns(bool? userLikeStatus, int likeCount, int dislikeCount)
        => _musicService
            .Setup(s => s.SetLikeStateAsync(It.IsAny<int>(), It.IsAny<bool?>()))
            .ReturnsAsync(SetLikeStateOutcome.Applied(new LikeStateResult
            {
                UserLikeStatus = userLikeStatus,
                LikeCount = likeCount,
                DislikeCount = dislikeCount
            }));

    private void GivenQueuedOffline()
        => _musicService
            .Setup(s => s.SetLikeStateAsync(It.IsAny<int>(), It.IsAny<bool?>()))
            .ReturnsAsync(SetLikeStateOutcome.QueuedForRetry());

    private void GivenRequestFailed()
        => _musicService
            .Setup(s => s.SetLikeStateAsync(It.IsAny<int>(), It.IsAny<bool?>()))
            .ReturnsAsync(SetLikeStateOutcome.Failed());

    private void GivenServerReturnsStateWithoutCounts(bool? userLikeStatus)
        => _musicService
            .Setup(s => s.SetLikeStateAsync(It.IsAny<int>(), It.IsAny<bool?>()))
            .ReturnsAsync(SetLikeStateOutcome.Applied(new LikeStateResult { UserLikeStatus = userLikeStatus }));

    // --- Optimistic application ---

    [Test]
    public async Task ApplyAsync_SendsTheClientComputedDesiredState()
    {
        // The server endpoint is idempotent, so the client owns the toggle semantics.
        GivenServerReturns(true, 11, 4);

        await OptimisticLikeStateUpdater.ApplyAsync(_musicService.Object, CreateSong(), LikeAction.ThumbsUp);

        _musicService.Verify(s => s.SetLikeStateAsync(42, true), Times.Once);
    }

    [Test]
    public async Task ApplyAsync_ThumbsUpOnAnAlreadyLikedSong_RequestsNoOpinion()
    {
        GivenServerReturns(null, 9, 4);

        await OptimisticLikeStateUpdater.ApplyAsync(
            _musicService.Object, CreateSong(userLikeStatus: true), LikeAction.ThumbsUp);

        _musicService.Verify(s => s.SetLikeStateAsync(42, null), Times.Once);
    }

    [Test]
    public async Task ApplyAsync_QueuedOffline_KeepsTheOptimisticStateAndCounts()
    {
        var song = CreateSong();
        GivenQueuedOffline();

        await OptimisticLikeStateUpdater.ApplyAsync(_musicService.Object, song, LikeAction.ThumbsUp);

        Assert.Multiple(() =>
        {
            Assert.That(song.UserLikeStatus, Is.True);
            Assert.That(song.LikeCount, Is.EqualTo(11));
            Assert.That(song.DislikeCount, Is.EqualTo(4));
        });
    }

    [Test]
    public async Task ApplyAsync_QueuedOffline_SwitchingSidesMovesBothCounts()
    {
        var song = CreateSong(userLikeStatus: true);
        GivenQueuedOffline();

        await OptimisticLikeStateUpdater.ApplyAsync(_musicService.Object, song, LikeAction.ThumbsDown);

        Assert.Multiple(() =>
        {
            Assert.That(song.UserLikeStatus, Is.False);
            Assert.That(song.LikeCount, Is.EqualTo(9));
            Assert.That(song.DislikeCount, Is.EqualTo(5));
        });
    }

    [Test]
    public async Task ApplyAsync_QueuedOfflineTwice_ReturnsToTheStartingState()
    {
        var song = CreateSong();
        GivenQueuedOffline();

        await OptimisticLikeStateUpdater.ApplyAsync(_musicService.Object, song, LikeAction.ThumbsUp);
        await OptimisticLikeStateUpdater.ApplyAsync(_musicService.Object, song, LikeAction.ThumbsUp);

        Assert.Multiple(() =>
        {
            Assert.That(song.UserLikeStatus, Is.Null);
            Assert.That(song.LikeCount, Is.EqualTo(10));
            Assert.That(song.DislikeCount, Is.EqualTo(4));
        });
    }

    [Test]
    public async Task ApplyAsync_NeverDrivesACountNegative()
    {
        var song = CreateSong(userLikeStatus: true, likeCount: 0, dislikeCount: 0);
        GivenQueuedOffline();

        await OptimisticLikeStateUpdater.ApplyAsync(_musicService.Object, song, LikeAction.ThumbsUp);

        Assert.That(song.LikeCount, Is.Zero);
    }

    // --- Server reconciliation ---

    [Test]
    public async Task ApplyAsync_ServerResponseWithoutCounts_KeepsTheOptimisticCounts()
    {
        // The toggle-compatibility path can apply the state without being able to read the counts.
        // Treating the absent counts as zero would blank the visible totals.
        var song = CreateSong();
        GivenServerReturnsStateWithoutCounts(true);

        await OptimisticLikeStateUpdater.ApplyAsync(_musicService.Object, song, LikeAction.ThumbsUp);

        Assert.Multiple(() =>
        {
            Assert.That(song.UserLikeStatus, Is.True);
            Assert.That(song.LikeCount, Is.EqualTo(11));
            Assert.That(song.DislikeCount, Is.EqualTo(4));
        });
    }

    [Test]
    public async Task ApplyAsync_ServerResponse_OverwritesTheOptimisticValues()
    {
        // The server is authoritative once it answers: other users may have voted meanwhile.
        var song = CreateSong();
        GivenServerReturns(true, 999, 7);

        await OptimisticLikeStateUpdater.ApplyAsync(_musicService.Object, song, LikeAction.ThumbsUp);

        Assert.Multiple(() =>
        {
            Assert.That(song.UserLikeStatus, Is.True);
            Assert.That(song.LikeCount, Is.EqualTo(999));
            Assert.That(song.DislikeCount, Is.EqualTo(7));
        });
    }

    // --- Rollback ---

    [Test]
    public async Task ApplyAsync_NonRetryableFailure_RollsEverythingBack()
    {
        var song = CreateSong(userLikeStatus: false);
        GivenRequestFailed();

        await OptimisticLikeStateUpdater.ApplyAsync(_musicService.Object, song, LikeAction.ThumbsUp);

        Assert.Multiple(() =>
        {
            Assert.That(song.UserLikeStatus, Is.False);
            Assert.That(song.LikeCount, Is.EqualTo(10));
            Assert.That(song.DislikeCount, Is.EqualTo(4));
        });
    }

    [Test]
    public async Task ApplyAsync_RaisesPropertyChangedSoTheButtonsRefreshImmediately()
    {
        var song = CreateSong();
        GivenQueuedOffline();
        var raised = new List<string?>();
        song.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        await OptimisticLikeStateUpdater.ApplyAsync(_musicService.Object, song, LikeAction.ThumbsUp);

        Assert.Multiple(() =>
        {
            Assert.That(raised, Does.Contain(nameof(SongDto.UserLikeStatus)));
            Assert.That(raised, Does.Contain(nameof(SongDto.LikeCount)));
        });
    }

    // --- Redundant taps must not inflate the count ---

    [Test]
    public async Task ApplyAsync_StaleLocalState_DoesNotLeaveTheCountInflated()
    {
        // The song is already liked on the server but this client believed otherwise - the state fetch
        // failed, so UserLikeStatus came back null. The tap asks for "liked", the server is already
        // there, writes nothing and deliberately broadcasts nothing.
        //
        // The library normally leaves counts to the broadcast. For a no-op no broadcast is coming, so
        // without this the optimistic +1 stands forever: the phone shows 2 while the server and the web
        // both still show 1.
        var song = CreateSong(userLikeStatus: null, likeCount: 1);
        GivenServerReturns(userLikeStatus: true, likeCount: 1, dislikeCount: 0);

        await OptimisticLikeStateUpdater.ApplyAsync(_musicService.Object, song, LikeAction.ThumbsUp);

        Assert.Multiple(() =>
        {
            Assert.That(song.UserLikeStatus, Is.True, "The server's state is authoritative.");
            Assert.That(song.LikeCount, Is.EqualTo(1), "The optimistic increment must be undone.");
        });
    }

    // --- Rating requires a stream ---

    [Test]
    public async Task ApplyAsync_SettingARatingOnAnUnstreamedSong_IsRefusedWithoutTouchingTheServer()
    {
        var song = CreateSong(hasStreamed: false);

        var outcome = await OptimisticLikeStateUpdater.ApplyAsync(_musicService.Object, song, LikeAction.ThumbsUp);

        Assert.That(outcome, Is.EqualTo(LikeApplyOutcome.NeedsStream));
        _musicService.Verify(s => s.SetLikeStateAsync(It.IsAny<int>(), It.IsAny<bool?>()), Times.Never);
    }

    [Test]
    public async Task ApplyAsync_RefusedRating_LeavesTheButtonAlone()
    {
        // Checked before the optimistic write, so the thumb never fills in and snaps back.
        var song = CreateSong(hasStreamed: false, likeCount: 10);

        await OptimisticLikeStateUpdater.ApplyAsync(_musicService.Object, song, LikeAction.ThumbsUp);

        Assert.Multiple(() =>
        {
            Assert.That(song.UserLikeStatus, Is.Null);
            Assert.That(song.LikeCount, Is.EqualTo(10));
        });
    }

    [Test]
    public async Task ApplyAsync_ThumbsDownOnAnUnstreamedSong_IsAlsoRefused()
    {
        var song = CreateSong(hasStreamed: false);

        var outcome = await OptimisticLikeStateUpdater.ApplyAsync(_musicService.Object, song, LikeAction.ThumbsDown);

        Assert.That(outcome, Is.EqualTo(LikeApplyOutcome.NeedsStream));
    }

    [Test]
    public async Task ApplyAsync_ClearingAnExistingRatingWithoutAStream_IsAllowed()
    {
        // A rating made before the rule existed must stay retractable.
        var song = CreateSong(userLikeStatus: true, hasStreamed: false);
        GivenServerReturns(null, 9, 4);

        var outcome = await OptimisticLikeStateUpdater.ApplyAsync(_musicService.Object, song, LikeAction.ThumbsUp);

        Assert.That(outcome, Is.EqualTo(LikeApplyOutcome.Handled));
        _musicService.Verify(s => s.SetLikeStateAsync(42, null), Times.Once);
    }

    [Test]
    public async Task ApplyAsync_ServerRefuses_RollsBackAndCorrectsLocalEligibility()
    {
        // The client believed the song was streamed and the server disagreed. The server is the
        // authority, so the local view has to catch up or the next tap repeats the round trip.
        var song = CreateSong(hasStreamed: true, likeCount: 10);
        _musicService
            .Setup(s => s.SetLikeStateAsync(It.IsAny<int>(), It.IsAny<bool?>()))
            .ReturnsAsync(SetLikeStateOutcome.RequiresStream());

        var outcome = await OptimisticLikeStateUpdater.ApplyAsync(_musicService.Object, song, LikeAction.ThumbsUp);

        Assert.Multiple(() =>
        {
            Assert.That(outcome, Is.EqualTo(LikeApplyOutcome.NeedsStream));
            Assert.That(song.UserLikeStatus, Is.Null, "Optimistic state rolled back.");
            Assert.That(song.LikeCount, Is.EqualTo(10));
            Assert.That(song.HasStreamed, Is.False);
            Assert.That(song.CanRate, Is.False);
        });
    }

    [Test]
    public async Task ApplyAsync_OrdinaryFailure_IsNotReportedAsNeedingAStream()
    {
        var song = CreateSong();
        GivenRequestFailed();

        var outcome = await OptimisticLikeStateUpdater.ApplyAsync(_musicService.Object, song, LikeAction.ThumbsUp);

        Assert.Multiple(() =>
        {
            Assert.That(outcome, Is.EqualTo(LikeApplyOutcome.Handled));
            Assert.That(song.HasStreamed, Is.True, "A network failure says nothing about eligibility.");
        });
    }
}
