namespace MusicSalesApp.Maui.Services;

/// <summary>
/// Abstracts Google Play Billing for subscription purchases.
/// Platform implementations live under Platforms/Android/.
/// </summary>
public interface IBillingService
{
    /// <summary>
    /// Initiates the Google Play subscription purchase flow.
    /// Returns the result of the purchase attempt.
    /// </summary>
    Task<BillingPurchaseResult> PurchaseSubscriptionAsync();

    /// <summary>
    /// Checks whether the user has an active Google Play subscription
    /// that hasn't been sent to the server yet (e.g., after app reinstall).
    /// </summary>
    Task<BillingPurchaseResult?> RestorePurchaseAsync();

    /// <summary>
    /// Connects to Google Play Billing. Called once at app startup.
    /// </summary>
    Task InitializeAsync();
}

/// <summary>
/// Result of a Google Play Billing purchase or restore operation.
/// </summary>
public class BillingPurchaseResult
{
    public bool Success { get; init; }
    public string? PurchaseToken { get; init; }
    public string? OrderId { get; init; }
    public string? ErrorMessage { get; init; }

    public static BillingPurchaseResult Succeeded(string purchaseToken, string? orderId)
        => new() { Success = true, PurchaseToken = purchaseToken, OrderId = orderId };

    public static BillingPurchaseResult Failed(string error)
        => new() { Success = false, ErrorMessage = error };

    public static BillingPurchaseResult Cancelled()
        => new() { Success = false, ErrorMessage = "Purchase was cancelled." };
}
