using Android.App;
using Android.BillingClient.Api;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.Platforms.Android;

/// <summary>
/// Google Play Billing integration using the native BillingClient SDK.
/// Handles subscription purchase flow and purchase restoration.
/// </summary>
public class GooglePlayBillingService : Java.Lang.Object, IBillingService, IPurchasesUpdatedListener
{
    // PurchaseState is an enum in the Xamarin binding
    // PurchaseState.Purchased = purchased, PurchaseState.Pending = pending

    private readonly ILogger<GooglePlayBillingService> _logger;
    private readonly string _productId;
    private BillingClient? _billingClient;
    private TaskCompletionSource<BillingPurchaseResult>? _purchaseTcs;
    private ProductDetails? _subscriptionProductDetails;

    public GooglePlayBillingService(IConfiguration configuration, ILogger<GooglePlayBillingService> logger)
    {
        _logger = logger;
        _productId = configuration["GooglePlay:SubscriptionProductId"] ?? "streamtunes_monthly_sub";
    }

    public async Task InitializeAsync()
    {
        if (_billingClient != null)
            return;

        var activity = Platform.CurrentActivity;
        if (activity == null)
        {
            _logger.LogWarning("Cannot initialize BillingClient — no current activity");
            return;
        }

        _billingClient = BillingClient.NewBuilder(activity)
            .SetListener(this)
            .EnablePendingPurchases(PendingPurchasesParams.NewBuilder()
                .EnableOneTimeProducts()
                .EnablePrepaidPlans()
                .Build())
            .Build();

        await ConnectAsync();
    }

    public async Task<BillingPurchaseResult> PurchaseSubscriptionAsync()
    {
        var activity = Platform.CurrentActivity;
        if (activity == null)
            return BillingPurchaseResult.Failed("No active Android activity.");

        if (_billingClient == null || !_billingClient.IsReady)
        {
            await InitializeAsync();
            if (_billingClient == null || !_billingClient.IsReady)
                return BillingPurchaseResult.Failed("Could not connect to Google Play Billing.");
        }

        // Query for the subscription product details if not cached
        if (_subscriptionProductDetails == null)
        {
            var queryResult = await QuerySubscriptionProductAsync();
            if (queryResult != null)
                return queryResult; // Error result
        }

        // Find the first offer (base plan)
        var offerDetails = _subscriptionProductDetails!.GetSubscriptionOfferDetails();
        if (offerDetails == null || offerDetails.Count == 0)
            return BillingPurchaseResult.Failed("No subscription offers available.");

        var offerToken = offerDetails[0].OfferToken;

        // Build the billing flow params
        var productDetailsParams = BillingFlowParams.ProductDetailsParams.NewBuilder()
            .SetProductDetails(_subscriptionProductDetails)
            .SetOfferToken(offerToken)
            .Build();

        var flowParams = BillingFlowParams.NewBuilder()
            .SetProductDetailsParamsList([productDetailsParams])
            .Build();

        // Create TCS to await the result from OnPurchasesUpdated callback
        _purchaseTcs = new TaskCompletionSource<BillingPurchaseResult>();

        var responseCode = _billingClient.LaunchBillingFlow(activity, flowParams);
        if (responseCode.ResponseCode != BillingResponseCode.Ok)
        {
            _purchaseTcs = null;
            return BillingPurchaseResult.Failed($"Failed to launch billing flow (code: {responseCode.ResponseCode}).");
        }

        // Wait for the OnPurchasesUpdated callback
        return await _purchaseTcs.Task;
    }

    public async Task<BillingPurchaseResult?> RestorePurchaseAsync()
    {
        if (_billingClient == null || !_billingClient.IsReady)
        {
            await InitializeAsync();
            if (_billingClient == null || !_billingClient.IsReady)
                return null;
        }

        var queryParams = QueryPurchasesParams.NewBuilder()
            .SetProductType(BillingClient.ProductType.Subs)
            .Build();

        var result = await _billingClient.QueryPurchasesAsync(queryParams);
        if (result == null)
            return null;

        var purchases = result.Purchases;
        if (purchases == null || purchases.Count == 0)
            return null;

        // Find an active subscription
        foreach (var purchase in purchases)
        {
            if (purchase.PurchaseState == PurchaseState.Purchased)
            {
                // Acknowledge if needed
                if (!purchase.IsAcknowledged)
                {
                    await AcknowledgePurchaseAsync(purchase.PurchaseToken);
                }

                return BillingPurchaseResult.Succeeded(purchase.PurchaseToken, purchase.OrderId);
            }
        }

        return null;
    }

    /// <summary>
    /// Callback from BillingClient when a purchase flow completes.
    /// </summary>
    public void OnPurchasesUpdated(BillingResult billingResult, IList<Purchase>? purchases)
    {
        if (_purchaseTcs == null)
            return;

        if (billingResult.ResponseCode == BillingResponseCode.Ok && purchases?.Count > 0)
        {
            var purchase = purchases[0];
            if (purchase.PurchaseState == PurchaseState.Purchased)
            {
                // Acknowledge in background — don't block the callback
                _ = AcknowledgePurchaseAsync(purchase.PurchaseToken);

                _purchaseTcs.TrySetResult(BillingPurchaseResult.Succeeded(
                    purchase.PurchaseToken, purchase.OrderId));
            }
            else if (purchase.PurchaseState == PurchaseState.Pending)
            {
                _purchaseTcs.TrySetResult(BillingPurchaseResult.Failed(
                    "Purchase is pending. You'll get access once payment completes."));
            }
            else
            {
                _purchaseTcs.TrySetResult(BillingPurchaseResult.Failed("Purchase was not completed."));
            }
        }
        else if (billingResult.ResponseCode == BillingResponseCode.UserCancelled)
        {
            _purchaseTcs.TrySetResult(BillingPurchaseResult.Cancelled());
        }
        else
        {
            _purchaseTcs.TrySetResult(BillingPurchaseResult.Failed(
                $"Purchase failed: {billingResult.DebugMessage} (code: {billingResult.ResponseCode})"));
        }
    }

    private async Task ConnectAsync()
    {
        if (_billingClient == null)
            return;

        var tcs = new TaskCompletionSource<bool>();

        _billingClient.StartConnection(new BillingClientStateListener(
            onConnected: () =>
            {
                _logger.LogInformation("Connected to Google Play Billing");
                tcs.TrySetResult(true);
            },
            onDisconnected: () =>
            {
                _logger.LogWarning("Disconnected from Google Play Billing");
                tcs.TrySetResult(false);
            }));

        await tcs.Task;
    }

    private async Task<BillingPurchaseResult?> QuerySubscriptionProductAsync()
    {
        var productList = new List<QueryProductDetailsParams.Product>
        {
            QueryProductDetailsParams.Product.NewBuilder()
                .SetProductId(_productId)
                .SetProductType(BillingClient.ProductType.Subs)
                .Build()
        };

        var queryParams = QueryProductDetailsParams.NewBuilder()
            .SetProductList(productList)
            .Build();

        var result = await _billingClient!.QueryProductDetailsAsync(queryParams);
        if (result == null || result.Result.ResponseCode != BillingResponseCode.Ok)
        {
            return BillingPurchaseResult.Failed($"Failed to query product details (code: {result?.Result.ResponseCode}).");
        }

        var productDetailsList = result.ProductDetails;
        if (productDetailsList == null || productDetailsList.Count == 0)
        {
            return BillingPurchaseResult.Failed($"Subscription product '{_productId}' not found in Google Play.");
        }

        _subscriptionProductDetails = productDetailsList[0];
        return null; // Success — no error
    }

    private async Task AcknowledgePurchaseAsync(string purchaseToken)
    {
        try
        {
            var ackParams = AcknowledgePurchaseParams.NewBuilder()
                .SetPurchaseToken(purchaseToken)
                .Build();

            var result = await _billingClient!.AcknowledgePurchaseAsync(ackParams);
            if (result.ResponseCode == BillingResponseCode.Ok)
            {
                _logger.LogInformation("Purchase acknowledged successfully");
            }
            else
            {
                _logger.LogWarning("Failed to acknowledge purchase: {Code} {Message}",
                    result.ResponseCode, result.DebugMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error acknowledging purchase");
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _billingClient != null)
        {
            _billingClient.EndConnection();
            _billingClient = null;
        }
        base.Dispose(disposing);
    }

    /// <summary>
    /// Helper class for BillingClient connection state callbacks.
    /// </summary>
    private class BillingClientStateListener : Java.Lang.Object, IBillingClientStateListener
    {
        private readonly Action _onConnected;
        private readonly Action _onDisconnected;

        public BillingClientStateListener(Action onConnected, Action onDisconnected)
        {
            _onConnected = onConnected;
            _onDisconnected = onDisconnected;
        }

        public void OnBillingServiceDisconnected()
        {
            _onDisconnected();
        }

        public void OnBillingSetupFinished(BillingResult billingResult)
        {
            if (billingResult.ResponseCode == BillingResponseCode.Ok)
                _onConnected();
            else
                _onDisconnected();
        }
    }
}
