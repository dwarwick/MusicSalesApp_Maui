using Android.Content;
using AndroidX.Media3.Common;
using AndroidX.Media3.ExoPlayer;
using AndroidX.Media3.Session;
using Microsoft.Maui.ApplicationModel;

namespace MusicSalesApp.Maui.Platforms.Android;

internal static class AndroidMedia3PlaybackRegistry
{
    private static readonly object Sync = new();
    private static IExoPlayer? _player;
    private static MediaSession? _mediaSession;
    private static Task<IExoPlayer>? _playerInitializationTask;
    private static Task<MediaSession>? _mediaSessionInitializationTask;

    public static Task<IExoPlayer> GetOrCreatePlayerAsync(Context context)
    {
        lock (Sync)
        {
            // IExoPlayer.IsReleased is a native call that ExoPlayer only allows on the main
            // thread, and this method is reached from background continuations. ReleasePlayer()
            // nulls _player under this lock, so the managed null check is the release check.
            if (_player != null)
            {
                return Task.FromResult(_player);
            }

            if (_playerInitializationTask is null or { IsFaulted: true } or { IsCanceled: true })
            {
                _playerInitializationTask = InitializePlayerAsync(context.ApplicationContext ?? context);
            }

            return _playerInitializationTask;
        }
    }

    public static Task<MediaSession> GetOrCreateMediaSessionAsync(Context context)
    {
        lock (Sync)
        {
            if (_mediaSession != null)
            {
                return Task.FromResult(_mediaSession);
            }

            if (_mediaSessionInitializationTask is null or { IsFaulted: true } or { IsCanceled: true })
            {
                _mediaSessionInitializationTask = InitializeMediaSessionAsync(context.ApplicationContext ?? context);
            }

            return _mediaSessionInitializationTask;
        }
    }

    public static MediaSession? TryGetMediaSession()
    {
        lock (Sync)
        {
            return _mediaSession;
        }
    }

    private static async Task<IExoPlayer> InitializePlayerAsync(Context context)
    {
        await AndroidMedia3CacheProvider.GetCacheAsync(context).ConfigureAwait(false);
        return await MainThread.InvokeOnMainThreadAsync(() =>
        {
            lock (Sync)
            {
                if (_player != null && !_player.IsReleased)
                {
                    return _player;
                }

                AndroidMedia3CacheProvider.EnsureNotificationChannels(context);
                var builder = new ExoPlayerBuilder(context);
                builder.SetMediaSourceFactory(AndroidMedia3CacheProvider.GetMediaSourceFactory(context));
                builder.SetHandleAudioBecomingNoisy(true);

                _player = builder.Build()
                    ?? throw new InvalidOperationException("Media3 ExoPlayerBuilder returned null.");
                _player.SetWakeMode(C.WakeModeNetwork);
                return _player;
            }
        });
    }

    private static async Task<MediaSession> InitializeMediaSessionAsync(Context context)
    {
        var player = await GetOrCreatePlayerAsync(context).ConfigureAwait(false);
        return await MainThread.InvokeOnMainThreadAsync(() =>
        {
            lock (Sync)
            {
                if (_mediaSession != null)
                {
                    return _mediaSession;
                }

                _mediaSession = new MediaSession.Builder(context, player)
                    .Build()
                    ?? throw new InvalidOperationException("Media3 MediaSession.Builder returned null.");
                return _mediaSession;
            }
        });
    }

    public static void ReleaseMediaSession()
    {
        lock (Sync)
        {
            _mediaSession?.Release();
            _mediaSession = null;
            _mediaSessionInitializationTask = null;
        }
    }

    public static void ReleasePlayer()
    {
        lock (Sync)
        {
            _mediaSession?.Release();
            _mediaSession = null;
            _mediaSessionInitializationTask = null;

            _player?.Release();
            _player = null;
            _playerInitializationTask = null;
        }
    }
}
