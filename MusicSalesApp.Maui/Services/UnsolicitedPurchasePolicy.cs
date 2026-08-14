namespace MusicSalesApp.Maui.Services;

/// <summary>What to do with a store transaction that arrived without anyone waiting for it.</summary>
public enum UnsolicitedPurchaseAction
{
    /// <summary>Routine, and the server already knows: finish it and move on.</summary>
    Finish,

    /// <summary>Never recorded anywhere: verify with the server, then finish it.</summary>
    Verify,

    /// <summary>Cannot be attributed to an account right now: leave it queued for a later attempt.</summary>
    Hold
}

/// <summary>
/// Decides what to do with a purchase the store delivers when no purchase call is awaiting one.
///
/// The store re-delivers every unfinished transaction each time the observer is attached, and a
/// subscription renewal arrives this way too — nobody is "waiting" for a renewal, it simply happens.
/// Treating every such arrival as suspicious and refusing to finish it made renewals accumulate in
/// the queue forever: three had piled up within fifteen minutes of a sandbox purchase, and each app
/// launch replayed the whole backlog. Unfinished transactions are also replayed while a genuine
/// purchase is in flight, where one can settle that purchase with a stale transaction.
///
/// A renewal is identifiable: it carries the original transaction's ID alongside its own. The server
/// learns about renewals from App Store Server Notifications, so the client has nothing to add and
/// should just finish them.
///
/// What must not be finished blindly is a standalone purchase nobody has verified — that is a
/// customer who has paid with no record of it anywhere, which is the failure this whole area exists
/// to prevent.
/// </summary>
public static class UnsolicitedPurchasePolicy
{
    public static UnsolicitedPurchaseAction Decide(
        string? transactionId,
        string? originalTransactionId,
        bool canVerify)
    {
        if (IsRenewalOfAnExistingSubscription(transactionId, originalTransactionId))
        {
            return UnsolicitedPurchaseAction.Finish;
        }

        return canVerify ? UnsolicitedPurchaseAction.Verify : UnsolicitedPurchaseAction.Hold;
    }

    /// <summary>
    /// True when this transaction continues a subscription that started with a different one. The
    /// store reports the first purchase of a chain with either no original ID or its own, so a
    /// differing original ID is what distinguishes a renewal from a first purchase.
    /// </summary>
    private static bool IsRenewalOfAnExistingSubscription(string? transactionId, string? originalTransactionId)
        => !string.IsNullOrWhiteSpace(originalTransactionId)
           && !string.Equals(originalTransactionId, transactionId, StringComparison.Ordinal);
}
