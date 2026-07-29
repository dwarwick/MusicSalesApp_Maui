using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Services;

/// <summary>
/// Replays not-yet-sent like/dislike intents over songs restored from the offline catalog.
///
/// The snapshot holds the counts and status that were current when it was written, so without this a
/// thumbs-up tapped offline would visibly revert after an app restart even though it is still queued.
/// </summary>
public static class PendingLikeStateApplier
{
    public static void Apply(IReadOnlyList<SongDto> songs, IReadOnlyDictionary<int, bool?> pendingLikeStates)
    {
        if (pendingLikeStates.Count == 0)
        {
            return;
        }

        foreach (var song in songs)
        {
            if (!pendingLikeStates.TryGetValue(song.Id, out var desiredState))
            {
                continue;
            }

            if (song.UserLikeStatus == desiredState)
            {
                continue;
            }

            // Move the counts by the same delta the original tap applied, so the snapshot's totals stay
            // consistent with the status being shown.
            song.LikeCount = Math.Max(0, song.LikeCount + CountDelta(song.UserLikeStatus, desiredState, countsLikes: true));
            song.DislikeCount = Math.Max(0, song.DislikeCount + CountDelta(song.UserLikeStatus, desiredState, countsLikes: false));
            song.UserLikeStatus = desiredState;
        }
    }

    private static int CountDelta(bool? currentState, bool? desiredState, bool countsLikes)
    {
        var wasCounted = currentState == countsLikes;
        var isCounted = desiredState == countsLikes;

        if (wasCounted == isCounted)
        {
            return 0;
        }

        return isCounted ? 1 : -1;
    }
}
