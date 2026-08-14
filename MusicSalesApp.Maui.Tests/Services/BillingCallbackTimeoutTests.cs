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
    /// The store callback is left to arrive whenever it likes — its TrySetResult simply finds the
    /// task already settled. This is what makes the bound safe to apply to a native callback we
    /// cannot cancel.
    /// </summary>
    [Test]
    public async Task WaitAsync_WhenTheCallbackArrivesLate_DoesNotThrowOrChangeTheResult()
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
