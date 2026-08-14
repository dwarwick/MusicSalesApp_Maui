namespace MusicSalesApp.Maui.Services;

/// <summary>
/// No-op billing service for non-Android platforms (iOS, Windows, macOS).
/// Google Play Billing is only available on Android.
/// </summary>
public class NoBillingService : IBillingService
{
    /// <summary>Never invoked: this service has no store to deliver an unsolicited purchase.</summary>
    public Func<BillingPurchaseVerificationRequest, Task<bool>>? UnverifiedPurchaseHandler { get; set; }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task<BillingPurchaseResult> PurchaseSubscriptionAsync()
        => Task.FromResult(BillingPurchaseResult.Failed("Subscriptions are only available on Android via Google Play."));

    public Task<BillingPurchaseResult?> RestorePurchaseAsync()
        => Task.FromResult<BillingPurchaseResult?>(null);

    public Task<SubscriptionOfferInfo> GetSubscriptionOfferAsync()
        => Task.FromResult(SubscriptionOfferInfo.None);
}
