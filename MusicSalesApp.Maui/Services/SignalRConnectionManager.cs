using Microsoft.Extensions.Logging;
using Microsoft.Maui.Networking;

namespace MusicSalesApp.Maui.Services;

public interface ISignalRConnectionManager
{
    Task InitializeAsync();

    Task HandleAppResumeAsync();
}

public sealed class SignalRConnectionManager : ISignalRConnectionManager, IDisposable
{
    private readonly ISignalRService _signalRService;
    private readonly IConnectivity _connectivity;
    private readonly ILogger<SignalRConnectionManager> _logger;
    private readonly SemaphoreSlim _startLock = new(1, 1);

    public SignalRConnectionManager(
        ISignalRService signalRService,
        IConnectivity connectivity,
        ILogger<SignalRConnectionManager> logger)
    {
        _signalRService = signalRService;
        _connectivity = connectivity;
        _logger = logger;

        _connectivity.ConnectivityChanged += OnConnectivityChanged;
    }

    public Task InitializeAsync() => EnsureStartedAsync("startup");

    public Task HandleAppResumeAsync() => EnsureStartedAsync("resume");

    public void Dispose()
    {
        _connectivity.ConnectivityChanged -= OnConnectivityChanged;
        _startLock.Dispose();
    }

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        if (e.NetworkAccess != NetworkAccess.Internet)
        {
            return;
        }

        _ = EnsureStartedAsync("connectivity-restored");
    }

    private async Task EnsureStartedAsync(string reason)
    {
        await _startLock.WaitAsync();

        try
        {
            _logger.LogInformation("SignalR connection manager ensuring hubs are started after {Reason}", reason);
            await _signalRService.StartAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SignalR connection manager failed to start hubs after {Reason}", reason);
        }
        finally
        {
            _startLock.Release();
        }
    }
}