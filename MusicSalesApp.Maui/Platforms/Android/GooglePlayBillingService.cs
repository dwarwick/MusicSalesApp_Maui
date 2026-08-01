using Android.App;
using Android.BillingClient.Api;
using Android.Runtime;
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

        var outcome = await QueryPurchasesAsync(client, queryParams).ConfigureAwait(false);

        // A query that failed comes back with an empty purchase list, which is indistinguishable
        // from a genuine "owns nothing" unless the response code is checked. Reading a failure as
        // "owns nothing" is what would strand a subscriber on the free tier. Purchases in hand
        // prove the query succeeded whatever the code says.
        if (outcome.ResponseCode != BillingResponseCode.Ok && outcome.Purchases.Count == 0)
            return BillingPurchaseResult.Unavailable(CreateBillingFailureMessage(outcome.ResponseCode, outcome.DebugMessage));

        // Find an active subscription
        foreach (var purchase in outcome.Purchases)
        {
            if (purchase.State == PurchaseState.Purchased && purchase.PurchaseToken is { } purchaseToken)
            {
                // Acknowledge if needed
                if (!purchase.IsAcknowledged)
                {
                    await AcknowledgePurchaseAsync(client, purchaseToken);
                }

                return BillingPurchaseResult.Succeeded(purchaseToken, purchase.OrderId);
            }
        }

        return null;
    }

    public async Task<SubscriptionOfferInfo> GetSubscriptionOfferAsync()
    {
        if (await ConnectedClientAsync() is not { } client)
            return SubscriptionOfferInfo.None;

        // Forces a fresh query rather than reusing a cached product, and releases the global
        // reference held for the previous one.
        SetSubscriptionProductDetails(null);
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
        // Two passes, because the gate's cached "connected" answer can outlive the connection it
        // describes. Google's BillingClient normally reports a drop through
        // OnBillingServiceDisconnected, which invalidates the gate — but a binder that dies without
        // delivering that callback would leave a successful attempt cached forever while IsReady
        // says otherwise, and every billing call from then on would fail for the life of the
        // process. Invalidating on the mismatch is what makes that self-healing.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (!await _connectionGate.EnsureConnectedAsync())
                return null;

            if (CurrentClient is { IsReady: true } client)
                return client;

            _logger.LogWarning(
                "Google Play Billing reported connected but the client is not ready; dropping the cached connection and reconnecting");
            _connectionGate.Invalidate();
        }

        return null;
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

        var outcome = await QueryProductDetailsAsync(client, queryParams).ConfigureAwait(false);

        if (outcome.Product is not null)
        {
            SetSubscriptionProductDetails(outcome.Product);
            return null; // Success — no error
        }

        if (outcome.ResponseCode is not null && outcome.ResponseCode != BillingResponseCode.Ok)
        {
            return BillingPurchaseResult.Failed(CreateBillingFailureMessage(outcome.ResponseCode, outcome.DebugMessage));
        }

        return BillingPurchaseResult.Failed($"Subscription product '{_productId}' not found in Google Play.");
    }

    /// <summary>
    /// Runs the product query through the listener overload rather than the awaitable one, and reads
    /// the whole response before the callback returns.
    ///
    /// The awaitable overload hands back Java-owned objects that the binding releases once its
    /// callback completes, so every access in the continuation is a race against the GC. That race
    /// crashed Account Settings twice: first on BillingResult.ResponseCode, then — after that read
    /// was guarded — on ProductDetailsList.Count. Guarding individual properties only moves the
    /// crash to the next one, because the entire result graph dies together. Reading here, while the
    /// peers are still valid, is what actually removes it. The chosen product is kept alive by a
    /// global reference of our own.
    /// </summary>
    private Task<ProductQueryOutcome> QueryProductDetailsAsync(BillingClient client, QueryProductDetailsParams queryParams)
    {
        var tcs = new TaskCompletionSource<ProductQueryOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);

        client.QueryProductDetails(queryParams, new ProductDetailsResponseListener((billingResult, queryResult) =>
        {
            BillingResponseCode? responseCode = null;
            string? debugMessage = null;
            ProductDetails? product = null;

            try
            {
                responseCode = billingResult?.ResponseCode;
                debugMessage = billingResult?.DebugMessage;

                var productDetailsList = queryResult?.ProductDetailsList;
                if (productDetailsList is { Count: > 0 })
                {
                    product = Retain(productDetailsList[0]);
                }
            }
            catch (Exception ex)
            {
                // Never let a read failure escape into a Java callback — it would surface as an
                // unhandled exception on the main thread, which is exactly the crash being fixed.
                _logger.LogWarning(ex, "Failed to read the Google Play product details response");
            }

            tcs.TrySetResult(new ProductQueryOutcome(responseCode, debugMessage, product));
        }));

        return tcs.Task;
    }

    /// <summary>
    /// The purchases equivalent of <see cref="QueryProductDetailsAsync"/>. Purchases reduce cleanly
    /// to plain values, so nothing Java-owned needs to escape the callback at all.
    /// </summary>
    private Task<PurchaseQueryOutcome> QueryPurchasesAsync(BillingClient client, QueryPurchasesParams queryParams)
    {
        var tcs = new TaskCompletionSource<PurchaseQueryOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);

        client.QueryPurchases(queryParams, new PurchasesResponseListener((billingResult, purchases) =>
        {
            BillingResponseCode? responseCode = null;
            string? debugMessage = null;
            var snapshots = new List<PurchaseSnapshot>();

            try
            {
                responseCode = billingResult?.ResponseCode;
                debugMessage = billingResult?.DebugMessage;

                if (purchases is not null)
                {
                    snapshots.AddRange(purchases.Select(purchase => new PurchaseSnapshot(
                        purchase.PurchaseToken,
                        purchase.OrderId,
                        purchase.PurchaseState,
                        purchase.IsAcknowledged)));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read the Google Play purchases response");
            }

            tcs.TrySetResult(new PurchaseQueryOutcome(responseCode, debugMessage, snapshots));
        }));

        return tcs.Task;
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

    /// <summary>
    /// Reads a <see cref="BillingResult"/> defensively, tolerating a peer that has already been
    /// released. Only for paths that cannot read inside the callback; the query paths capture
    /// everything up front instead, which is the actual cure rather than damage limitation.
    /// </summary>
    private static (BillingResponseCode? ResponseCode, string? DebugMessage) ReadBillingOutcome(Func<BillingResult?> read)
    {
        try
        {
            var billingResult = read();
            return billingResult is null
                ? (null, null)
                : (billingResult.ResponseCode, billingResult.DebugMessage);
        }
        catch (ObjectDisposedException)
        {
            return (null, null);
        }
    }

    /// <summary>
    /// Takes our own global reference to a Java object so it outlives the callback that delivered
    /// it. The binding releases its wrapper once the callback returns; a wrapper we own is
    /// unaffected. The caller owns the result and must dispose it.
    /// </summary>
    private static T? Retain<T>(T? value) where T : Java.Lang.Object
    {
        if (value is null)
        {
            return null;
        }

        return Java.Lang.Object.GetObject<T>(JNIEnv.NewGlobalRef(value.Handle), JniHandleOwnership.TransferGlobalRef);
    }

    /// <summary>Everything worth keeping from a product-details query, already off the Java heap.</summary>
    private sealed record ProductQueryOutcome(
        BillingResponseCode? ResponseCode,
        string? DebugMessage,
        ProductDetails? Product);

    /// <summary>A purchase reduced to plain values, so nothing Java-owned escapes the callback.</summary>
    private sealed record PurchaseSnapshot(
        string? PurchaseToken,
        string? OrderId,
        PurchaseState State,
        bool IsAcknowledged);

    private sealed record PurchaseQueryOutcome(
        BillingResponseCode? ResponseCode,
        string? DebugMessage,
        IReadOnlyList<PurchaseSnapshot> Purchases);

    /// <summary>
    /// Replaces the cached product, disposing the global reference held for the previous one.
    /// </summary>
    private void SetSubscriptionProductDetails(ProductDetails? product)
    {
        var previous = _subscriptionProductDetails;
        _subscriptionProductDetails = product;

        if (!ReferenceEquals(previous, product))
        {
            previous?.Dispose();
        }
    }

    private sealed class ProductDetailsResponseListener(Action<BillingResult?, QueryProductDetailsResult?> onResponse)
        : Java.Lang.Object, IProductDetailsResponseListener
    {
        public void OnProductDetailsResponse(BillingResult billingResult, QueryProductDetailsResult queryProductDetailsResult)
            => onResponse(billingResult, queryProductDetailsResult);
    }

    private sealed class PurchasesResponseListener(Action<BillingResult?, IList<Purchase>?> onResponse)
        : Java.Lang.Object, IPurchasesResponseListener
    {
        public void OnQueryPurchasesResponse(BillingResult billingResult, IList<Purchase> purchases)
            => onResponse(billingResult, purchases);
    }

    private string CreateBillingFailureMessage(BillingResult? billingResult)
    {
        var (responseCode, debugMessage) = ReadBillingOutcome(() => billingResult);
        return CreateBillingFailureMessage(responseCode, debugMessage);
    }

    private string CreateBillingFailureMessage(BillingResponseCode? responseCode, string? debugMessageOrNull)
    {
        var debugMessage = debugMessageOrNull ?? string.Empty;
        if (debugMessage.Contains("not configured for billing", StringComparison.OrdinalIgnoreCase))
        {
            return "Google Play Billing is not available for this installed build. Install the app from a Google Play internal or closed testing track that uses the same package name, signing key, version, and subscription product, then try again.";
        }

        var code = responseCode?.ToString() ?? "Unknown";
        return string.IsNullOrWhiteSpace(debugMessage)
            ? $"Google Play Billing failed (code: {code})."
            : $"Google Play Billing failed: {debugMessage} (code: {code}).";
    }

    private async Task AcknowledgePurchaseAsync(BillingClient client, string purchaseToken)
    {
        try
        {
            var ackParams = AcknowledgePurchaseParams.NewBuilder()
                .SetPurchaseToken(purchaseToken)
                .Build();

            var result = await client.AcknowledgePurchaseAsync(ackParams);

            // Same released-peer hazard as the queries. This one is already inside a catch so it
            // could never crash the app, but reading it defensively keeps the log honest instead of
            // reporting an acknowledgement failure that was really just a disposed wrapper.
            var (responseCode, debugMessage) = ReadBillingOutcome(() => result);
            if (responseCode == BillingResponseCode.Ok)
            {
                _logger.LogInformation("Purchase acknowledged successfully");
            }
            else
            {
                _logger.LogWarning("Failed to acknowledge purchase: {Code} {Message}",
                    responseCode, debugMessage);
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

            // Releases the global reference we hold for the cached product.
            SetSubscriptionProductDetails(null);

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
