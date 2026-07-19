using Android.App;
using Android.Content.PM;
using AndroidX.Media3.Session;
using Microsoft.Maui.ApplicationModel;

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
    private bool _isStoppingOrDestroyed;

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
        _isStoppingOrDestroyed = false;
        lock (ActiveServiceSync)
        {
            _activeService = new WeakReference<PlaybackMediaSessionService>(this);
        }

        _ = AttachMediaSessionAsync();
    }

    public override MediaSession? OnGetSession(MediaSession.ControllerInfo? controllerInfo)
    {
        return _mediaSession ?? AndroidMedia3PlaybackRegistry.TryGetMediaSession();
    }

    public override void OnDestroy()
    {
        _isStoppingOrDestroyed = true;
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
        _isStoppingOrDestroyed = true;
        ReleaseCurrentSession();
        StopForeground(StopForegroundFlags.Remove);
        StopSelf();
    }

    private async Task AttachMediaSessionAsync()
    {
        try
        {
            var mediaSession = await AndroidMedia3PlaybackRegistry
                .GetOrCreateMediaSessionAsync(this)
                .ConfigureAwait(false);
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (_isStoppingOrDestroyed || _mediaSession != null)
                {
                    return;
                }

                _mediaSession = mediaSession;
                AddSession(mediaSession);
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Unable to initialize Media3 playback session: {ex}");
        }
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
