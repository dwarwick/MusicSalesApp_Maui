using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.Tests.Services;

[TestFixture]
public class BillingConnectionGateTests
{
    // Short enough to keep the timeout tests quick, long enough that a loaded CI agent does not
    // trip the timeout in tests that are not about timing out.
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan GenerousTimeout = TimeSpan.FromSeconds(30);

    [Test]
    public async Task EnsureConnectedAsync_WhenCallersArriveTogether_OpensOnlyOneConnection()
    {
        // The bug this guards: a client field assigned before the connection completes lets a
        // second caller see a half-built client, return immediately, and silently skip its work.
        var attempts = 0;
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new BillingConnectionGate(
            _ =>
            {
                Interlocked.Increment(ref attempts);
                return release.Task;
            },
            GenerousTimeout);

        var callers = Enumerable.Range(0, 5).Select(_ => gate.EnsureConnectedAsync()).ToArray();
        release.SetResult(true);
        var results = await Task.WhenAll(callers);

        Assert.Multiple(() =>
        {
            Assert.That(attempts, Is.EqualTo(1));
            Assert.That(results, Is.All.True);
        });
    }

    [Test]
    public async Task EnsureConnectedAsync_WhenAlreadyConnected_DoesNotReconnect()
    {
        var attempts = 0;
        var gate = new BillingConnectionGate(
            _ =>
            {
                Interlocked.Increment(ref attempts);
                return Task.FromResult(true);
            },
            GenerousTimeout);

        await gate.EnsureConnectedAsync();
        var second = await gate.EnsureConnectedAsync();

        Assert.Multiple(() =>
        {
            Assert.That(second, Is.True);
            Assert.That(attempts, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task EnsureConnectedAsync_WhenThePlatformNeverAnswers_ReportsFailure()
    {
        // A platform store that never calls back must not hold the caller forever — that is what
        // used to wedge app startup behind billing.
        var gate = new BillingConnectionGate(
            _ => new TaskCompletionSource<bool>().Task,
            ShortTimeout);

        var connected = await gate.EnsureConnectedAsync();

        Assert.That(connected, Is.False);
    }

    [Test]
    public async Task EnsureConnectedAsync_WhenTheAttemptTimesOut_CancelsTheConnect()
    {
        CancellationToken observed = default;
        var gate = new BillingConnectionGate(
            token =>
            {
                observed = token;
                return new TaskCompletionSource<bool>().Task;
            },
            ShortTimeout);

        await gate.EnsureConnectedAsync();

        Assert.That(observed.IsCancellationRequested, Is.True);
    }

    [Test]
    public async Task EnsureConnectedAsync_AfterATimeout_RetriesOnTheNextCall()
    {
        // One bad attempt must not disable billing for the rest of the process lifetime.
        var attempts = 0;
        var gate = new BillingConnectionGate(
            _ => Interlocked.Increment(ref attempts) == 1
                ? new TaskCompletionSource<bool>().Task
                : Task.FromResult(true),
            ShortTimeout);

        var first = await gate.EnsureConnectedAsync();
        var second = await gate.EnsureConnectedAsync();

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.False);
            Assert.That(second, Is.True);
            Assert.That(attempts, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task EnsureConnectedAsync_AfterARefusedConnect_RetriesOnTheNextCall()
    {
        var attempts = 0;
        var gate = new BillingConnectionGate(
            _ => Task.FromResult(Interlocked.Increment(ref attempts) != 1),
            GenerousTimeout);

        var first = await gate.EnsureConnectedAsync();
        var second = await gate.EnsureConnectedAsync();

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.False);
            Assert.That(second, Is.True);
            Assert.That(attempts, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task EnsureConnectedAsync_WhenTheConnectThrows_ReportsFailureWithoutThrowing()
    {
        var attempts = 0;
        var gate = new BillingConnectionGate(
            _ => Interlocked.Increment(ref attempts) == 1
                ? Task.FromException<bool>(new InvalidOperationException("store unavailable"))
                : Task.FromResult(true),
            GenerousTimeout);

        var first = await gate.EnsureConnectedAsync();
        var second = await gate.EnsureConnectedAsync();

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.False);
            Assert.That(second, Is.True);
        });
    }

    [Test]
    public async Task Invalidate_MakesTheNextCallReconnect()
    {
        // Platform billing clients require an explicit reconnect after a disconnect; a cached
        // "connected" answer is stale from that moment on.
        var attempts = 0;
        var gate = new BillingConnectionGate(
            _ =>
            {
                Interlocked.Increment(ref attempts);
                return Task.FromResult(true);
            },
            GenerousTimeout);

        await gate.EnsureConnectedAsync();
        gate.Invalidate();
        await gate.EnsureConnectedAsync();

        Assert.That(attempts, Is.EqualTo(2));
    }

    [Test]
    public async Task EnsureConnectedAsync_WhenTheConnectInvalidatesInline_DoesNotDeadlock()
    {
        // A platform callback can fire on the calling thread, so the connect delegate can re-enter
        // the gate. If the gate ran the connect while holding its own lock, this would be a
        // re-entrancy hazard rather than a plain call.
        BillingConnectionGate? gate = null;
        gate = new BillingConnectionGate(
            _ =>
            {
                gate!.Invalidate();
                return Task.FromResult(true);
            },
            GenerousTimeout);

        var connected = await gate.EnsureConnectedAsync().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.That(connected, Is.True);
    }

    [Test]
    public void Constructor_WithoutAConnectDelegate_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new BillingConnectionGate(null!));
    }
}
