using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.ViewModels;

public readonly record struct SubscriptionBannerDisplay(bool IsVisible, string Text);

/// <summary>
/// Decides whether the "we couldn't confirm your subscription" banner is shown, and what it says.
///
/// Lives here rather than in the two ViewModels because the rule was duplicated verbatim in both
/// and they had already drifted apart in what they consider. It is deliberately a pure function of
/// three inputs so it can be tested without a ViewModel, and so Home and Account Settings can never
/// disagree about what the user is told.
/// </summary>
public static class SubscriptionBannerDisplayBuilder
{
    public static SubscriptionBannerDisplay Create(
        bool isSignedIn,
        SubscriptionVerificationState verification,
        bool isOffline)
    {
        // A signed-out user has no subscription to confirm, so there is nothing to explain. Without
        // this, a fresh install sat at the default Unverified and told someone who had never signed
        // in that their subscription features were paused - which is what this banner was reported
        // for. Note this hides a message, it does not grant anything: entitlement is decided by
        // HasActiveSubscription (see PreviewAccessPolicy), never by the verification state.
        if (!isSignedIn)
        {
            return new SubscriptionBannerDisplay(false, string.Empty);
        }

        if (verification == SubscriptionVerificationState.Verified)
        {
            return new SubscriptionBannerDisplay(false, string.Empty);
        }

        // The offline branch is copy only, so it uses IsOffline rather than HasNoNetworkAccess -
        // being wrong in the pessimistic direction here costs a hedge word, while claiming "you're
        // offline" to someone on wifi reads as a bug in the app.
        if (verification == SubscriptionVerificationState.Cached)
        {
            return new SubscriptionBannerDisplay(
                true,
                isOffline
                    ? "You're offline, so this is your subscription as we last confirmed it. It'll refresh automatically when you reconnect."
                    : "This is your subscription as we last confirmed it — we haven't been able to reach the server since. It'll refresh automatically.");
        }

        return new SubscriptionBannerDisplay(
            true,
            isOffline
                ? "We couldn't confirm your subscription, so subscription features are paused. You appear to be offline — reconnect and your subscription will be restored automatically."
                : "We couldn't confirm your subscription, so subscription features are paused. We're still trying to reach the server, and it'll be restored as soon as we can.");
    }
}
