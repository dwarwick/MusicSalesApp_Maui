using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.Tests.Services;

[TestFixture]
public class BillingCallbackTimeoutTests
{
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromMilliseconds(50);

    [Test]
    public async Task WaitAsync_WhenTheCallbackAnswersInTime_ReturnsItsValue()
    {
        var timedOut = false;

        var result = await BillingCallbackTimeout.WaitAsync(
            Task.FromResult("answered"),
            ShortTimeout,
            () => { timedOut = true; return "timed out"; },
            "test callback");

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo("answered"));
            Assert.That(timedOut, Is.False, "The timeout factory must not run when the callback answered.");
        });
    }

    [Test]
    public async Task WaitAsync_WhenTheCallbackNeverArrives_ReturnsTheTimeoutValue()
    {
        var never = new TaskCompletionSource<string>();

        var result = await BillingCallbackTimeout.WaitAsync(
            never.Task,
            ShortTimeout,
            () => "timed out",
            "test callback");

        Assert.That(result, Is.EqualTo("timed out"));
    }

    /// <summary>
    /// Pins the behaviour that matters to callers: timing out does NOT settle the source task, so a
    /// late TrySetResult still succeeds. A caller that keeps its TaskCompletionSource in a field has
    /// to settle it itself on timeout, or an abandoned wait goes on looking live. The comment on
    /// this test used to claim the opposite while the assertion below proved otherwise.
    /// </summary>
    [Test]
    public async Task WaitAsync_WhenItTimesOut_LeavesTheSourceTaskUnsettled()
    {
        var late = new TaskCompletionSource<string>();

        var result = await BillingCallbackTimeout.WaitAsync(
            late.Task,
            ShortTimeout,
            () => "timed out",
            "test callback");

        Assert.That(late.TrySetResult("late answer"), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo("timed out"));
            Assert.That(late.Task.Result, Is.EqualTo("late answer"));
        });
    }

    // --- Settling: the fix for a timed-out purchase still looking like a live one ---

    /// <summary>
    /// The defect this exists for: a purchase that timed out left its TaskCompletionSource pending,
    /// so a transaction arriving later in the session was reported to a caller that had already
    /// given up, then finished and discarded without ever being verified.
    /// </summary>
    [Test]
    public async Task Settling_OnTimeout_LeavesTheSourceCompletedSoNothingLooksLikeItIsWaiting()
    {
        var source = new TaskCompletionSource<string>();

        var result = await BillingCallbackTimeout.WaitAsync(
            source.Task,
            ShortTimeout,
            BillingCallbackTimeout.Settling(source, "timed out"),
            "test callback");

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo("timed out"));
            Assert.That(source.Task.IsCompleted, Is.True, "The source must not go on advertising a waiting caller.");
            Assert.That(source.TrySetResult("late answer"), Is.False, "A late callback must not find a waiter.");
        });
    }

    [Test]
    public async Task Settling_WhenTheCallbackAnswersInTime_LeavesTheRealResultAlone()
    {
        var source = new TaskCompletionSource<string>();
        source.SetResult("answered");

        var result = await BillingCallbackTimeout.WaitAsync(
            source.Task,
            ShortTimeout,
            BillingCallbackTimeout.Settling(source, "timed out"),
            "test callback");

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo("answered"));
            Assert.That(source.Task.Result, Is.EqualTo("answered"));
        });
    }

    [Test]
    public void WaitAsync_WhenTheCallbackFaults_PropagatesTheException()
    {
        var faulted = new TaskCompletionSource<string>();
        faulted.SetException(new InvalidOperationException("store said no"));

        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await BillingCallbackTimeout.WaitAsync(
                faulted.Task,
                ShortTimeout,
                () => "timed out",
                "test callback"));

        Assert.That(ex!.Message, Is.EqualTo("store said no"));
    }
}
