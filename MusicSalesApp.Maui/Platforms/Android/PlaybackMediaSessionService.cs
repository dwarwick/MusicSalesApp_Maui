using Android.App;
using Android.Content.PM;
using AndroidX.Media3.Session;

namespace MusicSalesApp.Maui.Platforms.Android;

[Service(
    Exported = true,
    ForegroundServiceType = ForegroundService.TypeMediaPlayback)]
[IntentFilter([AndroidMedia3Constants.PlaybackServiceAction])]
public sealed class PlaybackMediaSessionService : MediaSessionService
{
    private static readonly object ActiveServiceSync = new();
    private static WeakReference<PlaybackMediaSessionService>? _activeService;

    private MediaSession? _mediaSession;

    internal static void RequestStop(global::Android.Content.Context context)
    {
        PlaybackMediaSessionService? service = null;
        lock (ActiveServiceSync)
        {
            _activeService?.TryGetTarget(out service);
        }

        if (service != null)
        {
            service.StopPlaybackService();
            return;
        }

        var intent = new global::Android.Content.Intent(context, typeof(PlaybackMediaSessionService));
        context.StopService(intent);
    }

    public override void OnCreate()
    {
        base.OnCreate();
        lock (ActiveServiceSync)
        {
            _activeService = new WeakReference<PlaybackMediaSessionService>(this);
        }

        AndroidMedia3CacheProvider.EnsureNotificationChannels(this);
        _mediaSession = AndroidMedia3PlaybackRegistry.GetOrCreateMediaSession(this);
        AddSession(_mediaSession);
    }

    public override MediaSession? OnGetSession(MediaSession.ControllerInfo? controllerInfo)
    {
        _mediaSession ??= AndroidMedia3PlaybackRegistry.GetOrCreateMediaSession(this);
        return _mediaSession;
    }

    public override void OnDestroy()
    {
        ReleaseCurrentSession();
        lock (ActiveServiceSync)
        {
            if (_activeService != null &&
                _activeService.TryGetTarget(out var activeService) &&
                ReferenceEquals(activeService, this))
            {
                _activeService = null;
            }
        }

        base.OnDestroy();
    }

    private void StopPlaybackService()
    {
        ReleaseCurrentSession();
        StopForeground(StopForegroundFlags.Remove);
        StopSelf();
    }

    private void ReleaseCurrentSession()
    {
        if (_mediaSession == null)
        {
            return;
        }

        RemoveSession(_mediaSession);
        AndroidMedia3PlaybackRegistry.ReleaseMediaSession();
        _mediaSession = null;
    }
}
