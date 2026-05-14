namespace MusicSalesApp.Maui.Services;

/// <summary>
/// String constants for subscription billing providers used by the MAUI app.
/// Keep these aligned with the server billing source values.
/// </summary>
public static class BillingProviders
{
    public const string GooglePlay = "GooglePlay";
    public const string Apple = "Apple";
}

/// <summary>
/// Provider-aware purchase payload that the MAUI app sends to the server for verification.
/// </summary>
public sealed class BillingPurchaseVerificationRequest
{
    public string Provider { get; init; } = string.Empty;
    public string? PurchaseToken { get; init; }
    public string? OrderId { get; init; }
    public string? TransactionId { get; init; }
    public string? OriginalTransactionId { get; init; }
    public string? ProductId { get; init; }
    public string? AppAccountToken { get; init; }

    public static BillingPurchaseVerificationRequest ForGooglePlay(string purchaseToken, string? orderId)
        => new()
        {
            Provider = BillingProviders.GooglePlay,
            PurchaseToken = purchaseToken,
            OrderId = orderId
        };

    public static BillingPurchaseVerificationRequest ForApple(
        string transactionId,
        string? originalTransactionId,
        string? productId,
        string? appAccountToken)
        => new()
        {
            Provider = BillingProviders.Apple,
            TransactionId = transactionId,
            OriginalTransactionId = originalTransactionId,
            ProductId = productId,
            AppAccountToken = appAccountToken
        };
}

/// <summary>
/// Abstracts platform billing for subscription purchases.
/// Platform implementations live under Platforms/Android/ and Platforms/iOS/.
/// </summary>
public interface IBillingService
{
    /// <summary>
    /// Initiates the platform subscription purchase flow.
    /// Returns the result of the purchase attempt.
    /// </summary>
    Task<BillingPurchaseResult> PurchaseSubscriptionAsync();

    /// <summary>
    /// Checks whether the user has an active platform subscription
    /// that hasn't been sent to the server yet (e.g., after app reinstall).
    /// </summary>
    Task<BillingPurchaseResult?> RestorePurchaseAsync();

    /// <summary>
    /// Connects to platform billing. Called once at app startup.
    /// </summary>
    Task InitializeAsync();
}

/// <summary>
/// Result of a billing purchase or restore operation.
/// </summary>
public class BillingPurchaseResult
{
    public bool Success { get; init; }
    public string Provider { get; init; } = string.Empty;
    public string? PurchaseToken { get; init; }
    public string? OrderId { get; init; }
    public string? TransactionId { get; init; }
    public string? OriginalTransactionId { get; init; }
    public string? ProductId { get; init; }
    public string? AppAccountToken { get; init; }
    public string? ErrorMessage { get; init; }

    public static BillingPurchaseResult Succeeded(string purchaseToken, string? orderId)
        => new()
        {
            Success = true,
            Provider = BillingProviders.GooglePlay,
            PurchaseToken = purchaseToken,
            OrderId = orderId
        };

    public static BillingPurchaseResult Succeeded(BillingPurchaseVerificationRequest verificationRequest)
        => new()
        {
            Success = true,
            Provider = verificationRequest.Provider,
            PurchaseToken = verificationRequest.PurchaseToken,
            OrderId = verificationRequest.OrderId,
            TransactionId = verificationRequest.TransactionId,
            OriginalTransactionId = verificationRequest.OriginalTransactionId,
            ProductId = verificationRequest.ProductId,
            AppAccountToken = verificationRequest.AppAccountToken
        };

    public static BillingPurchaseResult Failed(string error)
        => new() { Success = false, ErrorMessage = error };

    public static BillingPurchaseResult Cancelled()
        => new() { Success = false, ErrorMessage = "Purchase was cancelled." };

    public BillingPurchaseVerificationRequest ToVerificationRequest()
        => new()
        {
            Provider = Provider,
            PurchaseToken = PurchaseToken,
            OrderId = OrderId,
            TransactionId = TransactionId,
            OriginalTransactionId = OriginalTransactionId,
            ProductId = ProductId,
            AppAccountToken = AppAccountToken
        };
}
