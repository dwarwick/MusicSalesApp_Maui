using Microsoft.Extensions.Logging;

namespace MusicSalesApp.Maui.Services;

public interface IAppActivationCoordinator
{
    Task HandleActivationAsync();
}

public sealed class AppActivationCoordinator : IAppActivationCoordinator
{
    private readonly IMusicService _musicService;
    private readonly ISignalRConnectionManager _signalRConnectionManager;
    private readonly ILogger<AppActivationCoordinator> _logger;

    public AppActivationCoordinator(
        IMusicService musicService,
        ISignalRConnectionManager signalRConnectionManager,
        ILogger<AppActivationCoordinator> logger)
    {
        _musicService = musicService;
        _signalRConnectionManager = signalRConnectionManager;
        _logger = logger;
    }

    public async Task HandleActivationAsync()
    {
        try
        {
            await _musicService.FlushPendingStreamRecordsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to flush pending stream records during app activation");
        }

        await _signalRConnectionManager.HandleAppResumeAsync();
    }
}