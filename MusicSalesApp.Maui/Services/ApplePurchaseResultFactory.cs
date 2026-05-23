namespace MusicSalesApp.Maui.Services;

public static class ApplePurchaseResultFactory
{
    public static BillingPurchaseResult CreateSuccess(
        string transactionId,
        string? originalTransactionId,
        string productId,
        string? appAccountToken)
    {
        var effectiveOriginalTransactionId = string.IsNullOrWhiteSpace(originalTransactionId)
            ? transactionId
            : originalTransactionId;

        return BillingPurchaseResult.Succeeded(
            BillingPurchaseVerificationRequest.ForApple(
                transactionId,
                effectiveOriginalTransactionId,
                productId,
                appAccountToken));
    }
}