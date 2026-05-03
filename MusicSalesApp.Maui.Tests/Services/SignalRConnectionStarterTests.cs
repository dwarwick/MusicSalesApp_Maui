using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.Tests.Services;

[TestFixture]
public class SignalRConnectionStarterTests
{
    [Test]
    public async Task StartAsync_StartsOnlyTargetsThatNeedStart()
    {
        var starter = new SignalRConnectionStarter();
        var started = new List<string>();

        await starter.StartAsync(
        [
            new SignalRStartTarget("StreamCount", () => true, () =>
            {
                started.Add("StreamCount");
                return Task.CompletedTask;
            }),
            new SignalRStartTarget("LikeCount", () => false, () =>
            {
                started.Add("LikeCount");
                return Task.CompletedTask;
            })
        ]);

        Assert.That(started, Is.EqualTo(new[] { "StreamCount" }));
    }

    [Test]
    public async Task StartAsync_AllowsRetryAfterFailure()
    {
        var starter = new SignalRConnectionStarter();
        var attempts = 0;
        var connected = new List<string>();
        var failed = new List<string>();
        var shouldStart = true;

        Task StartTargetAsync()
        {
            attempts++;
            if (attempts == 1)
            {
                throw new InvalidOperationException("first attempt failed");
            }

            shouldStart = false;
            return Task.CompletedTask;
        }

        await starter.StartAsync(
            [new SignalRStartTarget("LikeCount", () => shouldStart, StartTargetAsync)],
            connected.Add,
            (name, _) => failed.Add(name));

        await starter.StartAsync(
            [new SignalRStartTarget("LikeCount", () => shouldStart, StartTargetAsync)],
            connected.Add,
            (name, _) => failed.Add(name));

        Assert.Multiple(() =>
        {
            Assert.That(attempts, Is.EqualTo(2));
            Assert.That(failed, Is.EqualTo(new[] { "LikeCount" }));
            Assert.That(connected, Is.EqualTo(new[] { "LikeCount" }));
        });
    }
}