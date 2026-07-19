namespace MusicSalesApp.Maui.Services;

/// <summary>Combines update flags while a single UI dispatch is pending.</summary>
internal sealed class CoalescedUiUpdateScheduler
{
    private readonly Action<Action> _dispatch;
    private readonly Action<int> _apply;
    private int _pendingUpdates;
    private int _dispatchScheduled;

    public CoalescedUiUpdateScheduler(Action<Action> dispatch, Action<int> apply)
    {
        _dispatch = dispatch;
        _apply = apply;
    }

    public void Request(int updates)
    {
        if (updates == 0)
        {
            return;
        }

        Interlocked.Or(ref _pendingUpdates, updates);
        TrySchedule();
    }

    private void TrySchedule()
    {
        if (Interlocked.CompareExchange(ref _dispatchScheduled, 1, 0) != 0)
        {
            return;
        }

        try
        {
            _dispatch(Drain);
        }
        catch
        {
            Interlocked.Exchange(ref _dispatchScheduled, 0);
            throw;
        }
    }

    private void Drain()
    {
        var updates = Interlocked.Exchange(ref _pendingUpdates, 0);
        if (updates != 0)
        {
            _apply(updates);
        }

        Interlocked.Exchange(ref _dispatchScheduled, 0);
        if (Volatile.Read(ref _pendingUpdates) != 0)
        {
            TrySchedule();
        }
    }
}
