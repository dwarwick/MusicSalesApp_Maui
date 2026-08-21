using MusicSalesApp.Common.Contracts;
using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.Tests.Services;

/// <summary>
/// The flatten-and-look-up half of synchronized lyrics.
/// </summary>
/// <remarks>
/// Worth testing hard because every failure here is subtle on a device: a highlight that skips a
/// word, blinks out between lines, or lights a section heading during the intro all look like
/// "the lyrics are a bit off" rather than like a specific bug.
/// </remarks>
[TestFixture]
public class LyricsTimelineTests
{
    private static LyricsTimedWord Word(string text, long? start, long? end = null) =>
        new() { Text = text, StartMs = start, EndMs = end ?? (start + 200) };

    private static LyricsTimedLine Line(string text, long? start, long? end, params LyricsTimedWord[] words) =>
        new() { Text = text, StartMs = start, EndMs = end, Words = [.. words] };

    /// <summary>Two sung lines with a section marker between them.</summary>
    private static LyricsTimingsDocument Document() => new()
    {
        SongId = 1,
        DurationMs = 60_000,
        Lines =
        [
            Line("one two", 1_000, 3_000, Word("one", 1_000), Word("two", 2_000)),
            Line("[Chorus]", null, null),
            Line("three four", 4_000, 6_000, Word("three", 4_000), Word("four", 5_000)),
        ]
    };

    [Test]
    public void Build_EmitsOneEntryPerTimedWord_InTimeOrder()
    {
        var timeline = LyricsTimeline.Build(Document());

        Assert.That(timeline.Count, Is.EqualTo(4), "Four words; the section marker contributes nothing.");
    }

    [Test]
    public void Build_KeepsDocumentLineIndices_SoTheRendererCanFindTheLine()
    {
        // The marker occupies index 1 in the document. If the timeline renumbered around it, the
        // second sung line would highlight the marker instead.
        var timeline = LyricsTimeline.Build(Document());

        Assert.Multiple(() =>
        {
            Assert.That(timeline.LineOf(0), Is.EqualTo(0));
            Assert.That(timeline.LineOf(2), Is.EqualTo(2), "Not 1 - the untimed marker keeps its slot.");
        });
    }

    [Test]
    public void Build_IgnoresUntimedLinesEntirely()
    {
        // Null times are load-bearing: a section marker read as 0 would be "sung" at the very
        // start and would light up through the whole intro.
        var timeline = LyricsTimeline.Build(Document());

        for (var i = 0; i < timeline.Count; i++)
        {
            Assert.That(timeline.LineOf(i), Is.Not.EqualTo(1));
        }
    }

    [Test]
    public void Build_GivesALineWithNoTimedWords_ASingleBlockEntry()
    {
        // The aligner can place a line without placing its words. Dropping those lines would lose
        // them silently; they highlight as one block instead.
        var document = new LyricsTimingsDocument
        {
            SongId = 1,
            DurationMs = 10_000,
            Lines = [Line("placed but unsplit", 1_000, 2_000, Word("placed", null), Word("unsplit", null))]
        };

        var timeline = LyricsTimeline.Build(document);

        Assert.Multiple(() =>
        {
            Assert.That(timeline.Count, Is.EqualTo(1));
            Assert.That(timeline.WordOf(0), Is.Zero, "Index 0 matches the single span the renderer draws.");
        });
    }

    [Test]
    public void Build_ToleratesNothingUsable()
    {
        Assert.Multiple(() =>
        {
            Assert.That(LyricsTimeline.Build(null).Count, Is.Zero);
            Assert.That(LyricsTimeline.Build(new LyricsTimingsDocument()).Count, Is.Zero);
        });
    }

    [Test]
    public void IndexAt_ReturnsMinusOne_BeforeTheFirstWord()
    {
        var timeline = LyricsTimeline.Build(Document());

        Assert.That(timeline.IndexAt(500, -1), Is.EqualTo(-1));
    }

    [TestCase(1_000, 0)]
    [TestCase(1_999, 0)]
    [TestCase(2_000, 1)]
    [TestCase(4_500, 2)]
    [TestCase(59_000, 3)]
    public void IndexAt_PicksTheLastWordThatHasStarted(long atMs, int expected)
    {
        var timeline = LyricsTimeline.Build(Document());

        Assert.That(timeline.IndexAt(atMs, -1), Is.EqualTo(expected));
    }

    /// <summary>
    /// A word stays lit through the gap after it ends.
    /// </summary>
    /// <remarks>
    /// End times exist in the document but are deliberately never consulted. Checking whether the
    /// clock is still inside the current word's span would blank the highlight between lines and
    /// through every instrumental break - which reads as the feature being broken.
    /// </remarks>
    [Test]
    public void IndexAt_HoldsTheLastWord_ThroughTheGapBeforeTheNext()
    {
        var timeline = LyricsTimeline.Build(Document());

        // "two" ends at 2_200; the next line does not start until 4_000.
        Assert.That(timeline.IndexAt(3_500, 1), Is.EqualTo(1));
    }

    [Test]
    public void IndexAt_WalksForward_FromThePreviousAnswer()
    {
        var timeline = LyricsTimeline.Build(Document());

        // The ordinary case each tick: a small step forward from where we were.
        Assert.That(timeline.IndexAt(4_100, 1), Is.EqualTo(2));
    }

    [Test]
    public void IndexAt_FallsBackToSearch_WhenTheClockJumpsBackwards()
    {
        var timeline = LyricsTimeline.Build(Document());

        // A seek to the start while the previous answer was the last word. Walking forward could
        // never find this; the search has to take over.
        Assert.That(timeline.IndexAt(1_000, 3), Is.EqualTo(0));
    }

    [Test]
    public void IndexAt_AgreesWithItself_WhicheverPathItTakes()
    {
        // The walk and the search are two routes to one answer; a divergence between them would
        // show up as the highlight depending on how you got there.
        var timeline = LyricsTimeline.Build(Document());

        for (long ms = 0; ms <= 7_000; ms += 50)
        {
            var searched = timeline.IndexAt(ms, -1);
            var walked = timeline.IndexAt(ms, searched > 0 ? searched - 1 : searched);

            Assert.That(walked, Is.EqualTo(searched), $"Disagreement at {ms}ms.");
        }
    }
}
