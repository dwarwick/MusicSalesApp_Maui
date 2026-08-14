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
    public long? PriceAmountMicros { get; init; }
    public string? PriceCurrencyCode { get; init; }
    public string? FormattedPrice { get; init; }

    public static BillingPurchaseVerificationRequest ForGooglePlay(
        string purchaseToken,
        string? orderId,
        long? priceAmountMicros = null,
        string? priceCurrencyCode = null,
        string? formattedPrice = null)
        => new()
        {
            Provider = BillingProviders.GooglePlay,
            PurchaseToken = purchaseToken,
            OrderId = orderId,
            PriceAmountMicros = priceAmountMicros,
            PriceCurrencyCode = priceCurrencyCode,
            FormattedPrice = formattedPrice
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

public sealed class SubscriptionOfferInfo
{
    public bool LookupSucceeded { get; init; }
    public bool IsAvailable { get; init; }
    public bool HasFreeTrial { get; init; }
    public int? FreeTrialDays { get; init; }
    public string? OfferToken { get; init; }
    public string? RenewalPrice { get; init; }
    public string? ErrorMessage { get; init; }

    public static SubscriptionOfferInfo None { get; } = new();
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
    /// Returns null when the store answered and the user owns nothing, and a result with
    /// <see cref="BillingPurchaseResult.BillingUnavailable"/> set when the store could not be
    /// reached — those two must stay distinguishable, because only the latter is worth retrying.
    /// </summary>
    Task<BillingPurchaseResult?> RestorePurchaseAsync();

    Task<SubscriptionOfferInfo> GetSubscriptionOfferAsync();

    /// <summary>
    /// Connects to platform billing. Safe to call from anywhere, any number of times: callers
    /// share a single connection attempt, so startup ordering does not affect correctness.
    /// </summary>
    Task InitializeAsync();

    /// <summary>
    /// Records a purchase the store delivered when nothing was waiting for one — an interrupted
    /// purchase replayed at launch, say. Returns true once the server has it, which is what makes it
    /// safe to finish the transaction; false means it must stay queued for a later attempt.
    ///
    /// A callback rather than a direct dependency because the verification path runs through
    /// AuthService, which already depends on this service — injecting it the other way round would
    /// close a cycle. AuthService assigns this at construction.
    /// </summary>
    Func<BillingPurchaseVerificationRequest, Task<bool>>? UnverifiedPurchaseHandler { get; set; }
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
    public long? PriceAmountMicros { get; init; }
    public string? PriceCurrencyCode { get; init; }
    public string? FormattedPrice { get; init; }
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// True when the platform store could not be reached at all, so this result carries no
    /// information about what the user owns. Callers must not read it as "nothing owned" — a
    /// restore that could not ask is retried, one that asked and got nothing is not.
    /// </summary>
    public bool BillingUnavailable { get; init; }

    public static BillingPurchaseResult Succeeded(
        string purchaseToken,
        string? orderId,
        long? priceAmountMicros = null,
        string? priceCurrencyCode = null,
        string? formattedPrice = null)
        => new()
        {
            Success = true,
            Provider = BillingProviders.GooglePlay,
            PurchaseToken = purchaseToken,
            OrderId = orderId,
            PriceAmountMicros = priceAmountMicros,
            PriceCurrencyCode = priceCurrencyCode,
            FormattedPrice = formattedPrice
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
            AppAccountToken = verificationRequest.AppAccountToken,
            PriceAmountMicros = verificationRequest.PriceAmountMicros,
            PriceCurrencyCode = verificationRequest.PriceCurrencyCode,
            FormattedPrice = verificationRequest.FormattedPrice
        };

    public static BillingPurchaseResult Failed(string error)
        => new() { Success = false, ErrorMessage = error };

    /// <summary>
    /// The platform store could not be reached, so the question went unanswered.
    /// Distinct from <see cref="Failed"/>, which means the store answered and refused.
    /// </summary>
    public static BillingPurchaseResult Unavailable(string error)
        => new() { Success = false, BillingUnavailable = true, ErrorMessage = error };

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
            AppAccountToken = AppAccountToken,
            PriceAmountMicros = PriceAmountMicros,
            PriceCurrencyCode = PriceCurrencyCode,
            FormattedPrice = FormattedPrice
        };
}
