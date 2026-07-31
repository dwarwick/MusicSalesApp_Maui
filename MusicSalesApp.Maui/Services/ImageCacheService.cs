using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace MusicSalesApp.Maui.Services;

/// <summary>
/// An image to cache, together with the server's content version for it.
///
/// <para>
/// The version exists because a blob path is not by itself a unique identity: cover art under the
/// GUID naming scheme lives at a fixed path that a re-crop overwrites in place. Without it the
/// cache would keep serving the pre-crop image indefinitely, since the path - and therefore the key
/// - never changed.
/// </para>
/// </summary>
public readonly record struct CachedImageReference(
    string Url,
    int Version = 0,
    string? SupersededBy = null,
    int SupersededByVersion = 0)
{
    /// <summary>
    /// Lets a bare URL stand in wherever no version is available. Version 0 keys exactly as the
    /// unversioned cache always did, so nothing already on disk is orphaned.
    /// </summary>
    public static implicit operator CachedImageReference(string url) => new(url, 0);

    /// <summary>
    /// Retain this image only while <paramref name="supersedingUrl"/> is not yet cached.
    ///
    /// <para>
    /// Used for the full-size master once a 320px rendition exists for it: the master is still the
    /// fallback the display chain reaches for until the thumb has actually been downloaded, but
    /// keeping a multi-megabyte original alongside a twenty-kilobyte thumb permanently would defeat
    /// the point of generating renditions - and the budget has no eviction to recover from it.
    /// </para>
    /// </summary>
    public CachedImageReference RetainedUntilCached(string? supersedingUrl, int supersedingVersion)
        => string.IsNullOrWhiteSpace(supersedingUrl)
            ? this
            : this with { SupersededBy = supersedingUrl, SupersededByVersion = supersedingVersion };
}

/// <summary>
/// On-disk cache for song artwork (album art and creator persona images), so covers still render when
/// the device is offline.
///
/// MAUI's built-in UriImageSource cache cannot do this job: it is keyed on the full URL, and the server
/// mints a fresh SAS query string for every image on every API call, so its entries never match across
/// sessions. Keys here come from <see cref="StableRemoteAssetKey"/>, which hashes the blob path only.
/// </summary>
public interface IImageCacheService
{
    /// <summary>
    /// Local path for an already-cached image, or null. Never performs a download.
    ///
    /// <para>
    /// A file-metadata check plus, at most once per file per process, a short header read. Cheap, but
    /// not free: prefer to call it off the UI thread when resolving a whole list.
    /// </para>
    /// </summary>
    string? TryGetCachedImagePath(string? remoteImageUrl, int contentVersion = 0);

    /// <summary>
    /// Returns the local path, downloading the image first if necessary. Returns null rather than
    /// throwing on any failure, and never attempts a download while offline.
    /// </summary>
    Task<string?> EnsureCachedAsync(string? remoteImageUrl, CancellationToken cancellationToken = default);

    /// <summary>
    /// As <see cref="EnsureCachedAsync(string?, CancellationToken)"/>, but declaring how important
    /// this image is so the budget can be shared sensibly between the two tiers.
    /// </summary>
    Task<string?> EnsureCachedAsync(
        string? remoteImageUrl,
        ImageCachePriority priority,
        int contentVersion = 0,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// As <see cref="EnsureCachedAsync(string?, ImageCachePriority, int, CancellationToken)"/>, but
    /// reporting <em>why</em> nothing was cached.
    ///
    /// <para>
    /// Callers that ration their retries need this: an image the budget turned away has nothing wrong
    /// with it and should be attempted again once space is freed, whereas a download that genuinely
    /// failed should eventually stop being retried. Collapsing both into a null path makes the
    /// retry limit blacklist perfectly good images.
    /// </para>
    /// </summary>
    Task<ImageCacheOutcome> TryEnsureCachedAsync(
        string? remoteImageUrl,
        ImageCachePriority priority,
        int contentVersion = 0,
        CancellationToken cancellationToken = default);

    Task<long> GetCacheUsageBytesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes cached images not named by <paramref name="retainedImages"/>.
    ///
    /// <para>
    /// Versions matter here as much as URLs: a retained image at version 3 keeps only its version-3
    /// file, so the superseded copies left by earlier versions are swept up rather than accumulating
    /// against the budget forever.
    /// </para>
    /// </summary>
    Task PruneAsync(
        IReadOnlyCollection<CachedImageReference> retainedImages,
        CancellationToken cancellationToken = default);
}

/// <summary>Why a caching attempt ended the way it did.</summary>
public enum ImageCacheResult
{
    /// <summary>The image is on disk, either already or as a result of this call.</summary>
    Cached = 0,

    /// <summary>Nothing to do: the URL was absent or unusable.</summary>
    NoImage = 1,

    /// <summary>
    /// Turned away by the cache budget. Nothing is wrong with the image; it should be attempted
    /// again once a prune frees space, and must not count against any retry limit.
    /// </summary>
    Declined = 2,

    /// <summary>Not attempted because the device has no network at all. Also not a failure.</summary>
    Offline = 3,

    /// <summary>The download was attempted and did not produce a usable image.</summary>
    Failed = 4
}

/// <summary>The local path, where there is one, and the reason behind it.</summary>
public readonly record struct ImageCacheOutcome(string? Path, ImageCacheResult Result)
{
    /// <summary>Whether a retry later stands a chance. False only for a genuine download failure.</summary>
    public bool IsWorthRetrying => Result is ImageCacheResult.Declined or ImageCacheResult.Offline;
}

/// <summary>
/// How much of the image cache budget an image is entitled to.
///
/// <para>
/// There is no eviction: once the budget is reached downloads simply stop. With two tiers competing
/// that would be actively harmful - a few hundred large hero images could consume the whole budget
/// and leave the list rows, the mini player and the lock screen with no artwork at all, which is
/// worse than the behaviour this feature replaced.
/// </para>
/// </summary>
public enum ImageCachePriority
{
    /// <summary>Cached while any budget remains. The artwork every small surface falls back to.</summary>
    Thumb = 0,

    /// <summary>
    /// Cached only while the cache is below <see cref="ImageCacheService.HeroBudgetFraction"/> of
    /// its limit, so heroes can never crowd out thumbs.
    /// </summary>
    Hero = 1
}

public sealed class ImageCacheService : IImageCacheService
{
    /// <summary>
    /// A floor cheap enough to reject an obviously-truncated write before the signature check runs.
    ///
    /// <para>
    /// This used to be 512 bytes, on the reasoning that an Azure "AuthenticationFailed" XML body from
    /// an expired SAS is a few hundred bytes. That became a trap once pre-resized renditions arrived:
    /// a small WebP of flat-coloured artwork is legitimately under half a kilobyte, and it would be
    /// deleted and re-downloaded forever. <see cref="LooksLikeImage"/> now does the real work, and
    /// this is only a pre-filter.
    /// </para>
    /// </summary>
    internal const long MinPlausibleImageBytes = 64;

    /// <summary>
    /// The share of the budget heroes may occupy. Above this, only thumbs are still cached.
    /// </summary>
    internal const double HeroBudgetFraction = 0.6;

    private const string DefaultImageExtension = ".img";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ImageCacheService> _logger;
    private readonly IOfflineCacheSettingsService? _offlineCacheSettingsService;
    private readonly INetworkStatusService? _networkStatusService;
    private readonly string _cacheDirectory;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _downloadLocks = new();

    /// <summary>
    /// Files whose image signature has already been verified, keyed by path. Bounded in practice by
    /// the cache budget itself - there cannot be more entries than there are files on disk.
    /// </summary>
    private readonly ConcurrentDictionary<string, ValidatedFile> _validatedFiles = new();

    public ImageCacheService(
        IHttpClientFactory httpClientFactory,
        ILogger<ImageCacheService> logger,
        IOfflineCacheSettingsService offlineCacheSettingsService,
        INetworkStatusService networkStatusService)
        : this(
            httpClientFactory,
            logger,
            Path.Combine(FileSystem.Current.CacheDirectory, "image-cache"),
            offlineCacheSettingsService,
            networkStatusService)
    {
    }

    public ImageCacheService(
        IHttpClientFactory httpClientFactory,
        ILogger<ImageCacheService> logger,
        string cacheDirectory,
        IOfflineCacheSettingsService? offlineCacheSettingsService = null,
        INetworkStatusService? networkStatusService = null)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _cacheDirectory = cacheDirectory;
        _offlineCacheSettingsService = offlineCacheSettingsService;
        _networkStatusService = networkStatusService;

        // Off the constructor's thread: this service is resolved during startup on the main thread, and
        // the sweep enumerates a directory and stats every file in it. Nothing waits on the result.
        StaleTemporaryFileSweep = Task.Run(SweepStaleTemporaryFiles);
    }

    /// <summary>Completion of the background startup sweep. Exposed so tests need not poll.</summary>
    internal Task StaleTemporaryFileSweep { get; }

    public string? TryGetCachedImagePath(string? remoteImageUrl, int contentVersion = 0)
    {
        if (!StableRemoteAssetKey.TryGetAbsoluteUri(remoteImageUrl, out var remoteUri))
        {
            return null;
        }

        var cachePath = GetCachePath(remoteUri, contentVersion);
        if (HasCachedFile(cachePath))
        {
            return cachePath;
        }

        // Migration path, and the only reason a lookup ever considers a second key. Every copy cached
        // by a build that predates versioning sits under the version-0 key; the moment the server
        // starts reporting a nonzero version - which the backfill does for every image at once - those
        // files become unreachable and a user with a downloaded offline library loses every cover.
        //
        // Serving the legacy copy risks showing pre-crop pixels for one session, until the correct
        // file downloads. That is strictly better than showing nothing, and it is self-limiting: the
        // prune reclaims each legacy copy as soon as its versioned replacement lands.
        if (contentVersion > 0)
        {
            var legacyPath = GetCachePath(remoteUri, 0);
            if (HasCachedFile(legacyPath))
            {
                return legacyPath;
            }
        }

        return null;
    }

    public Task<string?> EnsureCachedAsync(string? remoteImageUrl, CancellationToken cancellationToken = default)
        => EnsureCachedAsync(remoteImageUrl, ImageCachePriority.Thumb, 0, cancellationToken);

    public async Task<string?> EnsureCachedAsync(
        string? remoteImageUrl,
        ImageCachePriority priority,
        int contentVersion = 0,
        CancellationToken cancellationToken = default)
        => (await TryEnsureCachedAsync(remoteImageUrl, priority, contentVersion, cancellationToken)
            .ConfigureAwait(false)).Path;

    public async Task<ImageCacheOutcome> TryEnsureCachedAsync(
        string? remoteImageUrl,
        ImageCachePriority priority,
        int contentVersion = 0,
        CancellationToken cancellationToken = default)
    {
        if (!StableRemoteAssetKey.TryGetAbsoluteUri(remoteImageUrl, out var remoteUri))
        {
            return new ImageCacheOutcome(null, ImageCacheResult.NoImage);
        }

        var cachePath = GetCachePath(remoteUri, contentVersion);
        if (await Task.Run(() => HasCachedFile(cachePath), cancellationToken).ConfigureAwait(false))
        {
            return new ImageCacheOutcome(cachePath, ImageCacheResult.Cached);
        }

        // With no network at all the request cannot succeed and would only stall for the client
        // timeout, so skip it; the caller falls back to the built-in placeholder artwork. Only
        // NetworkAccess.None qualifies - on a constrained connection the download usually works, and
        // giving up would leave covers blank for no reason.
        if (_networkStatusService?.HasNoNetworkAccess == true)
        {
            return new ImageCacheOutcome(null, ImageCacheResult.Offline);
        }

        var downloadLock = _downloadLocks.GetOrAdd(cachePath, _ => new SemaphoreSlim(1, 1));
        await downloadLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        var temporaryPath = cachePath + ".tmp";
        try
        {
            if (await Task.Run(() => HasCachedFile(cachePath), cancellationToken).ConfigureAwait(false))
            {
                return new ImageCacheOutcome(cachePath, ImageCacheResult.Cached);
            }

            if (!CanQueueDownload(remoteUri, null, priority))
            {
                return new ImageCacheOutcome(null, ImageCacheResult.Declined);
            }

            Directory.CreateDirectory(_cacheDirectory);

            // Reuses the audio-download client deliberately: it carries no BaseAddress, no bearer token
            // and no X-Api-Key. Azure SAS URLs are self-authenticating, and sending the app's
            // credentials to blob storage would be wrong. Do not "fix" this to use MusicSalesApi.
            var client = _httpClientFactory.CreateClient(AudioCacheService.AudioDownloadClientName);
            using var response = await client
                .GetAsync(remoteUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug(
                    "Image cache download failed. StatusCode={StatusCode}; ImagePath={ImagePath}",
                    response.StatusCode,
                    remoteUri.AbsolutePath);
                return new ImageCacheOutcome(null, ImageCacheResult.Failed);
            }

            if (!CanQueueDownload(remoteUri, response.Content.Headers.ContentLength, priority))
            {
                return new ImageCacheOutcome(null, ImageCacheResult.Declined);
            }

            DeleteFileIfPresent(temporaryPath);

            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var destination = File.Create(temporaryPath))
            {
                await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
                await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            var downloadedBytes = new FileInfo(temporaryPath).Length;
            if (downloadedBytes < MinPlausibleImageBytes || !LooksLikeImage(temporaryPath))
            {
                _logger.LogDebug(
                    "Image cache download rejected because the payload is not a recognisable image. ImagePath={ImagePath}; DownloadedBytes={DownloadedBytes}",
                    remoteUri.AbsolutePath,
                    downloadedBytes);
                return new ImageCacheOutcome(null, ImageCacheResult.Failed);
            }

            File.Move(temporaryPath, cachePath, overwrite: true);
            return new ImageCacheOutcome(cachePath, ImageCacheResult.Cached);
        }
        catch (OperationCanceledException)
        {
            // Cancellation says nothing about the image, so it must not count against its retries.
            return new ImageCacheOutcome(null, ImageCacheResult.Offline);
        }
        catch (Exception ex)
        {
            // Artwork is decorative - a failure must never propagate into playback or a list load.
            _logger.LogDebug(ex, "Image cache download failed. ImagePath={ImagePath}", remoteUri.AbsolutePath);
            return new ImageCacheOutcome(null, ImageCacheResult.Failed);
        }
        finally
        {
            DeleteFileIfPresent(temporaryPath);
            downloadLock.Release();
        }
    }

    public Task<long> GetCacheUsageBytesAsync(CancellationToken cancellationToken = default)
        => Task.Run(GetCacheDirectorySizeBytes, cancellationToken);

    public Task PruneAsync(
        IReadOnlyCollection<CachedImageReference> retainedImages,
        CancellationToken cancellationToken = default)
        => Task.Run(
            () =>
            {
                try
                {
                    if (!Directory.Exists(_cacheDirectory))
                    {
                        return;
                    }

                    var retainedFileNames = BuildRetainedFileNames(retainedImages);

                    var removedCount = 0;
                    foreach (var file in Directory.EnumerateFiles(_cacheDirectory))
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        // Never touch .tmp files: a download in flight right now would be deleted
                        // mid-write, and because the backfill only attempts each image once per
                        // session the cover would then be missing until the app restarts. Orphaned
                        // ones are handled by the stale sweep, which checks their age first.
                        if (file.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        if (retainedFileNames.Contains(Path.GetFileName(file)))
                        {
                            continue;
                        }

                        DeleteFileIfPresent(file);
                        removedCount++;
                    }

                    if (removedCount > 0)
                    {
                        _logger.LogInformation(
                            "Pruned {RemovedCount} cached images no longer referenced by the catalog", removedCount);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to prune the image cache");
                }
            },
            cancellationToken);

    /// <summary>
    /// The set of cache filenames this prune must keep.
    ///
    /// <para>
    /// Two entries are conditional, and both exist so that a copy the app is still relying on is
    /// never deleted before its replacement is actually on disk - the cache has no eviction, so a
    /// premature delete means a missing cover until the next successful download:
    /// </para>
    /// <list type="bullet">
    /// <item>the pre-versioning copy of an image the server now reports a version for, and</item>
    /// <item>a full-size master that a rendition has superseded.</item>
    /// </list>
    /// <para>
    /// Each stops being retained the moment its replacement exists, which is what keeps them from
    /// accumulating against the budget.
    /// </para>
    /// </summary>
    private HashSet<string> BuildRetainedFileNames(IReadOnlyCollection<CachedImageReference> retainedImages)
    {
        var retained = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var image in retainedImages)
        {
            if (!StableRemoteAssetKey.TryGetAbsoluteUri(image.Url, out var uri))
            {
                continue;
            }

            var currentPath = GetCachePath(uri, image.Version);

            if (!string.IsNullOrWhiteSpace(image.SupersededBy)
                && StableRemoteAssetKey.TryGetAbsoluteUri(image.SupersededBy, out var supersedingUri)
                && HasCachedFile(GetCachePath(supersedingUri, image.SupersededByVersion)))
            {
                // The replacement is here, so this copy is dead weight.
                continue;
            }

            retained.Add(Path.GetFileName(currentPath));

            if (image.Version > 0 && !HasCachedFile(currentPath))
            {
                retained.Add(Path.GetFileName(GetCachePath(uri, 0)));
            }
        }

        return retained;
    }

    /// <summary>
    /// Filename is the path hash alone, with no song id: one persona image is shared by every song
    /// from that creator, and prefixing the id would store a separate copy per song.
    ///
    /// <para>
    /// The content version participates in the hash rather than sitting beside it, so a superseded
    /// copy is simply a file the prune no longer recognises and cleans up on the next catalog load.
    /// </para>
    /// </summary>
    private string GetCachePath(Uri remoteUri, int contentVersion) => Path.Combine(
        _cacheDirectory,
        StableRemoteAssetKey.GetPathHash(remoteUri, remoteUri.AbsoluteUri, contentVersion)
            + StableRemoteAssetKey.GetExtension(remoteUri, DefaultImageExtension));

    /// <summary>
    /// Whether a usable image is cached at this path.
    ///
    /// <para>
    /// The signature check opens the file, which is far from free when a library refresh resolves
    /// five paths per song, so its verdict is remembered against the file's size and write time. A
    /// file the cache itself wrote does not change afterwards, so the check runs once per file per
    /// process and every later lookup costs a single stat.
    /// </para>
    /// </summary>
    private bool HasCachedFile(string cachePath)
    {
        try
        {
            var info = new FileInfo(cachePath);
            if (!info.Exists)
            {
                _validatedFiles.TryRemove(cachePath, out _);
                return false;
            }

            var stamp = new ValidatedFile(info.Length, info.LastWriteTimeUtc.Ticks);
            if (_validatedFiles.TryGetValue(cachePath, out var validated) && validated == stamp)
            {
                return true;
            }

            if (info.Length >= MinPlausibleImageBytes && LooksLikeImage(cachePath))
            {
                _validatedFiles[cachePath] = stamp;
                return true;
            }

            // A truncated write, or an error document cached by an older build - drop it so the next
            // call re-downloads rather than serving a broken image forever.
            DeleteFileIfPresent(cachePath);
            _validatedFiles.TryRemove(cachePath, out _);
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Identity of a file whose signature has already been checked.</summary>
    private readonly record struct ValidatedFile(long Length, long LastWriteUtcTicks);

    /// <summary>
    /// Whether a file starts with the signature of an image container we expect.
    ///
    /// <para>
    /// This replaces a byte-count floor. The floor existed to reject an Azure "AuthenticationFailed"
    /// XML body from an expired SAS - a few hundred bytes of text that would otherwise be cached as
    /// artwork and pin a broken image forever - but it also rejected small WebP renditions, which are
    /// legitimately tiny. Checking the container signature catches the error document without any
    /// assumption about size.
    /// </para>
    /// </summary>
    internal static bool LooksLikeImage(string path)
    {
        try
        {
            Span<byte> header = stackalloc byte[12];
            using var stream = File.OpenRead(path);

            var read = 0;
            while (read < header.Length)
            {
                var bytes = stream.Read(header[read..]);
                if (bytes == 0)
                {
                    break;
                }

                read += bytes;
            }

            if (read < 12)
            {
                // Too short for a RIFF header. Still allow the shorter signatures.
                return read >= 8 && (IsPng(header) || IsJpeg(header) || IsGif(header));
            }

            return IsWebp(header) || IsPng(header) || IsJpeg(header) || IsGif(header);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Unreadable is not the same as invalid; leave the entry alone rather than deleting it.
            return true;
        }
    }

    // "RIFF" .... "WEBP"
    private static bool IsWebp(ReadOnlySpan<byte> header) =>
        header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46
        && header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50;

    private static bool IsPng(ReadOnlySpan<byte> header) =>
        header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47;

    private static bool IsJpeg(ReadOnlySpan<byte> header) =>
        header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF;

    private static bool IsGif(ReadOnlySpan<byte> header) =>
        header[0] == 0x47 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x38;

    private bool CanQueueDownload(Uri remoteUri, long? contentLengthBytes, ImageCachePriority priority)
    {
        if (_offlineCacheSettingsService == null)
        {
            return true;
        }

        var cacheSizeBytes = GetCacheDirectorySizeBytes();
        var cacheLimitBytes = _offlineCacheSettingsService.GetImageCacheLimitBytes();
        var projectedCacheSizeBytes = contentLengthBytes is > 0
            ? cacheSizeBytes + contentLengthBytes.Value
            : cacheSizeBytes;

        // Heroes stop well before the ceiling so the remaining budget always belongs to thumbs, which
        // are what the list rows, the mini player and the lock screen fall back to. Without this the
        // large tier could fill the cache and leave every small surface blank.
        if (priority == ImageCachePriority.Hero)
        {
            var heroBudgetBytes = (long)(cacheLimitBytes * HeroBudgetFraction);
            if (cacheSizeBytes >= heroBudgetBytes || projectedCacheSizeBytes > heroBudgetBytes)
            {
                _logger.LogDebug(
                    "Hero image download skipped so the remaining budget stays available for thumbnails. ImagePath={ImagePath}; CacheSizeBytes={CacheSizeBytes}; HeroBudgetBytes={HeroBudgetBytes}",
                    remoteUri.AbsolutePath,
                    cacheSizeBytes,
                    heroBudgetBytes);
                return false;
            }
        }

        if (cacheSizeBytes >= cacheLimitBytes || projectedCacheSizeBytes > cacheLimitBytes)
        {
            // Warning, not Debug: a skip means artwork silently stops caching, and both log providers
            // filter Debug out. The equivalent Android audio warning is the only reason the cache-limit
            // jam was ever diagnosed.
            _logger.LogWarning(
                "Image cache download skipped because the image cache budget is reached. ImagePath={ImagePath}; CacheSizeBytes={CacheSizeBytes}; CacheLimitBytes={CacheLimitBytes}",
                remoteUri.AbsolutePath,
                cacheSizeBytes,
                cacheLimitBytes);
            return false;
        }

        var availableStorageBytes = GetAvailableCacheStorageBytes();
        var reserveBytes = _offlineCacheSettingsService.GetDeviceFreeSpaceReserveBytes();
        var projectedAvailableStorageBytes = contentLengthBytes is > 0
            ? availableStorageBytes - contentLengthBytes.Value
            : availableStorageBytes;

        if (projectedAvailableStorageBytes <= reserveBytes)
        {
            _logger.LogWarning(
                "Image cache download skipped because the device free-space reserve is reached. ImagePath={ImagePath}; AvailableStorageBytes={AvailableStorageBytes}; ReserveBytes={ReserveBytes}",
                remoteUri.AbsolutePath,
                availableStorageBytes,
                reserveBytes);
            return false;
        }

        return true;
    }

    private long GetCacheDirectorySizeBytes()
    {
        try
        {
            if (!Directory.Exists(_cacheDirectory))
            {
                return 0;
            }

            return new DirectoryInfo(_cacheDirectory).EnumerateFiles().Sum(file => file.Length);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Unable to measure image cache directory size");
            return 0;
        }
    }

    private long GetAvailableCacheStorageBytes()
    {
        try
        {
            Directory.CreateDirectory(_cacheDirectory);
            return CacheStorageProbe.GetAvailableFreeSpaceBytes(_cacheDirectory);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Unable to measure available image cache storage");
            return long.MaxValue;
        }
    }

    /// <summary>
    /// Removes orphaned .tmp files left by a process kill mid-download. They are invisible to the cache
    /// but still count against the budget.
    /// </summary>
    private void SweepStaleTemporaryFiles()
    {
        try
        {
            if (!Directory.Exists(_cacheDirectory))
            {
                return;
            }

            var cutoffUtc = DateTime.UtcNow.AddHours(-1);
            foreach (var file in Directory.EnumerateFiles(_cacheDirectory, "*.tmp"))
            {
                if (File.GetLastWriteTimeUtc(file) < cutoffUtc)
                {
                    DeleteFileIfPresent(file);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to sweep stale image cache temporary files");
        }
    }

    private static void DeleteFileIfPresent(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Leftover files are harmless; the next write overwrites them.
        }
    }
}
