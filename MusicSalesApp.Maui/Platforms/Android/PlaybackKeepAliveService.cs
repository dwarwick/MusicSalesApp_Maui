using Android.Content;
using Android.Net.Wifi;
using Android.OS;
using Microsoft.Extensions.Logging;
using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.Platforms.Android;

public sealed class PlaybackKeepAliveService : IPlaybackKeepAliveService, IDisposable
{
    private const string WakeLockTag = "net.streamtunes.musicsalesapp.maui:PlaybackKeepAlive";
    private const string WifiLockTag = "net.streamtunes.musicsalesapp.maui:PlaybackWifiKeepAlive";

    private readonly object _sync = new();
    private readonly PlaybackKeepAliveCoordinator _coordinator;
    private readonly ILogger<PlaybackKeepAliveService> _logger;
    private PowerManager.WakeLock? _wakeLock;
    private WifiManager.WifiLock? _wifiLock;

    public PlaybackKeepAliveService(ILogger<PlaybackKeepAliveService> logger)
    {
        _logger = logger;
        _coordinator = new PlaybackKeepAliveCoordinator(AcquireLocks, ReleaseLocks);
    }

    public void SetPlaybackActive(bool isActive)
    {
        lock (_sync)
        {
            _coordinator.SetPlaybackActive(isActive);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _coordinator.Dispose();
        }
    }

    private void AcquireLocks()
    {
        _logger.LogInformation("Playback keep-alive activating CPU and Wi-Fi locks.");
        AcquireWakeLock();
        AcquireWifiLock();
    }

    private void AcquireWakeLock()
    {
        try
        {
            var powerManager = global::Android.App.Application.Context.GetSystemService(Context.PowerService) as PowerManager;
            if (powerManager == null)
            {
                _logger.LogWarning("Playback keep-alive could not acquire CPU wake lock because PowerManager was unavailable.");
                return;
            }

            var wakeLock = _wakeLock ??= CreateWakeLock(powerManager);

            if (wakeLock == null || wakeLock.IsHeld)
            {
                return;
            }

            wakeLock.Acquire();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Playback keep-alive failed to acquire CPU wake lock.");
        }
    }

    private void AcquireWifiLock()
    {
        try
        {
            var wifiManager = global::Android.App.Application.Context.GetSystemService(Context.WifiService) as WifiManager;
            if (wifiManager == null)
            {
                _logger.LogWarning("Playback keep-alive could not acquire Wi-Fi lock because WifiManager was unavailable.");
                return;
            }

            var wifiLock = _wifiLock ??= CreateWifiLock(wifiManager);

            if (wifiLock == null || wifiLock.IsHeld)
            {
                return;
            }

            wifiLock.Acquire();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Playback keep-alive failed to acquire Wi-Fi lock.");
        }
    }

    private static PowerManager.WakeLock? CreateWakeLock(PowerManager powerManager)
    {
        var wakeLock = powerManager.NewWakeLock(WakeLockFlags.Partial, WakeLockTag);
        if (wakeLock == null)
        {
            return null;
        }

        wakeLock.SetReferenceCounted(false);
        return wakeLock;
    }

    private static WifiManager.WifiLock? CreateWifiLock(WifiManager wifiManager)
    {
        var wifiLock = wifiManager.CreateWifiLock(global::Android.Net.WifiMode.FullHighPerf, WifiLockTag);
        if (wifiLock == null)
        {
            return null;
        }

        wifiLock.SetReferenceCounted(false);
        return wifiLock;
    }

    private void ReleaseLocks()
    {
        _logger.LogInformation("Playback keep-alive releasing CPU and Wi-Fi locks.");
        ReleaseWifiLock();
        ReleaseWakeLock();
    }

    private void ReleaseWakeLock()
    {
        try
        {
            if (_wakeLock?.IsHeld == true)
            {
                _wakeLock.Release();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Playback keep-alive failed to release CPU wake lock.");
        }
    }

    private void ReleaseWifiLock()
    {
        try
        {
            if (_wifiLock?.IsHeld == true)
            {
                _wifiLock.Release();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Playback keep-alive failed to release Wi-Fi lock.");
        }
    }
}