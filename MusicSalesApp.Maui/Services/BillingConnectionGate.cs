using Microsoft.Extensions.Logging;

namespace MusicSalesApp.Maui.Services;

/// <summary>
/// Coordinates a single, bounded connection attempt to a platform billing service.
///
/// Platform billing clients connect asynchronously and answer through a callback, which makes
/// three failure modes easy to write by accident and hard to see afterwards:
///
///   1. A caller observes a half-built client. If the client field is assigned before the
///      connection completes, a second caller sees a non-null client, returns immediately, and
///      then finds it is not ready — silently skipping whatever it was going to ask.
///   2. A callback that never arrives wedges the caller forever, because the wait on the
///      completion source is unbounded.
///   3. A disconnect is never followed by a reconnect, so every later call fails even though
///      the service came back.
///
/// This gate removes all three: concurrent callers share one attempt, the attempt is bounded by
/// a timeout, and a failed attempt is discarded so the next caller retries rather than inheriting
/// a dead task. Because every entry point awaits the same attempt, the order in which the app
/// happens to touch billing stops mattering.
///
/// The connect delegate is responsible for logging its own failures: this gate reports success
/// or failure as a bool and never throws.
/// </summary>
public sealed class BillingConnectionGate
{
    /// <summary>
    /// How long to wait for the platform to answer before giving up on an attempt. Long enough to
    /// cover a slow cold start of the platform billing service, short enough that a service which
    /// never answers cannot hold up whatever is waiting on it.
    /// </summary>
    public static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromSeconds(10);

    private readonly object _sync = new();
    private readonly Func<int, CancellationToken, Task<bool>> _connectAsync;
    private readonly TimeSpan _connectTimeout;
    private readonly ILogger? _logger;
    private Task<bool>? _attempt;
    private int _epoch;

    /// <param name="connectAsync">
    /// Opens the platform connection and completes with true once it is usable. It is cancelled
    /// when the attempt times out, and is expected to log its own failures. Receives the epoch of
    /// the attempt it is running as, to hand back to <see cref="Invalidate(int)"/> when the platform
    /// reports the connection dropped — a listener that outlives its attempt must not invalidate a
    /// newer one.
    /// </param>
    /// <param name="connectTimeout">Overrides <see cref="DefaultConnectTimeout"/>; tests use a short value.</param>
    /// <param name="logger">
    /// Records the two failures the connect delegate cannot report itself: a timeout (the delegate is
    /// still waiting, so it has nothing to log) and an exception thrown out of it (swallowed here to
    /// keep the failure on the retry path). Without this, a billing connection that never succeeds
    /// leaves no trace at all in the log.
    /// </param>
    public BillingConnectionGate(
        Func<int, CancellationToken, Task<bool>> connectAsync,
        TimeSpan? connectTimeout = null,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(connectAsync);
        _connectAsync = connectAsync;
        _connectTimeout = connectTimeout ?? DefaultConnectTimeout;
        _logger = logger;
    }

    /// <summary>
    /// Returns true once the platform billing service is connected.
    /// Concurrent callers share a single in-flight attempt; a connected gate answers immediately.
    /// </summary>
    public Task<bool> EnsureConnectedAsync()
    {
        lock (_sync)
        {
            if (_attempt is { } existing && CanReuse(existing))
            {
                return existing;
            }

            var epoch = ++_epoch;
            var started = RunAttemptAsync(epoch);
            _attempt = started;
            return started;
        }
    }

    /// <summary>
    /// Drops the cached connection so the next <see cref="EnsureConnectedAsync"/> reconnects.
    /// Call this when the platform reports a disconnect — platform billing clients require an
    /// explicit reconnect afterwards, and the cached "connected" answer is stale from that moment.
    ///
    /// Prefer the <see cref="Invalidate(int)"/> overload from anything holding a connection epoch.
    /// </summary>
    public void Invalidate()
    {
        lock (_sync)
        {
            _attempt = null;
        }
    }

    /// <summary>
    /// Drops the cached connection only if it is still the one <paramref name="epoch"/> describes.
    ///
    /// A listener registered by an abandoned attempt stays alive inside the platform client and can
    /// fire long afterwards. Unconditionally invalidating on that late callback would throw away a
    /// newer, healthy connection — and under repeated late callbacks the client would churn between
    /// connecting and being invalidated. The epoch makes a stale listener harmless.
    /// </summary>
    public void Invalidate(int epoch)
    {
        lock (_sync)
        {
            if (epoch == _epoch)
            {
                _attempt = null;
            }
        }
    }

    /// <summary>
    /// Identifies the current connection attempt. Read under <see cref="_sync"/> by callers that
    /// need to report a disconnect against the attempt they were part of.
    /// </summary>
    public int CurrentEpoch
    {
        get
        {
            lock (_sync)
            {
                return _epoch;
            }
        }
    }

    /// <summary>
    /// An attempt is worth reusing while it is still running, or once it has succeeded. Anything
    /// else — a refusal, a fault, a cancellation — must not be cached, or one bad attempt would
    /// disable billing for the rest of the process lifetime.
    /// </summary>
    private static bool CanReuse(Task<bool> attempt) => attempt switch
    {
        { IsCompletedSuccessfully: true } completed => completed.Result,
        { IsCompleted: true } => false,
        _ => true
    };

    private async Task<bool> RunAttemptAsync(int epoch)
    {
        // Task.Run, not Task.Yield. Yielding resumes on the captured SynchronizationContext, which
        // for any caller entered from a ViewModel is the UI thread — so the platform connect
        // (building the client, then a bindService IPC) would run on the main thread. That is the
        // exact main-thread native work this branch exists to remove, and only the startup call in
        // App.xaml.cs was wrapped against it. Running on the pool also keeps the connect off the
        // lock: EnsureConnectedAsync starts this while holding _sync, and a delegate that calls back
        // into Invalidate() on its calling thread must not do so underneath it.
        using var timeoutSource = new CancellationTokenSource();

        try
        {
            return await Task.Run(() => _connectAsync(epoch, timeoutSource.Token))
                .WaitAsync(_connectTimeout)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // Stop the underlying connect so it is not left running against a caller that has
            // already given up. A callback that arrives afterwards is harmless — this attempt is
            // no longer cached, so the next caller starts a fresh one.
            timeoutSource.Cancel();

            // The delegate is still waiting on a callback that never came, so it cannot report this
            // itself — if we stay quiet here the whole failure is invisible.
            _logger?.LogWarning(
                "Billing connection attempt timed out after {TimeoutSeconds}s; the next caller will retry",
                _connectTimeout.TotalSeconds);
            return false;
        }
        catch (Exception ex)
        {
            // Reporting false keeps the failure on the "retry next time" path instead of surfacing
            // as an unobserved task exception — but it must not also make the failure silent.
            _logger?.LogError(ex, "Billing connection attempt failed; the next caller will retry");
            return false;
        }
    }
}
