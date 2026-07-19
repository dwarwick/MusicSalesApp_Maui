namespace MusicSalesApp.Maui.Services;

public interface IAudioVisualizerLifecycleCoordinator
{
    void Register(IAudioVisualizerService service);

    void Unregister(IAudioVisualizerService service);

    void OnApplicationStopped();

    void OnApplicationResumed();
}

public sealed class AudioVisualizerLifecycleCoordinator : IAudioVisualizerLifecycleCoordinator
{
    private readonly object _sync = new();
    private WeakReference<IAudioVisualizerService>? _service;

    public void Register(IAudioVisualizerService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        lock (_sync)
        {
            _service = new WeakReference<IAudioVisualizerService>(service);
        }
    }

    public void Unregister(IAudioVisualizerService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        lock (_sync)
        {
            if (_service != null &&
                _service.TryGetTarget(out var registeredService) &&
                ReferenceEquals(service, registeredService))
            {
                _service = null;
            }
        }
    }

    public void OnApplicationStopped()
    {
        if (TryGetService(out var service))
        {
            service.Suspend();
        }
    }

    public void OnApplicationResumed()
    {
        if (TryGetService(out var service))
        {
            _ = service.EnsureInitializedAsync();
        }
    }

    private bool TryGetService(out IAudioVisualizerService service)
    {
        lock (_sync)
        {
            if (_service != null && _service.TryGetTarget(out var registeredService))
            {
                service = registeredService;
                return true;
            }

            _service = null;
            service = null!;
            return false;
        }
    }
}
