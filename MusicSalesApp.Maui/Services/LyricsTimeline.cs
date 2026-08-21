#nullable enable
using MusicSalesApp.Common.Contracts;

namespace MusicSalesApp.Maui.Services;

/// <summary>
/// A timings document flattened into the one shape a per-frame lookup wants: parallel arrays of
/// every <em>timed</em> word, ascending in time.
/// </summary>
/// <remarks>
/// <para>
/// Pure and free of any UI or platform type, because this is where the bugs live. Getting the
/// lookup subtly wrong produces lyrics that drift, flicker, or skip a word - all of which are
/// hard to see in a screenshot and easy to assert here.
/// </para>
/// <para>
/// Mirrors the web app's <c>LyricsScroller.Flatten</c> deliberately, so the two clients light up
/// the same word at the same moment. Where the two must agree, this file says so.
/// </para>
/// </remarks>
internal sealed class LyricsTimeline
{
    /// <summary>An empty timeline, for a song with no usable timings.</summary>
    public static readonly LyricsTimeline Empty = new([], [], []);

    private readonly long[] _starts;
    private readonly int[] _lineOf;
    private readonly int[] _wordOf;

    private LyricsTimeline(long[] starts, int[] lineOf, int[] wordOf)
    {
        _starts = starts;
        _lineOf = lineOf;
        _wordOf = wordOf;
    }

    public int Count => _starts.Length;

    /// <summary>The document line index that entry <paramref name="index"/> belongs to.</summary>
    public int LineOf(int index) => _lineOf[index];

    /// <summary>The word index within its line, or 0 for a line highlighted as a single block.</summary>
    public int WordOf(int index) => _wordOf[index];

    /// <summary>
    /// Flatten a document. Entries come out in document order and therefore ascending in time.
    /// </summary>
    /// <remarks>
    /// Three cases, and the second is the one that is easy to miss:
    /// <list type="bullet">
    /// <item>An <b>untimed line</b> contributes nothing. Blank separators and section markers are
    /// kept in the document with null times on purpose - treating them as zero would light every
    /// heading during the intro.</item>
    /// <item>A <b>timed line whose words are not timed</b> contributes ONE entry covering the
    /// whole line, so it highlights as a block. The aligner can place a line without placing its
    /// words, and dropping those lines would silently lose them.</item>
    /// <item>Otherwise, one entry per timed word.</item>
    /// </list>
    /// </remarks>
    public static LyricsTimeline Build(LyricsTimingsDocument? document)
    {
        if (document?.Lines is not { Count: > 0 })
        {
            return Empty;
        }

        var starts = new List<long>();
        var lineOf = new List<int>();
        var wordOf = new List<int>();

        for (var lineIndex = 0; lineIndex < document.Lines.Count; lineIndex++)
        {
            var line = document.Lines[lineIndex];
            if (!line.IsTimed)
            {
                continue;
            }

            var timedWords = 0;
            for (var wordIndex = 0; wordIndex < line.Words.Count; wordIndex++)
            {
                var word = line.Words[wordIndex];
                if (!word.IsTimed)
                {
                    continue;
                }

                starts.Add(word.StartMs!.Value);
                lineOf.Add(lineIndex);
                wordOf.Add(wordIndex);
                timedWords++;
            }

            if (timedWords == 0)
            {
                // Word index 0 matches what the renderer draws for such a line: one span covering
                // the whole text. The two encodings have to agree or the highlight finds nothing.
                starts.Add(line.StartMs!.Value);
                lineOf.Add(lineIndex);
                wordOf.Add(0);
            }
        }

        return starts.Count == 0
            ? Empty
            : new LyricsTimeline([.. starts], [.. lineOf], [.. wordOf]);
    }

    /// <summary>
    /// The entry being sung at <paramref name="atMs"/>, or -1 before the first one starts.
    /// </summary>
    /// <param name="previousIndex">
    /// The answer from the last call, which is almost always still the answer or one before it.
    /// Pass -1 if there isn't one.
    /// </param>
    /// <remarks>
    /// <b>The active entry is the last one that has STARTED - end times are never consulted.</b>
    /// That is not an oversight, it is what keeps a word lit through the gap before the next one
    /// begins. Checking whether the clock is still inside the current word's span would make the
    /// highlight blink out between lines and during instrumental breaks.
    /// </remarks>
    public int IndexAt(long atMs, int previousIndex)
    {
        if (_starts.Length == 0)
        {
            return -1;
        }

        // The common case by far: time moved forward a little, so the answer is here or just
        // after. Walking beats a binary search when the step is a word or two.
        if (previousIndex >= 0 && previousIndex < _starts.Length && atMs >= _starts[previousIndex])
        {
            var index = previousIndex;
            while (index + 1 < _starts.Length && _starts[index + 1] <= atMs)
            {
                index++;
            }

            return index;
        }

        return Search(atMs);
    }

    /// <summary>The last index whose start is at or before <paramref name="atMs"/>, else -1.</summary>
    private int Search(long atMs)
    {
        var low = 0;
        var high = _starts.Length - 1;
        var found = -1;

        while (low <= high)
        {
            var mid = (low + high) >> 1;
            if (_starts[mid] <= atMs)
            {
                found = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return found;
    }
}
