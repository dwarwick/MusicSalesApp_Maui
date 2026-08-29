using Android.App;
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

    // Synchronous session creation for MediaSessionService.OnGetSession, which runs on the main
    // thread and must return a non-null session immediately (Media3 rejects a null result). We
    // can't block on the async path there — it marshals back to the main thread and would
    // deadlock — so build directly on the current (main) thread using the synchronous cache.
    public static MediaSession GetOrCreateMediaSessionSync(Context context)
    {
        var applicationContext = context.ApplicationContext ?? context;
        var player = CreateOrGetPlayerCore(applicationContext);
        lock (Sync)
        {
            if (_mediaSession != null)
            {
                return _mediaSession;
            }

            _mediaSession = BuildMediaSession(applicationContext, player);
            _mediaSessionInitializationTask = Task.FromResult(_mediaSession);
            return _mediaSession;
        }
    }

    private static async Task<IExoPlayer> InitializePlayerAsync(Context context)
    {
        await AndroidMedia3CacheProvider.GetCacheAsync(context).ConfigureAwait(false);
        return await MainThread.InvokeOnMainThreadAsync(() => CreateOrGetPlayerCore(context));
    }

    private static async Task<MediaSession> InitializeMediaSessionAsync(Context context)
    {
        var player = await GetOrCreatePlayerAsync(context).ConfigureAwait(false);
        return await MainThread.InvokeOnMainThreadAsync(() => CreateOrGetMediaSessionCore(context, player));
    }

    // Builds (or returns) the shared player. MUST be called on the main thread — the native
    // ExoPlayer builder and SimpleCache access require it.
    private static IExoPlayer CreateOrGetPlayerCore(Context context)
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
            _playerInitializationTask = Task.FromResult(_player);
            return _player;
        }
    }

    // Builds (or returns) the shared media session. MUST be called on the main thread.
    private static MediaSession CreateOrGetMediaSessionCore(Context context, IExoPlayer player)
    {
        lock (Sync)
        {
            if (_mediaSession != null)
            {
                return _mediaSession;
            }

            _mediaSession = BuildMediaSession(context, player);
            _mediaSessionInitializationTask = Task.FromResult(_mediaSession);
            return _mediaSession;
        }
    }

    // Both session-creation paths funnel through here so the notification's content intent is
    // never set on only one of them: the async path serves normal in-app playback, while
    // GetOrCreateMediaSessionSync serves the system-initiated cold start (media button,
    // Bluetooth, playback resumption) - exactly the case where getting back to the app matters.
    private static MediaSession BuildMediaSession(Context context, IExoPlayer player)
    {
        var builder = new MediaSession.Builder(context, player);

        var sessionActivity = CreateSessionActivityIntent(context);
        if (sessionActivity != null)
        {
            builder.SetSessionActivity(sessionActivity);
        }

        return builder.Build()
            ?? throw new InvalidOperationException("Media3 MediaSession.Builder returned null.");
    }

    // Media3's DefaultMediaNotificationProvider uses the session activity as the notification's
    // content intent, so without one the notification body is a dead tap - only the transport
    // buttons respond. This is the package launch intent (ACTION_MAIN + CATEGORY_LAUNCHER) rather
    // than a bare explicit Intent because it matches the task's base intent, so the system resumes
    // the existing task on whatever page the user left instead of restarting MainActivity into a
    // duplicate. MainActivity.HandleDeepLink ignores it (it requires ACTION_VIEW plus a data URI),
    // so this cannot trigger a spurious app-link navigation.
    private static PendingIntent? CreateSessionActivityIntent(Context context)
    {
        var launchIntent = context.PackageName is { } packageName
            ? context.PackageManager?.GetLaunchIntentForPackage(packageName)
            : null;

        launchIntent ??= new Intent(context, typeof(MainActivity))
            .SetFlags(ActivityFlags.NewTask);

        return PendingIntent.GetActivity(
            context,
            0,
            launchIntent,
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
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
