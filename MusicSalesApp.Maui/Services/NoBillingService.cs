namespace MusicSalesApp.Maui.Services;

/// <summary>
/// No-op billing service for non-Android platforms (iOS, Windows, macOS).
/// Google Play Billing is only available on Android.
/// </summary>
public class NoBillingService : IBillingService
{
    public Task InitializeAsync() => Task.CompletedTask;

    public Task<BillingPurchaseResult> PurchaseSubscriptionAsync()
        => Task.FromResult(BillingPurchaseResult.Failed("Subscriptions are only available on Android via Google Play."));

    public Task<BillingPurchaseResult?> RestorePurchaseAsync()
        => Task.FromResult<BillingPurchaseResult?>(null);
}
