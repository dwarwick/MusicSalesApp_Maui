namespace MusicSalesApp.Maui.Services;

/// <summary>
/// What the app knew about a session at the moment its token expired, kept just long enough to
/// explain the sign-out to the user.
///
/// There is no refresh-token flow, so an expired JWT ends the session silently at startup. Before
/// this existed the only visible consequence was the subscription banner switching on, which told a
/// signed-out user their subscription "could not be confirmed" and would restore itself on reconnect
/// — wrong on both counts. This carries the one fact needed to word the sign-out correctly instead.
///
/// Deliberately holds no identity: it is read from the outgoing user's cached entitlement snapshot
/// immediately before that snapshot is deleted, and it lives in memory only, so it cannot leak the
/// previous account's details into the next one.
/// </summary>
/// <param name="HadConfirmedEntitlement">
/// Whether the last status the server confirmed carried a subscription or a trial. False both for a
/// user who never subscribed and for one whose snapshot is too old to speak for.
/// </param>
/// <param name="EntitlementEndDate">When that entitlement ran, or runs, out. Null when unknown.</param>
public sealed record SessionExpiryNotice(bool HadConfirmedEntitlement, DateTime? EntitlementEndDate)
{
    /// <summary>
    /// Whether the entitlement had already run out by <paramref name="utcNow"/>. Separates "your
    /// subscription is waiting for you" from "your subscription needs renewing" — offering to restore
    /// one that has lapsed is a promise signing in will not keep. False when there was no entitlement
    /// or no end date to judge it by, which leaves the caller on the non-committal wording.
    ///
    /// Dates arriving from a parsed snapshot can carry <see cref="DateTimeKind.Unspecified"/>; they
    /// are read as UTC, matching how <see cref="CachedSubscriptionStatus"/> writes them. Reading them
    /// as local would shift every comparison by the device's offset.
    /// </summary>
    public bool HasLapsedBy(DateTime utcNow)
    {
        if (!HadConfirmedEntitlement || EntitlementEndDate is not { } endDate)
        {
            return false;
        }

        var endUtc = endDate.Kind switch
        {
            DateTimeKind.Utc => endDate,
            DateTimeKind.Local => endDate.ToUniversalTime(),
            _ => DateTime.SpecifyKind(endDate, DateTimeKind.Utc)
        };

        return endUtc <= utcNow;
    }
}
