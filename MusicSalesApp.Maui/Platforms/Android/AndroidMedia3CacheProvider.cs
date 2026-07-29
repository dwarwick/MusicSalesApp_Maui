using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Media3.DataSource;
using AndroidX.Media3.DataSource.Cache;
using AndroidX.Media3.Database;
using AndroidX.Media3.ExoPlayer.Offline;
using AndroidX.Media3.ExoPlayer.Source;
using Java.IO;
using Java.Util.Concurrent;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;
using MusicSalesApp.Maui.Services;
using Media3DownloadManager = AndroidX.Media3.ExoPlayer.Offline.DownloadManager;

namespace MusicSalesApp.Maui.Platforms.Android;

internal static class AndroidMedia3CacheProvider
{
    private const string CacheDirectoryName = "media3-download-cache";
    private const string DownloadExecutorThreadPrefix = "streamtunes-media3-download";

    private static readonly object Sync = new();
    private static StandaloneDatabaseProvider? _databaseProvider;
    private static SimpleCache? _cache;
    private static Media3DownloadManager? _downloadManager;
    private static IExecutorService? _downloadExecutor;
    private static Task<SimpleCache>? _cacheInitializationTask;

    public static StandaloneDatabaseProvider GetDatabaseProvider(Context context)
    {
        lock (Sync)
        {
            return _databaseProvider ??= new StandaloneDatabaseProvider(context.ApplicationContext);
        }
    }

    /// <summary>
    /// Size for the cache evictor, read straight from preferences because the cache is built from a
    /// static context that has no access to DI. Uses the same pure function as
    /// <c>IOfflineCacheSettingsService.GetAudioCacheLimitBytes</c>, so the ceiling the cache trims
    /// itself to and the ceiling that gates new downloads can never disagree.
    ///
    /// Read once per process: changing the limit in settings takes effect on the next app start.
    /// </summary>
    private static long GetAudioCacheLimitBytes()
    {
        try
        {
            var configuredLimitMb = Preferences.Default.Get(
                OfflineCacheSettingsService.CacheLimitPreferenceKey,
                OfflineCacheSettingsService.DefaultCacheLimitMegabytesValue);
            return OfflineCacheSettingsService.ComputeAudioCacheLimitBytes(configuredLimitMb);
        }
        catch (Exception)
        {
            // Never let a preference read stop the cache being created - without a cache there is no
            // playback at all.
            return OfflineCacheSettingsService.ComputeAudioCacheLimitBytes(
                OfflineCacheSettingsService.DefaultCacheLimitMegabytesValue);
        }
    }

    /// <summary>
    /// The shared Media3 cache. Both the download manager and playback write into it, so it needs an
    /// evictor: playback streams through <see cref="GetCacheDataSourceFactory"/>, which has a write
    /// sink, so simply listening to music grows the cache whether or not anything was ever explicitly
    /// downloaded. With the previous NoOpCacheEvictor it grew without limit, and once it passed the
    /// download ceiling <c>AndroidMedia3AudioCacheService</c> refused every new download - permanently,
    /// because nothing ever shrank it again. Observed on device at 1.02 GB and still climbing.
    ///
    /// Interaction with the sleep-safe contract: LRU only evicts while writing, and writing only
    /// happens with a network connection, so cached content cannot be evicted out from under a sleeping
    /// or offline device. A freshly prepared queue item counts as most-recently-used, so eviction
    /// victims are old content rather than the active queue. Readiness is re-resolved from the cache on
    /// every check, so anything that does get evicted is simply re-prepared rather than played wrong.
    /// </summary>
    public static SimpleCache GetCache(Context context)
    {
        lock (Sync)
        {
            if (_cache != null)
            {
                return _cache;
            }

            var downloadDirectory = GetDownloadDirectory(context);

            _cache = new SimpleCache(
                downloadDirectory,
                new LeastRecentlyUsedCacheEvictor(GetAudioCacheLimitBytes()),
                GetDatabaseProvider(context));

            return _cache;
        }
    }

    public static Task<SimpleCache> GetCacheAsync(Context context)
    {
        lock (Sync)
        {
            if (_cache != null)
            {
                return Task.FromResult(_cache);
            }

            // Retry on fault/cancel rather than caching a broken task forever: a single
            // transient SimpleCache construction failure must not permanently disable every
            // cache/download/player path for the process lifetime.
            if (_cacheInitializationTask is null or { IsFaulted: true } or { IsCanceled: true })
            {
                _cacheInitializationTask = Task.Run(() => GetCache(context));
            }

            return _cacheInitializationTask;
        }
    }

    public static IDataSourceFactory GetUpstreamDataSourceFactory() =>
        new DefaultHttpDataSource.Factory();

    public static CacheDataSource.Factory GetCacheDataSourceFactory(Context context)
    {
        var cache = GetCache(context);
        var cacheSinkFactory = new CacheDataSink.Factory();
        cacheSinkFactory.SetCache(cache);

        var cacheDataSourceFactory = new CacheDataSource.Factory();
        cacheDataSourceFactory.SetCache(cache);
        cacheDataSourceFactory.SetUpstreamDataSourceFactory(GetUpstreamDataSourceFactory());
        cacheDataSourceFactory.SetCacheWriteDataSinkFactory(cacheSinkFactory);
        return cacheDataSourceFactory;
    }

    public static DefaultMediaSourceFactory GetMediaSourceFactory(Context context) =>
        new(GetCacheDataSourceFactory(context));

    public static Media3DownloadManager GetDownloadManager(Context context)
    {
        lock (Sync)
        {
            if (_downloadManager != null)
            {
                return _downloadManager;
            }

            _downloadExecutor ??= Executors.NewFixedThreadPool(
                2,
                new NamedThreadFactory(DownloadExecutorThreadPrefix));

            _downloadManager = new Media3DownloadManager(
                context.ApplicationContext,
                GetDatabaseProvider(context),
                GetCache(context),
                GetUpstreamDataSourceFactory(),
                _downloadExecutor);

            _downloadManager.MaxParallelDownloads = 2;
            _downloadManager.MinRetryCount = 3;
            return _downloadManager;
        }
    }

    public static async Task<Media3DownloadManager> GetDownloadManagerAsync(Context context)
    {
        await GetCacheAsync(context).ConfigureAwait(false);
        return await MainThread.InvokeOnMainThreadAsync(() => GetDownloadManager(context));
    }

    public static void EnsureNotificationChannels(Context context)
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O)
        {
            return;
        }

        var notificationManager = context.GetSystemService(Context.NotificationService) as NotificationManager;
        if (notificationManager == null)
        {
            return;
        }

        notificationManager.CreateNotificationChannel(new NotificationChannel(
            AndroidMedia3Constants.PlaybackNotificationChannelId,
            "Playback",
            NotificationImportance.Low));
    }

    public static void RemoveDownload(Context context, string stableCacheKey)
    {
        if (string.IsNullOrWhiteSpace(stableCacheKey))
        {
            return;
        }

        GetDownloadManager(context).RemoveDownload(stableCacheKey);
        GetCache(context).RemoveResource(stableCacheKey);
    }

    public static async Task RemoveDownloadAsync(
        Context context,
        string stableCacheKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(stableCacheKey))
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var downloadManager = await GetDownloadManagerAsync(context).ConfigureAwait(false);
        await MainThread.InvokeOnMainThreadAsync(() => downloadManager.RemoveDownload(stableCacheKey));
        var cache = await GetCacheAsync(context).ConfigureAwait(false);
        await Task.Run(() => cache.RemoveResource(stableCacheKey), cancellationToken).ConfigureAwait(false);
    }

    public static long GetCacheSizeBytes(Context context) => GetDirectorySize(GetDownloadDirectory(context));

    public static Task<long> GetCacheSizeBytesAsync(Context context, CancellationToken cancellationToken = default) =>
        Task.Run(() => GetCacheSizeBytes(context), cancellationToken);

    public static long GetAvailableCacheStorageBytes(Context context)
    {
        var path = GetDownloadDirectory(context).AbsolutePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return 0;
        }

        return new StatFs(path).AvailableBytes;
    }

    public static Task<long> GetAvailableCacheStorageBytesAsync(
        Context context,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => GetAvailableCacheStorageBytes(context), cancellationToken);

    public static void Release()
    {
        lock (Sync)
        {
            _downloadManager?.Release();
            _downloadManager = null;

            _downloadExecutor?.Shutdown();
            _downloadExecutor = null;

            _cache?.Release();
            _cache = null;
            _cacheInitializationTask = null;

            _databaseProvider = null;
        }
    }

    private static Java.IO.File GetDownloadDirectory(Context context)
    {
        var downloadDirectory = new Java.IO.File(context.CacheDir, CacheDirectoryName);
        if (!downloadDirectory.Exists())
        {
            downloadDirectory.Mkdirs();
        }

        return downloadDirectory;
    }

    private static long GetDirectorySize(Java.IO.File file)
    {
        if (!file.Exists())
        {
            return 0;
        }

        if (file.IsFile)
        {
            return Math.Max(0, file.Length());
        }

        var children = file.ListFiles();
        if (children == null)
        {
            return 0;
        }

        long total = 0;
        foreach (var child in children)
        {
            total += GetDirectorySize(child);
        }

        return total;
    }

    private sealed class NamedThreadFactory(string prefix) : Java.Lang.Object, IThreadFactory
    {
        private int _threadNumber;

        public Java.Lang.Thread NewThread(Java.Lang.IRunnable? runnable)
        {
            var threadName = $"{prefix}-{Interlocked.Increment(ref _threadNumber)}";
            return new Java.Lang.Thread(runnable, threadName);
        }
    }
}
