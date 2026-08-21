using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.Tests.Services;

/// <summary>
/// Reconciling what the player holds against what the app believes it submitted.
/// </summary>
/// <remarks>
/// Every case here is a real production fault. Walking from the home page's featured queue, where
/// a song is first, to the artist queue, where the same song is second, played one song while the
/// screen named another - the title, duration and lyrics all belonging to a different track from
/// the audio. Twice: once because the queue check compared counts, and once because the correction
/// compared indices across two different orderings.
/// </remarks>
[TestFixture]
public class PlaybackQueueAlignmentTests
{
    // The two songs from the report. Ids are the per-song stable cache key.
    private const string LostInYourWaves = "song-156-f693aa02";
    private const string FiveYearPlan = "song-157-eca4c8e5";

    // Featured puts Five-Year-Plan first; the artist page puts it second. Same songs, same length,
    // different order - which is the entire difficulty.
    private static readonly string?[] FeaturedOrder = [FiveYearPlan, LostInYourWaves];
    private static readonly string?[] ArtistOrder = [LostInYourWaves, FiveYearPlan];

    // --- Did the submitted queue actually land? ---

    /// <summary>
    /// Two queues of the same length in different orders are NOT a match.
    /// </summary>
    /// <remarks>
    /// The regression. The check this replaces compared counts, so two of two passed while the
    /// player was still holding the previous ordering - after which every index the app and the
    /// player exchanged referred to a different song.
    /// </remarks>
    [Test]
    public void QueueMatches_IsFalse_ForTheSameSongsInADifferentOrder()
    {
        Assert.That(PlaybackQueueAlignment.QueueMatches(ArtistOrder, FeaturedOrder), Is.False);
    }

    [Test]
    public void QueueMatches_IsTrue_WhenThePlayerHoldsExactlyWhatWasSubmitted()
    {
        Assert.That(PlaybackQueueAlignment.QueueMatches(ArtistOrder, ArtistOrder), Is.True);
    }

    [Test]
    public void QueueMatches_IsFalse_WhenThePlayerHoldsNothing()
    {
        // The one case the old count check did catch: a bulk submit that silently applied nothing.
        Assert.That(PlaybackQueueAlignment.QueueMatches(ArtistOrder, []), Is.False);
    }

    [Test]
    public void QueueMatches_IsFalse_OnDifferentLengths()
    {
        Assert.That(
            PlaybackQueueAlignment.QueueMatches(ArtistOrder, [LostInYourWaves]),
            Is.False);
    }

    [Test]
    public void QueueMatches_IsTrue_ForTwoEmptyQueues()
    {
        Assert.That(PlaybackQueueAlignment.QueueMatches([], []), Is.True);
    }

    // --- Where, if anywhere, should the player be moved? ---

    /// <summary>
    /// A player already on the right song is left alone, whatever index it reports.
    /// </summary>
    /// <remarks>
    /// THE bug. The app wanted Five-Year-Plan at index 1, its position in the artist queue. The
    /// player was still holding the featured queue, where it sits at index 0 - and was playing it
    /// correctly. The old correction saw index 0 where it wanted index 1 and seeked to index 1,
    /// which in the list the player actually held is Lost In Your Waves. It moved playback onto the
    /// wrong song while every label kept naming the right one.
    /// </remarks>
    [Test]
    public void ResolveCorrection_LeavesThePlayerAlone_WhenItIsAlreadyOnTheRightSong()
    {
        var correction = PlaybackQueueAlignment.ResolveCorrection(
            expectedId: FiveYearPlan,
            currentId: FiveYearPlan,
            playerIds: FeaturedOrder);

        Assert.That(correction.Kind, Is.EqualTo(QueueCorrectionKind.AlreadyCorrect));
    }

    [Test]
    public void ResolveCorrection_SeeksToWhereTheSongSitsInThePlayersOwnList()
    {
        // The app thinks Five-Year-Plan is at index 1; the player holds it at index 0. The seek
        // target must come from the player's list, not the app's.
        var correction = PlaybackQueueAlignment.ResolveCorrection(
            expectedId: FiveYearPlan,
            currentId: LostInYourWaves,
            playerIds: FeaturedOrder);

        Assert.Multiple(() =>
        {
            Assert.That(correction.Kind, Is.EqualTo(QueueCorrectionKind.SeekTo));
            Assert.That(correction.SeekIndex, Is.EqualTo(0));
        });
    }

    [Test]
    public void ResolveCorrection_SeeksForward_WhenTheSongSitsLaterInThePlayersList()
    {
        var correction = PlaybackQueueAlignment.ResolveCorrection(
            expectedId: FiveYearPlan,
            currentId: LostInYourWaves,
            playerIds: ArtistOrder);

        Assert.Multiple(() =>
        {
            Assert.That(correction.Kind, Is.EqualTo(QueueCorrectionKind.SeekTo));
            Assert.That(correction.SeekIndex, Is.EqualTo(1));
        });
    }

    /// <summary>
    /// A song the player is not holding cannot be seeked to, and must not be guessed at.
    /// </summary>
    /// <remarks>
    /// Seeking to some plausible index here is what produces silent wrong-song playback. The caller
    /// logs this at Error instead, because it is the only trace such a session leaves.
    /// </remarks>
    [Test]
    public void ResolveCorrection_ReportsNotPresent_WhenThePlayerDoesNotHoldTheSong()
    {
        var correction = PlaybackQueueAlignment.ResolveCorrection(
            expectedId: "song-999-deadbeef",
            currentId: LostInYourWaves,
            playerIds: ArtistOrder);

        Assert.Multiple(() =>
        {
            Assert.That(correction.Kind, Is.EqualTo(QueueCorrectionKind.NotPresent));
            Assert.That(correction.SeekIndex, Is.EqualTo(-1));
        });
    }

    [Test]
    public void ResolveCorrection_ReportsNotPresent_ForAnEmptyPlayerQueue()
    {
        var correction = PlaybackQueueAlignment.ResolveCorrection(FiveYearPlan, null, []);

        Assert.That(correction.Kind, Is.EqualTo(QueueCorrectionKind.NotPresent));
    }

    [TestCase(null)]
    [TestCase("")]
    public void ResolveCorrection_ReportsNotPresent_WhenThereIsNoSongToLookFor(string? expectedId)
    {
        // An item with no media id cannot be matched against anything, and treating a null current
        // id as "equal" would report AlreadyCorrect for a player sitting on nothing.
        var correction = PlaybackQueueAlignment.ResolveCorrection(expectedId, null, ArtistOrder);

        Assert.That(correction.Kind, Is.EqualTo(QueueCorrectionKind.NotPresent));
    }

    [Test]
    public void ResolveCorrection_SeeksToTheFirstMatch_WhenASongAppearsTwice()
    {
        // A playlist may legitimately hold the same song twice. Deterministic beats clever.
        var correction = PlaybackQueueAlignment.ResolveCorrection(
            expectedId: FiveYearPlan,
            currentId: LostInYourWaves,
            playerIds: [LostInYourWaves, FiveYearPlan, FiveYearPlan]);

        Assert.That(correction.SeekIndex, Is.EqualTo(1));
    }
}
