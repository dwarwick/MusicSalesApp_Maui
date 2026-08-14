using Microsoft.Extensions.Logging;

namespace MusicSalesApp.Maui.Services;

/// <summary>
/// Bounds a wait on a platform store callback.
///
/// Both stores answer through callbacks that normally arrive in well under a second, and the point
/// of the bound is that one which never arrives must not hold a caller forever. Google Play has had
/// these bounds since the billing hardening work; the App Store side awaited its StoreKit callbacks
/// with no bound at all, so a device where StoreKit never answered wedged session restore - and
/// with it everything sequenced after it.
///
/// On timeout the underlying callback is left to arrive whenever it likes: its TrySetResult finds
/// the task already settled, so the only cost is that this caller stops waiting.
/// </summary>
public static class BillingCallbackTimeout
{
    /// <summary>Bound for a non-interactive store query (product lookup). Matches Google Play's.</summary>
    public static readonly TimeSpan DefaultQueryTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Bound for a restore. Deliberately not the query bound: Google Play's restore is a silent
    /// purchase query, but StoreKit's can put an Apple ID sign-in prompt in front of the user, and
    /// timing out while they are typing would discard a real restored transaction. Long enough to
    /// outlast that prompt, short enough that a store which never answers still lets go.
    /// </summary>
    public static readonly TimeSpan DefaultRestoreTimeout = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Bound for a purchase. Far longer than the query bound because it is waiting on a person
    /// reading a payment sheet — but not unbounded, because the callback genuinely may never arrive.
    /// Matches Google Play's.
    /// </summary>
    public static readonly TimeSpan DefaultPurchaseFlowTimeout = TimeSpan.FromMinutes(10);

    public static async Task<T> WaitAsync<T>(
        Task<T> callback,
        TimeSpan timeout,
        Func<T> onTimedOut,
        string description,
        ILogger? logger = null)
    {
        try
        {
            return await callback.WaitAsync(timeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            logger?.LogWarning(
                "The store did not answer the {Description} within {TimeoutSeconds}s",
                description,
                timeout.TotalSeconds);
            return onTimedOut();
        }
    }
}
