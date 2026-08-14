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

    /// <summary>
    /// Transactions delivered by the current restore, kept until the store says it has finished so
    /// the most recent one can be chosen. Guarded because StoreKit delivers on its own thread.
    /// </summary>
    private readonly List<RestoredTransaction> _restoredTransactions = [];
    private readonly object _restoredTransactionsLock = new();

    /// <inheritdoc />
    public Func<BillingPurchaseVerificationRequest, Task<bool>>? UnverifiedPurchaseHandler { get; set; }

    /// <summary>True while a restore has been asked for and the store has not finished answering.</summary>
    private bool IsRestoreInFlight => _restoreTcs is { Task.IsCompleted: false };

    /// <summary>True while a purchase has been started and no result has been delivered yet.</summary>
    private bool IsPurchaseWaiting => _purchaseTcs is { Task.IsCompleted: false };

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

        // Settle whoever was waiting on the previous attempt rather than abandoning them: the field
        // assignment alone left the earlier caller awaiting a task nothing would ever complete.
        var purchaseTcs = new TaskCompletionSource<BillingPurchaseResult>();
        Interlocked.Exchange(ref _purchaseTcs, purchaseTcs)
            ?.TrySetResult(BillingPurchaseResult.Failed("Superseded by another purchase attempt."));

        var payment = await CreatePaymentAsync(lookupResult.Product);
        SKPaymentQueue.DefaultQueue.AddPayment(payment);

        return await BillingCallbackTimeout.WaitAsync(
            purchaseTcs.Task,
            BillingCallbackTimeout.DefaultPurchaseFlowTimeout,
            // Failed, not Unavailable: nothing consumes BillingUnavailable on the purchase side, so
            // the only thing that matters here is that the caller gets an answer and a message that
            // does not claim the purchase did or did not happen.
            //
            // Settling: bounding the wait does not complete the source task, it only stops this
            // caller listening. Left pending, _purchaseTcs goes on looking like a caller waiting for
            // a result, so a transaction arriving later in the session was reported to a caller that
            // had already given up, then finished and dropped - unverified and never replayed. That
            // is the money-taken-with-no-record failure the unsolicited-purchase path exists to
            // prevent, reached on a path that could never consult it.
            BillingCallbackTimeout.Settling(
                purchaseTcs,
                BillingPurchaseResult.Failed(
                    "The App Store did not report the result of the purchase. If you completed it, it will be restored automatically.")),
            "purchase result",
            _logger);
    }

    public async Task<BillingPurchaseResult?> RestorePurchaseAsync()
    {
        await InitializeAsync();

        var restoreTcs = new TaskCompletionSource<BillingPurchaseResult?>();
        Interlocked.Exchange(ref _restoreTcs, restoreTcs)
            ?.TrySetResult(BillingPurchaseResult.Unavailable("Superseded by another restore attempt."));

        // Anything left over belongs to a restore that timed out or was superseded; carrying it into
        // this one could answer with a transaction the store is no longer talking about.
        lock (_restoredTransactionsLock)
        {
            _restoredTransactions.Clear();
        }

        SKPaymentQueue.DefaultQueue.RestoreCompletedTransactions();

        return await BillingCallbackTimeout.WaitAsync(
            restoreTcs.Task,
            BillingCallbackTimeout.DefaultRestoreTimeout,
            // Unavailable, not null and not Failed. A store that never answered has told us nothing
            // about ownership: null would be read as "you own nothing" and Failed as final, either
            // of which strands a subscriber on the free tier. Unavailable is what sets the pending
            // flag in AuthService that earns the retry on the next status refresh.
            //
            // Settling for the same reason as the purchase above: while restoreTcs stays pending
            // IsRestoreInFlight keeps reporting true, and every Restored transaction after that is
            // filed against a restore nobody is listening to, then silently discarded.
            BillingCallbackTimeout.Settling<BillingPurchaseResult?>(
                restoreTcs,
                BillingPurchaseResult.Unavailable("The App Store did not respond to the restore request.")),
            "restore request",
            _logger);
    }

    public Task<SubscriptionOfferInfo> GetSubscriptionOfferAsync()
        => Task.FromResult(SubscriptionOfferInfo.None);

    public void UpdatedTransactions(SKPaymentQueue queue, SKPaymentTransaction[] transactions)
    {
        foreach (var transaction in transactions)
        {
            // Logged through ILogger, not just Console.WriteLine, because Console output never
            // reaches the rolling device log — so from the log's point of view this observer did not
            // exist. A purchase that produced no visible transaction at all was indistinguishable
            // from one where the store never called back.
            _logger.LogInformation(
                "StoreKit transaction update. State={State}; ProductId={ProductId}; TransactionId={TransactionId}; OriginalTransactionId={OriginalTransactionId}; RestoreInFlight={RestoreInFlight}; PurchaseWaiting={PurchaseWaiting}",
                transaction.TransactionState,
                transaction.Payment?.ProductIdentifier ?? _productId,
                transaction.TransactionIdentifier ?? "<none>",
                transaction.OriginalTransaction?.TransactionIdentifier ?? "<none>",
                IsRestoreInFlight,
                IsPurchaseWaiting);

            switch (transaction.TransactionState)
            {
                case SKPaymentTransactionState.Purchased:
                    HandlePurchasedTransaction(queue, transaction, isRestore: false);
                    break;

                case SKPaymentTransactionState.Restored:
                    // A Restored transaction only belongs to a restore if one actually asked for it.
                    // Re-buying a subscription the Apple Account already owns delivers the purchase
                    // in this state, and treating that as restore traffic parked it in the pending
                    // list waiting for a RestoreCompletedTransactionsFinished that was never coming
                    // — so the Subscribe sheet was confirmed and then nothing happened at all.
                    HandlePurchasedTransaction(queue, transaction, isRestore: IsRestoreInFlight);
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

    /// <summary>
    /// Answers the restore with the customer's most recent transaction, once the store has finished
    /// delivering all of them.
    ///
    /// The restore used to be settled by whichever transaction arrived first. A restore replays the
    /// entire renewal history oldest-first, so for anyone who had renewed even once that was a
    /// long-expired transaction — the server would look it up, correctly report the end of that old
    /// billing period, and refuse to grant entitlement to a subscription that was in fact current.
    /// </summary>
    public void RestoreCompletedTransactionsFinished(SKPaymentQueue queue)
    {
        RestoredTransaction[] restored;
        lock (_restoredTransactionsLock)
        {
            restored = _restoredTransactions.ToArray();
            _restoredTransactions.Clear();
        }

        if (restored.Length == 0)
        {
            _logger.LogInformation("App Store restore finished with no transactions to restore");
            Console.WriteLine("[AppStoreBillingService] Restore finished with no transactions.");
            // Null, not a failure: the store answered, and this customer owns nothing.
            _restoreTcs?.TrySetResult(null);
            return;
        }

        // Date first, then transaction ID as a tiebreak. A sandbox account replays its whole history
        // with near-identical dates — 88 transactions all stamped within the same second — and
        // ordering on the date alone picks arbitrarily among them. Apple's transaction identifiers
        // increase over time, so the highest is the best available answer when the dates tie.
        var latest = restored
            .OrderByDescending(transaction => transaction.TransactionDateUtc)
            .ThenByDescending(transaction => long.TryParse(transaction.TransactionId, out var id) ? id : 0L)
            .First();

        _logger.LogInformation(
            "App Store restore finished. Reporting the most recent of {Count} restored transactions. TransactionId={TransactionId}; TransactionDateUtc={TransactionDateUtc}",
            restored.Length,
            latest.TransactionId,
            latest.TransactionDateUtc);
        Console.WriteLine(
            "[AppStoreBillingService] Restore finished with {0} transaction(s). Reporting '{1}' dated {2:u}.",
            restored.Length,
            latest.TransactionId,
            latest.TransactionDateUtc);

        _restoreTcs?.TrySetResult(ApplePurchaseResultFactory.CreateSuccess(
            latest.TransactionId,
            latest.OriginalTransactionId,
            latest.ProductId,
            latest.AppAccountToken));
    }

    /// <summary>What is worth keeping from a restored transaction once its native peer is gone.</summary>
    private readonly record struct RestoredTransaction(
        string TransactionId,
        string? OriginalTransactionId,
        string ProductId,
        string? AppAccountToken,
        DateTime TransactionDateUtc);

    public void RestoreCompletedTransactionsFailedWithError(SKPaymentQueue queue, NSError error)
    {
        _logger.LogWarning("App Store restore failed: {Code} {Description}", error.Code, error.LocalizedDescription);
        Console.WriteLine("[AppStoreBillingService] Restore failed: {0} {1}", error.Code, error.LocalizedDescription ?? "<no description>");

        var message = error.LocalizedDescription ?? "Failed to restore App Store purchases.";

        lock (_restoredTransactionsLock)
        {
            _restoredTransactions.Clear();
        }

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
            // An SKProductsRequest that never calls back used to hold the Subscribe button forever.
            // An empty result falls into the existing "no matching product" branch in the caller.
            return await BillingCallbackTimeout.WaitAsync(
                productRequestTcs.Task,
                BillingCallbackTimeout.DefaultQueryTimeout,
                () => new ProductLookupResult(null, []),
                "product lookup",
                _logger);
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
        // DateTime.MinValue for a transaction the store dated as unknown: it sorts last, so it can
        // never win the "most recent" comparison over a transaction that has a real date.
        var transactionDateUtc = transaction.TransactionDate is { } date
            ? (DateTime)date
            : DateTime.MinValue;

        Console.WriteLine(
            "[AppStoreBillingService] Transaction update. State={0}, ProductId={1}, TransactionId={2}, OriginalTransactionId={3}, IsRestore={4}",
            transaction.TransactionState,
            productId,
            transactionId ?? "<none>",
            originalTransactionId ?? "<none>",
            isRestore);

        if (string.IsNullOrWhiteSpace(transactionId))
        {
            // A restore that hands back a transaction with no identifier has nothing to report, but
            // it must not fail the whole restore - the transactions either side of it may be fine.
            if (!isRestore)
            {
                _purchaseTcs?.TrySetResult(
                    BillingPurchaseResult.Failed("App Store purchase completed without a transaction ID."));
            }

            queue.FinishTransaction(transaction);
            return;
        }

        if (isRestore)
        {
            // Collected rather than answered. RestoreCompletedTransactionsFinished picks the most
            // recent of them, because a restore replays the whole renewal history oldest-first and
            // answering with the first arrival reported a long-expired transaction as the purchase.
            lock (_restoredTransactionsLock)
            {
                _restoredTransactions.Add(new RestoredTransaction(
                    transactionId,
                    originalTransactionId,
                    productId,
                    transaction.Payment?.ApplicationUsername,
                    transactionDateUtc));
            }

            queue.FinishTransaction(transaction);
            return;
        }

        var result = ApplePurchaseResultFactory.CreateSuccess(
            transactionId, originalTransactionId, productId, transaction.Payment?.ApplicationUsername);

        if (_purchaseTcs?.TrySetResult(result) == true)
        {
            _logger.LogInformation(
                "Reported an App Store purchase to the waiting caller. TransactionId={TransactionId}; OriginalTransactionId={OriginalTransactionId}",
                transactionId,
                originalTransactionId ?? "<none>");
            queue.FinishTransaction(transaction);
            return;
        }

        // Nobody was waiting. That is routine for a renewal and suspicious for anything else, so the
        // two are told apart rather than lumped together - see UnsolicitedPurchasePolicy.
        var action = UnsolicitedPurchasePolicy.Decide(
            transactionId,
            originalTransactionId,
            canVerify: UnverifiedPurchaseHandler is not null);

        switch (action)
        {
            case UnsolicitedPurchaseAction.Finish:
                _logger.LogInformation(
                    "Finished a subscription renewal delivered by the App Store. TransactionId={TransactionId}; OriginalTransactionId={OriginalTransactionId}",
                    transactionId,
                    originalTransactionId);
                queue.FinishTransaction(transaction);
                return;

            case UnsolicitedPurchaseAction.Verify:
                _ = VerifyAndFinishAsync(queue, transaction, result, transactionId);
                return;

            default:
                // No way to attach it to an account right now. Left UNFINISHED on purpose: finishing
                // would tell StoreKit we had dealt with a purchase the server has never heard of,
                // and the customer's money would be gone with nothing to show for it. The store
                // re-delivers it, and a later launch with someone signed in can record it.
                _logger.LogWarning(
                    "An App Store purchase arrived that cannot be verified yet and was left unfinished. TransactionId={TransactionId}",
                    transactionId);
                return;
        }
    }

    /// <summary>
    /// Records an unsolicited purchase with the server before finishing it. Finishing is what tells
    /// the store the customer has had what they paid for, so it must not happen until the server has
    /// the purchase - and must still happen once it does, or the transaction is replayed forever.
    /// </summary>
    private async Task VerifyAndFinishAsync(
        SKPaymentQueue queue,
        SKPaymentTransaction transaction,
        BillingPurchaseResult result,
        string transactionId)
    {
        try
        {
            var handler = UnverifiedPurchaseHandler;
            if (handler is null)
            {
                _logger.LogWarning(
                    "An unsolicited App Store purchase could not be verified and was left unfinished. TransactionId={TransactionId}",
                    transactionId);
                return;
            }

            var verified = await handler(result.ToVerificationRequest()).ConfigureAwait(false);
            if (!verified)
            {
                _logger.LogWarning(
                    "The server did not record an unsolicited App Store purchase, so it was left unfinished for a later attempt. TransactionId={TransactionId}",
                    transactionId);
                return;
            }

            _logger.LogInformation(
                "Recorded an unsolicited App Store purchase with the server and finished it. TransactionId={TransactionId}",
                transactionId);
            queue.FinishTransaction(transaction);
        }
        catch (Exception ex)
        {
            // Left unfinished, which is the recoverable direction: the store replays it.
            _logger.LogWarning(
                ex,
                "Verifying an unsolicited App Store purchase failed; it was left unfinished. TransactionId={TransactionId}",
                transactionId);
        }
    }

    private void HandleFailedTransaction(SKPaymentQueue queue, SKPaymentTransaction transaction)
    {
        var error = transaction.Error;
        _logger.LogWarning(
            "StoreKit transaction failed. ProductId={ProductId}; Domain={Domain}; Code={Code}; Description={Description}",
            transaction.Payment?.ProductIdentifier ?? _productId,
            error?.Domain ?? "<none>",
            error?.Code,
            error?.LocalizedDescription ?? "<no description>");
        Console.WriteLine(
            "[AppStoreBillingService] Transaction failed. ProductId={0}, Code={1}, Description={2}",
            transaction.Payment?.ProductIdentifier ?? _productId,
            error?.Code,
            error?.LocalizedDescription ?? "<no description>");
        var result = error?.Code == (long)SKError.PaymentCancelled
            ? BillingPurchaseResult.Cancelled()
            : BillingPurchaseResult.Failed(error?.LocalizedDescription ?? "App Store purchase failed.");

        // Only the purchase waiter. A failed transaction reaches this observer callback, whereas a
        // restore reports through RestoreCompletedTransactionsFinished/FailedWithError — so
        // settling the restore here resolved an unrelated in-flight restore with a purchase's
        // error, and could report a cancelled payment as a failed restore.
        _purchaseTcs?.TrySetResult(result);
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