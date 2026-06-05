using Android.Content;
using AndroidX.Media3.Common;
using AndroidX.Media3.ExoPlayer;
using AndroidX.Media3.Session;

namespace MusicSalesApp.Maui.Platforms.Android;

internal static class AndroidMedia3PlaybackRegistry
{
    private static readonly object Sync = new();
    private static IExoPlayer? _player;
    private static MediaSession? _mediaSession;

    public static IExoPlayer GetOrCreatePlayer(Context context)
    {
        lock (Sync)
        {
            if (_player != null && !_player.IsReleased)
            {
                return _player;
            }

            AndroidMedia3CacheProvider.EnsureNotificationChannels(context);
            var appContext = context.ApplicationContext ?? context;

            var builder = new ExoPlayerBuilder(appContext);
            builder.SetMediaSourceFactory(AndroidMedia3CacheProvider.GetMediaSourceFactory(context));
            builder.SetHandleAudioBecomingNoisy(true);

            _player = builder.Build()
                ?? throw new InvalidOperationException("Media3 ExoPlayerBuilder returned null.");

            _player.SetWakeMode(C.WakeModeNetwork);
            return _player;
        }
    }

    public static MediaSession GetOrCreateMediaSession(Context context)
    {
        lock (Sync)
        {
            if (_mediaSession != null)
            {
                return _mediaSession;
            }

            var appContext = context.ApplicationContext ?? context;
            _mediaSession = new MediaSession.Builder(appContext, GetOrCreatePlayer(context))
                .Build()
                ?? throw new InvalidOperationException("Media3 MediaSession.Builder returned null.");
            return _mediaSession;
        }
    }

    public static void ReleaseMediaSession()
    {
        lock (Sync)
        {
            _mediaSession?.Release();
            _mediaSession = null;
        }
    }

    public static void ReleasePlayer()
    {
        lock (Sync)
        {
            _mediaSession?.Release();
            _mediaSession = null;

            _player?.Release();
            _player = null;
        }
    }
}
