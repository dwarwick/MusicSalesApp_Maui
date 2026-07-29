using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.ViewModels;

/// <summary>
/// Applies a thumbs-up/down tap optimistically, then reconciles with the server.
///
/// Shared by every ViewModel that hosts the buttons so the four of them cannot drift. The optimistic
/// step is what makes the buttons feel responsive offline, where the intent is queued rather than sent.
/// </summary>
public static class OptimisticLikeStateUpdater
{
    /// <param name="applyServerCounts">
    /// False for the library, which deliberately leaves counts to the SignalR broadcast so every open
    /// screen updates together rather than only the one that was tapped.
    /// </param>
    public static async Task ApplyAsync(
        IMusicService musicService,
        SongDto song,
        LikeAction action,
        bool applyServerCounts = true)
    {
        var previousState = song.UserLikeStatus;
        var previousLikeCount = song.LikeCount;
        var previousDislikeCount = song.DislikeCount;

        var change = LikeStateTransition.Apply(previousState, action);

        // Optimistic: the button fills in immediately, offline included.
        song.UserLikeStatus = change.DesiredState;
        song.LikeCount = Math.Max(0, previousLikeCount + change.LikeCountDelta);
        song.DislikeCount = Math.Max(0, previousDislikeCount + change.DislikeCountDelta);

        var outcome = await musicService.SetLikeStateAsync(song.Id, change.DesiredState);

        if (outcome.Result != null)
        {
            song.UserLikeStatus = outcome.Result.UserLikeStatus;

            // Null counts mean the server applied the state but had none to report, so the optimistic
            // values stand. Overwriting them with a default would blank the visible totals.
            if (applyServerCounts)
            {
                if (outcome.Result.LikeCount is { } likeCount)
                {
                    song.LikeCount = likeCount;
                }

                if (outcome.Result.DislikeCount is { } dislikeCount)
                {
                    song.DislikeCount = dislikeCount;
                }
            }

            return;
        }

        if (outcome.Queued)
        {
            // Keep the optimistic values; the queue will reconcile them when connectivity returns.
            return;
        }

        // Non-retryable failure - put the visible state back rather than leaving a lie on screen.
        song.UserLikeStatus = previousState;
        song.LikeCount = previousLikeCount;
        song.DislikeCount = previousDislikeCount;
    }
}
