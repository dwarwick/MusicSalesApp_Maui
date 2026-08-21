#nullable enable
namespace MusicSalesApp.Maui.Services;

/// <summary>What, if anything, to do to put the player on the song it should be playing.</summary>
internal enum QueueCorrectionKind
{
    /// <summary>The player is already on the right song. Its index does not matter.</summary>
    AlreadyCorrect,

    /// <summary>The player is on the wrong song; seek to <see cref="QueueCorrection.SeekIndex"/>.</summary>
    SeekTo,

    /// <summary>The player's playlist does not contain the song at all. Nothing to seek to.</summary>
    NotPresent,
}

/// <summary>The decision, and the index to seek to when there is one.</summary>
internal readonly record struct QueueCorrection(QueueCorrectionKind Kind, int SeekIndex)
{
    public static QueueCorrection AlreadyCorrect { get; } = new(QueueCorrectionKind.AlreadyCorrect, -1);

    public static QueueCorrection NotPresent { get; } = new(QueueCorrectionKind.NotPresent, -1);

    public static QueueCorrection SeekTo(int index) => new(QueueCorrectionKind.SeekTo, index);
}

/// <summary>
/// Reconciles what the player is holding with what the app believes it submitted.
/// </summary>
/// <remarks>
/// <para>
/// Extracted from the Android runtime because it is where a production fault lived twice, and
/// because none of it needs a player: it is two decisions over lists of media ids. The runtime
/// reads the ids off ExoPlayer and acts on the answer; everything hard is here, where it can be
/// tested. Ids are the per-song stable cache key, so two entries are the same song exactly when
/// their ids match.
/// </para>
/// <para>
/// The fault both decisions caused: leaving the home page's featured queue, where a song is first,
/// for the artist queue, where the same song is second. Playback continued on the correct song at
/// its old index while the app labelled it by the new one - so the title, duration and lyrics on
/// screen all belonged to a different song from the audio.
/// </para>
/// </remarks>
internal static class PlaybackQueueAlignment
{
    /// <summary>
    /// Whether the player is really holding the playlist that was submitted, item for item.
    /// </summary>
    /// <remarks>
    /// Compares ids in order. The check this replaces compared COUNTS, which cannot distinguish two
    /// different queues of the same length - and swapping a two-song featured queue for a two-song
    /// artist queue is exactly that. A submission that left the old order in place counted two of
    /// two and passed, after which the player held one order and the app held another.
    /// </remarks>
    public static bool QueueMatches(IReadOnlyList<string?> submittedIds, IReadOnlyList<string?> playerIds)
    {
        if (submittedIds.Count != playerIds.Count)
        {
            return false;
        }

        for (var index = 0; index < submittedIds.Count; index++)
        {
            if (!string.Equals(submittedIds[index], playerIds[index], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Decide how to put the player on <paramref name="expectedId"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Identity first, index never. If the player is already on the right song then it is right,
    /// whatever index it reports - and returning <see cref="QueueCorrectionKind.AlreadyCorrect"/>
    /// there is the whole point of this method. The code it replaces compared the player's index
    /// against the index the app wanted and seeked on a mismatch, so a player correctly playing the
    /// right song at its old index was dragged onto whatever sat at that index in the new ordering.
    /// The correction caused the fault it looked like it was repairing.
    /// </para>
    /// <para>
    /// When the player really is on the wrong song, the seek target is where that song sits in the
    /// PLAYER's list, not where the app assumes it sits. Those differ precisely when this is worth
    /// calling.
    /// </para>
    /// </remarks>
    public static QueueCorrection ResolveCorrection(
        string? expectedId,
        string? currentId,
        IReadOnlyList<string?> playerIds)
    {
        if (string.IsNullOrEmpty(expectedId))
        {
            return QueueCorrection.NotPresent;
        }

        if (string.Equals(expectedId, currentId, StringComparison.Ordinal))
        {
            return QueueCorrection.AlreadyCorrect;
        }

        for (var index = 0; index < playerIds.Count; index++)
        {
            if (string.Equals(expectedId, playerIds[index], StringComparison.Ordinal))
            {
                return QueueCorrection.SeekTo(index);
            }
        }

        return QueueCorrection.NotPresent;
    }
}
