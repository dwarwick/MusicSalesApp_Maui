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
    private readonly BillingConnectionGate _connectionGate;
    private readonly object _clientSync = new();
    private BillingClient? _billingClient;
    private TaskCompletionSource<BillingPurchaseResult>? _purchaseTcs;
    private ProductDetails? _subscriptionProductDetails;
    private long? _pendingRenewalPriceAmountMicros;
    private string? _pendingRenewalPriceCurrencyCode;
    private string? _pendingFormattedPrice;

    public GooglePlayBillingService(IConfiguration configuration, ILogger<GooglePlayBillingService> logger)
    {
        _logger = logger;
        _productId = configuration["GooglePlay:SubscriptionProductId"] ?? "streamtunes_monthly_sub";
        _connectionGate = new BillingConnectionGate(ConnectAsync, connectTimeout: null, logger: logger);
    }

    public Task InitializeAsync() => _connectionGate.EnsureConnectedAsync();

    public async Task<BillingPurchaseResult> PurchaseSubscriptionAsync()
    {
        var activity = Platform.CurrentActivity;
        if (activity == null)
            return BillingPurchaseResult.Failed("No active Android activity.");

        if (await ConnectedClientAsync() is not { } client)
            return BillingPurchaseResult.Failed("Could not connect to Google Play Billing.");

        // Query for the subscription product details if not cached
        if (_subscriptionProductDetails == null)
        {
            var queryResult = await QuerySubscriptionProductAsync(client);
            if (queryResult != null)
                return queryResult; // Error result
        }

        var offerDetails = _subscriptionProductDetails!.GetSubscriptionOfferDetails();
        if (offerDetails == null || offerDetails.Count == 0)
            return BillingPurchaseResult.Failed("No subscription offers available.");

        var purchaseOffer = FindFreeTrialOffer(offerDetails) ?? offerDetails[0];
        var offerToken = purchaseOffer.OfferToken;
        var renewalPricePhase = ResolveRenewalPricePhase(purchaseOffer);
        _pendingRenewalPriceAmountMicros = renewalPricePhase?.PriceAmountMicros;
        _pendingRenewalPriceCurrencyCode = renewalPricePhase?.PriceCurrencyCode;
        _pendingFormattedPrice = renewalPricePhase?.FormattedPrice;

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

        var responseCode = client.LaunchBillingFlow(activity, flowParams);
        if (responseCode.ResponseCode != BillingResponseCode.Ok)
        {
            _purchaseTcs = null;
            return BillingPurchaseResult.Failed(CreateBillingFailureMessage(responseCode));
        }

        // Wait for the OnPurchasesUpdated callback
        return await _purchaseTcs.Task;
    }

    public async Task<BillingPurchaseResult?> RestorePurchaseAsync()
    {
        // "We could not ask Google Play" must not be reported as "Google Play says you own
        // nothing" — the caller retries the first and accepts the second as final.
        if (await ConnectedClientAsync() is not { } client)
            return BillingPurchaseResult.Unavailable("Could not connect to Google Play Billing.");

        var queryParams = QueryPurchasesParams.NewBuilder()
            .SetProductType(BillingClient.ProductType.Subs)
            .Build();

        var result = await client.QueryPurchasesAsync(queryParams);
        if (result == null)
            return BillingPurchaseResult.Unavailable("Google Play returned no purchase query result.");

        // A query that failed comes back with an empty purchase list, which is indistinguishable
        // from a genuine "owns nothing" unless the response code is checked. Reading a failure as
        // "owns nothing" is what would strand a subscriber on the free tier.
        if (result.Result.ResponseCode != BillingResponseCode.Ok)
            return BillingPurchaseResult.Unavailable(CreateBillingFailureMessage(result.Result));

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
                    await AcknowledgePurchaseAsync(client, purchase.PurchaseToken);
                }

                return BillingPurchaseResult.Succeeded(purchase.PurchaseToken, purchase.OrderId);
            }
        }

        return null;
    }

    public async Task<SubscriptionOfferInfo> GetSubscriptionOfferAsync()
    {
        if (await ConnectedClientAsync() is not { } client)
            return SubscriptionOfferInfo.None;

        _subscriptionProductDetails = null;
        var queryResult = await QuerySubscriptionProductAsync(client);
        if (queryResult != null || _subscriptionProductDetails == null)
        {
            _logger.LogWarning("Google Play subscription offer lookup failed: {ErrorMessage}", queryResult?.ErrorMessage);
            return new SubscriptionOfferInfo
            {
                LookupSucceeded = false,
                ErrorMessage = queryResult?.ErrorMessage
            };
        }

        var offerDetails = _subscriptionProductDetails.GetSubscriptionOfferDetails();
        if (offerDetails == null || offerDetails.Count == 0)
        {
            return new SubscriptionOfferInfo
            {
                LookupSucceeded = true,
                IsAvailable = false,
                ErrorMessage = "No subscription offers available."
            };
        }

        var freeTrialOffer = FindFreeTrialOffer(offerDetails);
        var displayOffer = freeTrialOffer ?? offerDetails[0];
        var renewalPrice = ResolveRenewalPrice(displayOffer);
        var freeTrialDays = freeTrialOffer == null ? null : ResolveFreeTrialDays(freeTrialOffer);

        _logger.LogInformation(
            "Google Play subscription offer lookup succeeded. HasFreeTrial={HasFreeTrial}; FreeTrialDays={FreeTrialDays}; RenewalPrice={RenewalPrice}",
            freeTrialOffer != null,
            freeTrialDays,
            renewalPrice);

        return new SubscriptionOfferInfo
        {
            LookupSucceeded = true,
            IsAvailable = true,
            HasFreeTrial = freeTrialOffer != null,
            FreeTrialDays = freeTrialDays,
            OfferToken = displayOffer.OfferToken,
            RenewalPrice = renewalPrice
        };
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
                if (CurrentClient is { } client)
                {
                    _ = AcknowledgePurchaseAsync(client, purchase.PurchaseToken);
                }

                _purchaseTcs.TrySetResult(BillingPurchaseResult.Succeeded(
                    purchase.PurchaseToken,
                    purchase.OrderId,
                    _pendingRenewalPriceAmountMicros,
                    _pendingRenewalPriceCurrencyCode,
                    _pendingFormattedPrice));
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
            _purchaseTcs.TrySetResult(BillingPurchaseResult.Failed(CreateBillingFailureMessage(billingResult)));
        }
    }

    /// <summary>
    /// The connect delegate behind <see cref="_connectionGate"/>. The gate guarantees only one of
    /// these runs at a time, bounds how long it may take, and discards it if it fails — so this
    /// method only has to open the connection and report whether it worked.
    /// </summary>
    private async Task<bool> ConnectAsync(CancellationToken cancellationToken)
    {
        var client = GetOrCreateClient();
        if (client == null)
            return false;

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        client.StartConnection(new BillingClientStateListener(
            onSetupFinished: setupResult =>
            {
                if (setupResult.ResponseCode == BillingResponseCode.Ok)
                {
                    _logger.LogInformation("Connected to Google Play Billing");
                    tcs.TrySetResult(true);
                    return;
                }

                // Setup completing with a non-OK code is NOT the same as losing an established
                // connection, and the code is the only thing that says why. Collapsing both into a
                // bare "disconnected" message is what made an earlier failure undiagnosable:
                // BillingUnavailable (app/account not recognised by Play yet, common right after an
                // internal-track upload) looked identical to a genuine service drop.
                _logger.LogWarning(
                    "Google Play Billing setup failed. ResponseCode={ResponseCode}; DebugMessage={DebugMessage}",
                    setupResult.ResponseCode,
                    string.IsNullOrWhiteSpace(setupResult.DebugMessage) ? "(none)" : setupResult.DebugMessage);

                _connectionGate.Invalidate();
                tcs.TrySetResult(false);
            },
            onDisconnected: () =>
            {
                _logger.LogWarning("Disconnected from Google Play Billing");

                // Google's BillingClient requires an explicit reconnect after a disconnect, and a
                // disconnect can arrive long after this attempt has completed. Dropping the cached
                // attempt is what makes the next caller reconnect instead of using a dead client.
                _connectionGate.Invalidate();
                tcs.TrySetResult(false);
            }));

        using var registration = cancellationToken.Register(() => tcs.TrySetResult(false));
        return await tcs.Task.ConfigureAwait(false);
    }

    /// <summary>
    /// Creates the BillingClient on first use and reuses it afterwards. The client outlives any
    /// single connection attempt: reconnecting reuses it, only disposal clears it.
    /// </summary>
    private BillingClient? GetOrCreateClient()
    {
        lock (_clientSync)
        {
            if (_billingClient != null)
                return _billingClient;

            var activity = Platform.CurrentActivity;
            if (activity == null)
            {
                _logger.LogWarning("Cannot initialize BillingClient — no current activity");
                return null;
            }

            _billingClient = BillingClient.NewBuilder(activity)
                .SetListener(this)
                .EnablePendingPurchases(PendingPurchasesParams.NewBuilder()
                    .EnableOneTimeProducts()
                    .EnablePrepaidPlans()
                    .Build())
                .Build();

            return _billingClient;
        }
    }

    private BillingClient? CurrentClient
    {
        get
        {
            lock (_clientSync)
            {
                return _billingClient;
            }
        }
    }

    /// <summary>
    /// Returns a client that is connected and ready, or null if Google Play could not be reached.
    /// Every public entry point starts here, so none of them can observe a half-built client.
    /// </summary>
    private async Task<BillingClient?> ConnectedClientAsync()
    {
        if (!await _connectionGate.EnsureConnectedAsync())
            return null;

        var client = CurrentClient;
        return client is { IsReady: true } ? client : null;
    }

    private async Task<BillingPurchaseResult?> QuerySubscriptionProductAsync(BillingClient client)
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

        var result = await client.QueryProductDetailsAsync(queryParams);
        if (result == null || result.Result.ResponseCode != BillingResponseCode.Ok)
        {
            return BillingPurchaseResult.Failed(CreateBillingFailureMessage(result?.Result));
        }

        var productDetailsList = result.ProductDetails;
        if (productDetailsList == null || productDetailsList.Count == 0)
        {
            return BillingPurchaseResult.Failed($"Subscription product '{_productId}' not found in Google Play.");
        }

        _subscriptionProductDetails = productDetailsList[0];
        return null; // Success — no error
    }

    private static ProductDetails.SubscriptionOfferDetails? FindFreeTrialOffer(IList<ProductDetails.SubscriptionOfferDetails> offerDetails)
    {
        return offerDetails.FirstOrDefault(HasFreeTrialPhase);
    }

    private static bool HasFreeTrialPhase(ProductDetails.SubscriptionOfferDetails offer)
    {
        var phases = offer.PricingPhases?.PricingPhaseList;
        if (phases == null)
        {
            return false;
        }

        return phases.Any(phase => phase.PriceAmountMicros == 0 && !string.IsNullOrWhiteSpace(phase.BillingPeriod));
    }

    private static int? ResolveFreeTrialDays(ProductDetails.SubscriptionOfferDetails offer)
    {
        var phase = offer.PricingPhases?.PricingPhaseList?
            .FirstOrDefault(item => item.PriceAmountMicros == 0 && !string.IsNullOrWhiteSpace(item.BillingPeriod));

        return phase == null ? null : BillingPeriodParser.ParseIso8601PeriodDays(phase.BillingPeriod);
    }

    private static string? ResolveRenewalPrice(ProductDetails.SubscriptionOfferDetails offer)
    {
        return ResolveRenewalPricePhase(offer)?.FormattedPrice;
    }

    private static ProductDetails.PricingPhase? ResolveRenewalPricePhase(ProductDetails.SubscriptionOfferDetails offer)
    {
        return offer.PricingPhases?.PricingPhaseList?
            .LastOrDefault(phase => phase.PriceAmountMicros > 0);
    }

    private string CreateBillingFailureMessage(BillingResult? billingResult)
    {
        var debugMessage = billingResult?.DebugMessage ?? string.Empty;
        if (debugMessage.Contains("not configured for billing", StringComparison.OrdinalIgnoreCase))
        {
            return "Google Play Billing is not available for this installed build. Install the app from a Google Play internal or closed testing track that uses the same package name, signing key, version, and subscription product, then try again.";
        }

        var responseCode = billingResult?.ResponseCode.ToString() ?? "Unknown";
        return string.IsNullOrWhiteSpace(debugMessage)
            ? $"Google Play Billing failed (code: {responseCode})."
            : $"Google Play Billing failed: {debugMessage} (code: {responseCode}).";
    }

    private async Task AcknowledgePurchaseAsync(BillingClient client, string purchaseToken)
    {
        try
        {
            var ackParams = AcknowledgePurchaseParams.NewBuilder()
                .SetPurchaseToken(purchaseToken)
                .Build();

            var result = await client.AcknowledgePurchaseAsync(ackParams);
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
        if (disposing)
        {
            BillingClient? client;
            lock (_clientSync)
            {
                client = _billingClient;
                _billingClient = null;
            }

            if (client != null)
            {
                // Drop the cached connection too, or a later caller would be handed a "connected"
                // answer for a client that has already ended its connection.
                _connectionGate.Invalidate();
                client.EndConnection();
            }
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// Helper class for BillingClient connection state callbacks.
    /// </summary>
    private class BillingClientStateListener : Java.Lang.Object, IBillingClientStateListener
    {
        private readonly Action<BillingResult> _onSetupFinished;
        private readonly Action _onDisconnected;

        public BillingClientStateListener(Action<BillingResult> onSetupFinished, Action onDisconnected)
        {
            _onSetupFinished = onSetupFinished;
            _onDisconnected = onDisconnected;
        }

        public void OnBillingServiceDisconnected()
        {
            _onDisconnected();
        }

        /// <summary>
        /// Hands the whole <see cref="BillingResult"/> to the caller rather than reducing it to
        /// success/failure — the response code and debug message are the only diagnosis available
        /// when Play refuses to set up billing.
        /// </summary>
        public void OnBillingSetupFinished(BillingResult billingResult)
        {
            _onSetupFinished(billingResult);
        }
    }
}
