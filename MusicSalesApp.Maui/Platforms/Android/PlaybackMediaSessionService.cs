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
    private MediaSession? _mediaSession;

    public override void OnCreate()
    {
        base.OnCreate();
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
        if (_mediaSession != null)
        {
            RemoveSession(_mediaSession);
            AndroidMedia3PlaybackRegistry.ReleaseMediaSession();
            _mediaSession = null;
        }

        base.OnDestroy();
    }
}
