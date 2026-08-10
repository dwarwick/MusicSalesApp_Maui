#pragma warning disable CA1422
#if IOS
using Foundation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;
using MusicSalesApp.Maui.Services;
using StoreKit;

namespace MusicSalesApp.Maui.Platforms.iOS;

public class AppStoreBillingService : NSObject, IBillingService, ISKPaymentTransactionObserver
{
    private readonly ILogger<AppStoreBillingService> _logger;
    private readonly string _productId;
    private bool _initialized;
    private TaskCompletionSource<BillingPurchaseResult>? _purchaseTcs;
    private TaskCompletionSource<BillingPurchaseResult?>? _restoreTcs;
    private ProductRequestDelegate? _productRequestDelegate;
    private SKProductsRequest? _productRequest;

    public AppStoreBillingService(IConfiguration configuration, ILogger<AppStoreBillingService> logger)
    {
        _logger = logger;
        _productId = configuration["AppleAppStore:SubscriptionProductId"] ?? "streamtunes_monthly_sub_ios";
    }

    public Task InitializeAsync()
    {
        if (_initialized)
        {
            return Task.CompletedTask;
        }

        SKPaymentQueue.DefaultQueue.AddTransactionObserver(this);
        _initialized = true;
        return Task.CompletedTask;
    }

    public async Task<BillingPurchaseResult> PurchaseSubscriptionAsync()
    {
        await InitializeAsync();

        Console.WriteLine("[AppStoreBillingService] Starting subscription purchase for product '{0}'.", _productId);

        if (!SKPaymentQueue.CanMakePayments)
        {
            Console.WriteLine("[AppStoreBillingService] In-app purchases are disabled on this device.");
            return BillingPurchaseResult.Failed("In-app purchases are disabled on this device.");
        }

        var lookupResult = await QueryProductAsync();
        if (lookupResult.Product == null)
        {
            Console.WriteLine(
                "[AppStoreBillingService] Product lookup returned no matching product for '{0}'. Invalid products: {1}",
                _productId,
                lookupResult.InvalidProducts.Count == 0 ? "<none>" : string.Join(", ", lookupResult.InvalidProducts));
            return BillingPurchaseResult.Failed(AppleStoreProductLookupFailureMessage.Create(_productId, lookupResult.InvalidProducts));
        }

        Console.WriteLine("[AppStoreBillingService] Product lookup succeeded for '{0}'. Queueing payment.", _productId);
        _purchaseTcs = new TaskCompletionSource<BillingPurchaseResult>();
        var payment = await CreatePaymentAsync(lookupResult.Product);
        SKPaymentQueue.DefaultQueue.AddPayment(payment);
        return await _purchaseTcs.Task;
    }

    public async Task<BillingPurchaseResult?> RestorePurchaseAsync()
    {
        await InitializeAsync();

        _restoreTcs = new TaskCompletionSource<BillingPurchaseResult?>();
        SKPaymentQueue.DefaultQueue.RestoreCompletedTransactions();
        return await _restoreTcs.Task;
    }

    public Task<SubscriptionOfferInfo> GetSubscriptionOfferAsync()
        => Task.FromResult(SubscriptionOfferInfo.None);

    public void UpdatedTransactions(SKPaymentQueue queue, SKPaymentTransaction[] transactions)
    {
        foreach (var transaction in transactions)
        {
            switch (transaction.TransactionState)
            {
                case SKPaymentTransactionState.Purchased:
                    HandlePurchasedTransaction(queue, transaction, isRestore: false);
                    break;

                case SKPaymentTransactionState.Restored:
                    HandlePurchasedTransaction(queue, transaction, isRestore: true);
                    break;

                case SKPaymentTransactionState.Failed:
                    HandleFailedTransaction(queue, transaction);
                    break;
            }
        }
    }

    public void RemovedTransactions(SKPaymentQueue queue, SKPaymentTransaction[] transactions)
    {
    }

    public void RestoreCompletedTransactionsFinished(SKPaymentQueue queue)
    {
        _restoreTcs?.TrySetResult(null);
    }

    public void RestoreCompletedTransactionsFailedWithError(SKPaymentQueue queue, NSError error)
    {
        _logger.LogWarning("App Store restore failed: {Code} {Description}", error.Code, error.LocalizedDescription);
        Console.WriteLine("[AppStoreBillingService] Restore failed: {0} {1}", error.Code, error.LocalizedDescription ?? "<no description>");

        var message = error.LocalizedDescription ?? "Failed to restore App Store purchases.";

        // "We could not ask the store" has to be distinguishable from "the store answered and you
        // own nothing", or AuthService accepts it as final and never retries — leaving a subscriber
        // whose restore failed on a flaky network stuck on the free tier until the next launch.
        // Without this the retry contract on IBillingService was implemented on Android only.
        _restoreTcs?.TrySetResult(IsStoreUnreachable(error)
            ? BillingPurchaseResult.Unavailable(message)
            : BillingPurchaseResult.Failed(message));
    }

    public void UpdatedDownloads(SKPaymentQueue queue, SKDownload[] downloads)
    {
    }

    // Compared as literals rather than through the SDK constants: this file cannot be compiled on
    // the Windows machine this change was made from, so it avoids depending on API shapes that
    // cannot be checked here.
    private const string StoreKitErrorDomain = "SKErrorDomain";
    private const string UrlLoadingErrorDomain = "NSURLErrorDomain";

    /// <summary>
    /// True when StoreKit failed because it could not reach the store, rather than because it
    /// reached it and gave an answer. Only the former is worth retrying — a declined or cancelled
    /// restore is final, and retrying it would achieve nothing.
    /// </summary>
    private static bool IsStoreUnreachable(NSError error)
    {
        // StoreKit surfaces transport failures under the URL loading domain.
        if (error.Domain == UrlLoadingErrorDomain)
        {
            return true;
        }

        return error.Domain == StoreKitErrorDomain
            && (SKError)(long)error.Code == SKError.CloudServiceNetworkConnectionFailed;
    }

    private async Task<ProductLookupResult> QueryProductAsync()
    {
        var productRequestTcs = new TaskCompletionSource<ProductLookupResult>();
        _productRequestDelegate = new ProductRequestDelegate(
            onResponse: response =>
            {
                var invalidProducts = response.InvalidProducts?
                    .Select(p => p.ToString())
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray() ?? [];

                var validProducts = response.Products?
                    .Select(p => p.ProductIdentifier)
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray() ?? [];

                _logger.LogInformation(
                    "StoreKit product lookup for {ProductId}. Valid products: {ValidProducts}. Invalid products: {InvalidProducts}",
                    _productId,
                    validProducts.Length == 0 ? "<none>" : string.Join(", ", validProducts),
                    invalidProducts.Length == 0 ? "<none>" : string.Join(", ", invalidProducts));

                Console.WriteLine(
                    "[AppStoreBillingService] StoreKit lookup for '{0}'. Valid products: {1}. Invalid products: {2}",
                    _productId,
                    validProducts.Length == 0 ? "<none>" : string.Join(", ", validProducts),
                    invalidProducts.Length == 0 ? "<none>" : string.Join(", ", invalidProducts));

                var product = response.Products?.FirstOrDefault(p => p.ProductIdentifier == _productId);
                productRequestTcs.TrySetResult(new ProductLookupResult(product, invalidProducts));
            },
            onError: error =>
            {
                Console.WriteLine(
                    "[AppStoreBillingService] StoreKit request failed for '{0}': {1} {2}",
                    _productId,
                    error.Code,
                    error.LocalizedDescription ?? "<no description>");
                productRequestTcs.TrySetException(new InvalidOperationException(error.LocalizedDescription ?? "Failed to query App Store products."));
            });

        _productRequest = new SKProductsRequest(new NSSet<NSString>(new NSString(_productId)));
        _productRequest.Delegate = _productRequestDelegate;
        _productRequest.Start();

        try
        {
            return await productRequestTcs.Task;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query App Store product details for {ProductId}", _productId);
            Console.WriteLine("[AppStoreBillingService] Exception during StoreKit lookup for '{0}': {1}", _productId, ex.Message);
            return new ProductLookupResult(null, []);
        }
        finally
        {
            _productRequest?.Dispose();
            _productRequest = null;
            _productRequestDelegate = null;
        }
    }

    private sealed record ProductLookupResult(SKProduct? Product, IReadOnlyList<string> InvalidProducts);

    private async Task<SKPayment> CreatePaymentAsync(SKProduct product)
    {
        var payment = SKMutablePayment.PaymentWithProduct(product);
        var storedUserId = await SecureStorage.Default.GetAsync(AuthStorageKeys.UserId);
        var appAccountToken = AppleAppAccountTokenResolver.FromStoredUserId(storedUserId);

        if (!string.IsNullOrWhiteSpace(appAccountToken))
        {
            payment.ApplicationUsername = appAccountToken;
            _logger.LogInformation("StoreKit payment will include application username token for user {UserId}", appAccountToken);
            Console.WriteLine("[AppStoreBillingService] StoreKit payment will include application username token '{0}'.", appAccountToken);
        }
        else
        {
            _logger.LogWarning("StoreKit payment is proceeding without an application username token because no valid stored user ID was available.");
            Console.WriteLine("[AppStoreBillingService] StoreKit payment is proceeding without an application username token.");
        }

        return payment;
    }

    private void HandlePurchasedTransaction(SKPaymentQueue queue, SKPaymentTransaction transaction, bool isRestore)
    {
        var originalTransactionId = transaction.OriginalTransaction?.TransactionIdentifier;
        var transactionId = transaction.TransactionIdentifier ?? originalTransactionId;
        var productId = transaction.Payment?.ProductIdentifier ?? _productId;

        Console.WriteLine(
            "[AppStoreBillingService] Transaction update. State={0}, ProductId={1}, TransactionId={2}, OriginalTransactionId={3}, IsRestore={4}",
            transaction.TransactionState,
            productId,
            transactionId ?? "<none>",
            originalTransactionId ?? "<none>",
            isRestore);

        if (string.IsNullOrWhiteSpace(transactionId))
        {
            var failure = BillingPurchaseResult.Failed("App Store purchase completed without a transaction ID.");
            if (isRestore)
            {
                _restoreTcs?.TrySetResult(failure);
            }
            else
            {
                _purchaseTcs?.TrySetResult(failure);
            }

            queue.FinishTransaction(transaction);
            return;
        }

        var result = ApplePurchaseResultFactory.CreateSuccess(transactionId, originalTransactionId, productId, transaction.Payment?.ApplicationUsername);

        if (isRestore)
        {
            _restoreTcs?.TrySetResult(result);
        }
        else
        {
            _purchaseTcs?.TrySetResult(result);
        }

        queue.FinishTransaction(transaction);
    }

    private void HandleFailedTransaction(SKPaymentQueue queue, SKPaymentTransaction transaction)
    {
        var error = transaction.Error;
        Console.WriteLine(
            "[AppStoreBillingService] Transaction failed. ProductId={0}, Code={1}, Description={2}",
            transaction.Payment?.ProductIdentifier ?? _productId,
            error?.Code,
            error?.LocalizedDescription ?? "<no description>");
        var result = error?.Code == (long)SKError.PaymentCancelled
            ? BillingPurchaseResult.Cancelled()
            : BillingPurchaseResult.Failed(error?.LocalizedDescription ?? "App Store purchase failed.");

        _purchaseTcs?.TrySetResult(result);
        _restoreTcs?.TrySetResult(result);
        queue.FinishTransaction(transaction);
    }

    private sealed class ProductRequestDelegate : SKProductsRequestDelegate
    {
        private readonly Action<SKProductsResponse> _onResponse;
        private readonly Action<NSError> _onError;

        public ProductRequestDelegate(Action<SKProductsResponse> onResponse, Action<NSError> onError)
        {
            _onResponse = onResponse;
            _onError = onError;
        }

        public override void ReceivedResponse(SKProductsRequest request, SKProductsResponse response)
        {
            _onResponse(response);
        }

        public override void RequestFailed(SKRequest request, NSError error)
        {
            _onError(error);
        }
    }
}
#endif
#pragma warning restore CA1422