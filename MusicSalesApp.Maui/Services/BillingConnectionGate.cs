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
    private readonly Func<CancellationToken, Task<bool>> _connectAsync;
    private readonly TimeSpan _connectTimeout;
    private readonly ILogger? _logger;
    private Task<bool>? _attempt;

    /// <param name="connectAsync">
    /// Opens the platform connection and completes with true once it is usable. It is cancelled
    /// when the attempt times out, and is expected to log its own failures.
    /// </param>
    /// <param name="connectTimeout">Overrides <see cref="DefaultConnectTimeout"/>; tests use a short value.</param>
    /// <param name="logger">
    /// Records the two failures the connect delegate cannot report itself: a timeout (the delegate is
    /// still waiting, so it has nothing to log) and an exception thrown out of it (swallowed here to
    /// keep the failure on the retry path). Without this, a billing connection that never succeeds
    /// leaves no trace at all in the log.
    /// </param>
    public BillingConnectionGate(
        Func<CancellationToken, Task<bool>> connectAsync,
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

            var started = RunAttemptAsync();
            _attempt = started;
            return started;
        }
    }

    /// <summary>
    /// Drops the cached connection so the next <see cref="EnsureConnectedAsync"/> reconnects.
    /// Call this when the platform reports a disconnect — platform billing clients require an
    /// explicit reconnect afterwards, and the cached "connected" answer is stale from that moment.
    /// </summary>
    public void Invalidate()
    {
        lock (_sync)
        {
            _attempt = null;
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

    private async Task<bool> RunAttemptAsync()
    {
        // EnsureConnectedAsync starts this while holding _sync, and an async method runs
        // synchronously up to its first await. Yielding first guarantees the platform connect —
        // which may call back into Invalidate() on the calling thread — never runs under the lock.
        await Task.Yield();

        using var timeoutSource = new CancellationTokenSource();

        try
        {
            return await _connectAsync(timeoutSource.Token).WaitAsync(_connectTimeout).ConfigureAwait(false);
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
