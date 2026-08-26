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
    public static async Task<LikeApplyOutcome> ApplyAsync(
        IMusicService musicService,
        SongDto song,
        LikeAction action)
    {
        var previousState = song.UserLikeStatus;
        var previousLikeCount = song.LikeCount;
        var previousDislikeCount = song.DislikeCount;

        var change = LikeStateTransition.Apply(previousState, action);

        // Setting an opinion requires having streamed the song; clearing one never does. Checked before
        // the optimistic write so the button does not fill in and snap back, and checked here rather
        // than in each of the four callers so they cannot drift.
        if (change.DesiredState != null && !song.CanRate)
        {
            return LikeApplyOutcome.NeedsStream;
        }

        // Optimistic: the button fills in immediately, offline included.
        song.UserLikeStatus = change.DesiredState;
        song.LikeCount = Math.Max(0, previousLikeCount + change.LikeCountDelta);
        song.DislikeCount = Math.Max(0, previousDislikeCount + change.DislikeCountDelta);

        var outcome = await musicService.SetLikeStateAsync(song.Id, change.DesiredState);

        if (outcome.Result != null)
        {
            song.UserLikeStatus = outcome.Result.UserLikeStatus;

            // The server's counts win whenever it sends them.
            //
            // The optimistic value is previousCount plus a delta derived from previousState, so it is
            // only right when the local state was right. It is not always right: the state fetch can
            // fail, or the same account can rate the song on another device between loads. Then the
            // delta is computed from a wrong baseline, and because the set-state endpoint is idempotent
            // the server writes nothing and - deliberately - broadcasts nothing, so no later update ever
            // corrects it. The count creeps up by one on every redundant tap and never comes back down,
            // while the server and every other client still show the truth.
            //
            // Deferring to the broadcast still does its job for OTHER open screens; it just cannot be
            // the only correction for this one.
            //
            // Null counts mean the server applied the state but had none to report, so the optimistic
            // values stand. Overwriting them with a default would blank the visible totals.
            if (outcome.Result.LikeCount is { } likeCount)
            {
                song.LikeCount = likeCount;
            }

            if (outcome.Result.DislikeCount is { } dislikeCount)
            {
                song.DislikeCount = dislikeCount;
            }

            return LikeApplyOutcome.Handled;
        }

        if (outcome.Queued)
        {
            // Keep the optimistic values; the queue will reconcile them when connectivity returns.
            return LikeApplyOutcome.Handled;
        }

        // Non-retryable failure - put the visible state back rather than leaving a lie on screen.
        song.UserLikeStatus = previousState;
        song.LikeCount = previousLikeCount;
        song.DislikeCount = previousDislikeCount;

        if (!outcome.NeedsStream)
        {
            return LikeApplyOutcome.Handled;
        }

        // The server disagreed with what we believed about eligibility - it is the authority, so correct
        // the local view. The user gets the same explanation either way.
        song.HasStreamed = false;
        return LikeApplyOutcome.NeedsStream;
    }
}
