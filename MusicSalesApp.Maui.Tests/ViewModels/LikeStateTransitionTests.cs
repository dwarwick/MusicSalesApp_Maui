using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Tests.ViewModels;

[TestFixture]
public class LikeStateTransitionTests
{
    // Full truth table. currentState: true = thumbs up, false = thumbs down, null = no opinion.
    [TestCase(null, LikeAction.ThumbsUp, true, 1, 0)]
    [TestCase(true, LikeAction.ThumbsUp, null, -1, 0)]
    [TestCase(false, LikeAction.ThumbsUp, true, 1, -1)]
    [TestCase(null, LikeAction.ThumbsDown, false, 0, 1)]
    [TestCase(false, LikeAction.ThumbsDown, null, 0, -1)]
    [TestCase(true, LikeAction.ThumbsDown, false, -1, 1)]
    public void Apply_ProducesTheExpectedStateAndCountDeltas(
        bool? currentState,
        LikeAction action,
        bool? expectedState,
        int expectedLikeDelta,
        int expectedDislikeDelta)
    {
        var change = LikeStateTransition.Apply(currentState, action);

        Assert.Multiple(() =>
        {
            Assert.That(change.DesiredState, Is.EqualTo(expectedState));
            Assert.That(change.LikeCountDelta, Is.EqualTo(expectedLikeDelta));
            Assert.That(change.DislikeCountDelta, Is.EqualTo(expectedDislikeDelta));
        });
    }

    [TestCase(LikeAction.ThumbsUp)]
    [TestCase(LikeAction.ThumbsDown)]
    public void Apply_TappingTheSameButtonTwice_ReturnsToNoOpinion(LikeAction action)
    {
        // This is exactly why the queue coalesces per song: two offline taps must mean "no opinion",
        // never two toggles replayed against the server.
        var first = LikeStateTransition.Apply(null, action);

        var second = LikeStateTransition.Apply(first.DesiredState, action);

        Assert.That(second.DesiredState, Is.Null);
    }

    [TestCase(LikeAction.ThumbsUp)]
    [TestCase(LikeAction.ThumbsDown)]
    public void Apply_TappingTwice_LeavesCountsWhereTheyStarted(LikeAction action)
    {
        var first = LikeStateTransition.Apply(null, action);
        var second = LikeStateTransition.Apply(first.DesiredState, action);

        Assert.That(first.LikeCountDelta + second.LikeCountDelta, Is.Zero);
        Assert.That(first.DislikeCountDelta + second.DislikeCountDelta, Is.Zero);
    }

    [Test]
    public void Apply_SwitchingSides_MovesExactlyOneVoteFromEachCount()
    {
        var change = LikeStateTransition.Apply(true, LikeAction.ThumbsDown);

        Assert.That(change.LikeCountDelta, Is.EqualTo(-1));
        Assert.That(change.DislikeCountDelta, Is.EqualTo(1));
    }
}
