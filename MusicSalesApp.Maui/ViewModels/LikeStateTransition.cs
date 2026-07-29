namespace MusicSalesApp.Maui.ViewModels;

public enum LikeAction
{
    ThumbsUp,
    ThumbsDown
}

/// <summary>
/// Result of a thumbs-up/down tap: the state the user is asking for, plus how the visible counts move
/// to match. Deltas are relative to the state before the tap.
/// </summary>
public readonly record struct LikeStateChange(bool? DesiredState, int LikeCountDelta, int DislikeCountDelta);

/// <summary>
/// Toggle semantics for the thumbs-up/down buttons, computed on the client.
///
/// The client has to own this now: the server's set-state endpoint is idempotent (that is what makes
/// an offline queue safe to replay), so "what should tapping this do" can no longer be inferred from
/// the server's response. Keeping it here means all four ViewModels agree and the rule is testable in
/// isolation.
/// </summary>
public static class LikeStateTransition
{
    public static LikeStateChange Apply(bool? currentState, LikeAction action)
    {
        // Tapping the button that is already active clears the opinion; anything else sets it.
        var desiredState = action switch
        {
            LikeAction.ThumbsUp => currentState == true ? null : (bool?)true,
            LikeAction.ThumbsDown => currentState == false ? null : (bool?)false,
            _ => currentState
        };

        return new LikeStateChange(
            desiredState,
            CountDelta(currentState, desiredState, countsLikes: true),
            CountDelta(currentState, desiredState, countsLikes: false));
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
