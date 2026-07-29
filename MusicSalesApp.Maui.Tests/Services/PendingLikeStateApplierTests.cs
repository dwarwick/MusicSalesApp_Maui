using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Tests.Services;

[TestFixture]
public class PendingLikeStateApplierTests
{
    private static SongDto CreateSong(int id = 42, bool? userLikeStatus = null, int likeCount = 10, int dislikeCount = 4)
        => new()
        {
            Id = id,
            UserLikeStatus = userLikeStatus,
            LikeCount = likeCount,
            DislikeCount = dislikeCount
        };

    [Test]
    public void Apply_ReplaysAQueuedThumbsUpOverTheSnapshot()
    {
        // Without this a thumbs-up tapped offline would visibly revert after an app restart, even
        // though the intent is still queued for the server.
        var song = CreateSong();

        PendingLikeStateApplier.Apply([song], new Dictionary<int, bool?> { [42] = true });

        Assert.Multiple(() =>
        {
            Assert.That(song.UserLikeStatus, Is.True);
            Assert.That(song.LikeCount, Is.EqualTo(11));
            Assert.That(song.DislikeCount, Is.EqualTo(4));
        });
    }

    [Test]
    public void Apply_ReplayingAClearedOpinion_RemovesTheVote()
    {
        var song = CreateSong(userLikeStatus: true);

        PendingLikeStateApplier.Apply([song], new Dictionary<int, bool?> { [42] = null });

        Assert.Multiple(() =>
        {
            Assert.That(song.UserLikeStatus, Is.Null);
            Assert.That(song.LikeCount, Is.EqualTo(9));
        });
    }

    [Test]
    public void Apply_SwitchingSides_MovesOneVoteBetweenTheCounts()
    {
        var song = CreateSong(userLikeStatus: true);

        PendingLikeStateApplier.Apply([song], new Dictionary<int, bool?> { [42] = false });

        Assert.Multiple(() =>
        {
            Assert.That(song.UserLikeStatus, Is.False);
            Assert.That(song.LikeCount, Is.EqualTo(9));
            Assert.That(song.DislikeCount, Is.EqualTo(5));
        });
    }

    [Test]
    public void Apply_WhenTheSnapshotAlreadyMatches_ChangesNothing()
    {
        // The server may have accepted the intent before the snapshot was written.
        var song = CreateSong(userLikeStatus: true);

        PendingLikeStateApplier.Apply([song], new Dictionary<int, bool?> { [42] = true });

        Assert.That(song.LikeCount, Is.EqualTo(10));
    }

    [Test]
    public void Apply_LeavesSongsWithoutAQueuedIntentUntouched()
    {
        var withIntent = CreateSong(1);
        var withoutIntent = CreateSong(2);

        PendingLikeStateApplier.Apply([withIntent, withoutIntent], new Dictionary<int, bool?> { [1] = true });

        Assert.That(withIntent.UserLikeStatus, Is.True);
        Assert.That(withoutIntent.UserLikeStatus, Is.Null);
        Assert.That(withoutIntent.LikeCount, Is.EqualTo(10));
    }

    [Test]
    public void Apply_WithNoPendingIntents_IsANoOp()
    {
        var song = CreateSong(userLikeStatus: true);

        PendingLikeStateApplier.Apply([song], new Dictionary<int, bool?>());

        Assert.That(song.UserLikeStatus, Is.True);
        Assert.That(song.LikeCount, Is.EqualTo(10));
    }

    [Test]
    public void Apply_NeverDrivesACountNegative()
    {
        var song = CreateSong(userLikeStatus: true, likeCount: 0, dislikeCount: 0);

        PendingLikeStateApplier.Apply([song], new Dictionary<int, bool?> { [42] = null });

        Assert.That(song.LikeCount, Is.Zero);
    }
}
