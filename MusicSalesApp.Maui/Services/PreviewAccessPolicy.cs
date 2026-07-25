using MusicSalesApp.Common.Helpers;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Services;

/// <summary>
/// Single source of truth for "is this song limited to a 60-second preview?".
/// Mirrors the web app's bypass order (subscription → admin → featured song → creator's own song)
/// so playback, the player view models, and the progress-bar preview marker can never disagree.
/// </summary>
public static class PreviewAccessPolicy
{
    /// <summary>How much of a restricted song a listener may hear.</summary>
    public const double PreviewLimitSeconds = 60.0;

    /// <summary>
    /// Whether the user may hear any song in full, regardless of which song it is.
    /// Admins get full playback without a subscription, matching the web app.
    /// </summary>
    public static bool HasFullPlaybackAccess(IAuthService? authService)
    {
        if (authService == null)
        {
            return false;
        }

        return HasSubscriptionEntitlement(authService) || authService.IsAdmin;
    }

    /// <summary>Whether the user holds a subscription that still entitles them to full playback.</summary>
    public static bool HasSubscriptionEntitlement(IAuthService? authService)
    {
        if (authService?.HasActiveSubscription != true)
        {
            return false;
        }

        // A cancelled subscription keeps playing until its paid-through date, then stops.
        if (string.Equals(authService.SubscriptionStatus, SubscriptionStatuses.Cancelled, StringComparison.OrdinalIgnoreCase) &&
            authService.SubscriptionEndDate is { } endDate &&
            endDate <= DateTime.UtcNow)
        {
            return false;
        }

        return true;
    }

    /// <summary>Whether playback of <paramref name="song"/> must stop at <see cref="PreviewLimitSeconds"/>.</summary>
    public static bool ShouldLimitPreview(IAuthService? authService, SongDto? song)
    {
        if (song == null)
        {
            return false;
        }

        // Featured songs are full-length for everyone, including anonymous listeners.
        if (song.DisplayOnHomePage)
        {
            return false;
        }

        if (HasFullPlaybackAccess(authService))
        {
            return false;
        }

        // Creators always hear their own songs in full.
        if (authService?.IsCreator == true && song.CreatorUserId == authService.UserId)
        {
            return false;
        }

        return true;
    }
}
