using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.Tests.Services;

public class CoalescedUiUpdateSchedulerTests
{
    [Test]
    public void Requests_AreCombinedIntoOnePendingDispatch()
    {
        var dispatched = new List<Action>();
        var applied = new List<int>();
        var scheduler = new CoalescedUiUpdateScheduler(dispatched.Add, applied.Add);

        scheduler.Request(1);
        scheduler.Request(2);

        Assert.That(dispatched, Has.Count.EqualTo(1));
        dispatched[0]();
        Assert.That(applied, Is.EqualTo(new[] { 3 }));
    }

    [Test]
    public void RequestDuringApply_IsScheduledForNextDispatch()
    {
        var dispatched = new Queue<Action>();
        CoalescedUiUpdateScheduler? scheduler = null;
        var applied = new List<int>();
        scheduler = new CoalescedUiUpdateScheduler(dispatched.Enqueue, updates =>
        {
            applied.Add(updates);
            if (updates == 1)
            {
                scheduler!.Request(2);
            }
        });

        scheduler.Request(1);
        dispatched.Dequeue()();
        dispatched.Dequeue()();

        Assert.That(applied, Is.EqualTo(new[] { 1, 2 }));
    }
}
