namespace MusicSalesApp.Maui.Services;

internal readonly record struct SignalRStartTarget(
    string Name,
    Func<bool> ShouldStart,
    Func<Task> StartAsync);

internal sealed class SignalRConnectionStarter
{
    private readonly SemaphoreSlim _startLock = new(1, 1);

    public async Task StartAsync(
        IReadOnlyCollection<SignalRStartTarget> targets,
        Action<string>? onConnected = null,
        Action<string, Exception>? onFailed = null)
    {
        await _startLock.WaitAsync().ConfigureAwait(false);

        try
        {
            foreach (var target in targets)
            {
                if (!target.ShouldStart())
                {
                    continue;
                }

                try
                {
                    await target.StartAsync().ConfigureAwait(false);
                    onConnected?.Invoke(target.Name);
                }
                catch (Exception ex)
                {
                    onFailed?.Invoke(target.Name, ex);
                }
            }
        }
        finally
        {
            _startLock.Release();
        }
    }
}