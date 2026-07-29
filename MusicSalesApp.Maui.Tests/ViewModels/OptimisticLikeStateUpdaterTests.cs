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

    private static SongDto CreateSong(bool? userLikeStatus = null, int likeCount = 10, int dislikeCount = 4) => new()
    {
        Id = 42,
        UserLikeStatus = userLikeStatus,
        LikeCount = likeCount,
        DislikeCount = dislikeCount
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

    [Test]
    public async Task ApplyAsync_WithApplyServerCountsFalse_KeepsTheOptimisticCounts()
    {
        // The library waits for the SignalR broadcast so every open screen updates together.
        var song = CreateSong();
        GivenServerReturns(true, 999, 7);

        await OptimisticLikeStateUpdater.ApplyAsync(
            _musicService.Object, song, LikeAction.ThumbsUp, applyServerCounts: false);

        Assert.Multiple(() =>
        {
            Assert.That(song.UserLikeStatus, Is.True);
            Assert.That(song.LikeCount, Is.EqualTo(11));
            Assert.That(song.DislikeCount, Is.EqualTo(4));
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
}
