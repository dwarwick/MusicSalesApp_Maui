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
    private readonly IPushNotificationCoordinator? _pushNotificationCoordinator;
    private readonly ILogger<AppActivationCoordinator> _logger;

    public AppActivationCoordinator(
        IMusicService musicService,
        ISignalRConnectionManager signalRConnectionManager,
        ILogger<AppActivationCoordinator> logger,
        IPushNotificationCoordinator? pushNotificationCoordinator = null)
    {
        _musicService = musicService;
        _signalRConnectionManager = signalRConnectionManager;
        _logger = logger;

        // Trailing and optional, matching how the player ViewModels take their optional
        // collaborators - existing tests construct this without knowing about push.
        _pushNotificationCoordinator = pushNotificationCoordinator;
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

        if (_pushNotificationCoordinator is null)
        {
            return;
        }

        try
        {
            // Re-registers this device if the token rotated while the app was away, which is the
            // common case after an OS update or a restore. Never prompts - see SyncAsync.
            await _pushNotificationCoordinator.SyncAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to synchronise push registration during app activation");
        }
    }
}