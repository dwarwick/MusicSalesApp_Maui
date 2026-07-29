namespace MusicSalesApp.Maui.Services;

public interface IOfflineCacheSettingsService
{
    int MinimumCacheLimitMb { get; }

    int MaximumCacheLimitMb { get; }

    int DefaultCacheLimitMb { get; }

    int DeviceFreeSpaceReserveMb { get; }

    int GetOfflineCacheLimitMb();

    /// <summary>
    /// The total on-disk budget the user configured. Audio and artwork are both spent out of it, so
    /// this is the figure the settings screen compares reported usage against - not a per-cache limit.
    /// Use <see cref="GetAudioCacheLimitBytes"/> or <see cref="GetImageCacheLimitBytes"/> to bound an
    /// individual cache.
    /// </summary>
    long GetOfflineCacheLimitBytes();

    long GetDeviceFreeSpaceReserveBytes();

    /// <summary>
    /// Budget for cached artwork, carved out of <see cref="GetOfflineCacheLimitBytes"/> rather than
    /// added on top of it. Derived - there is no separate preference.
    /// </summary>
    long GetImageCacheLimitBytes();

    /// <summary>
    /// Budget for cached audio: the configured limit less the artwork carve-out. The two together can
    /// therefore never exceed what the user asked for, which matters because reported cache usage sums
    /// both - without the subtraction the settings screen could show usage above its own limit.
    /// </summary>
    long GetAudioCacheLimitBytes();

    void SetOfflineCacheLimitMb(int limitMb);

    int NormalizeCacheLimitMb(int limitMb);
}

public sealed class OfflineCacheSettingsService : IOfflineCacheSettingsService
{
    private const int MinimumCacheLimitMegabytes = 100;
    private const int MaximumCacheLimitMegabytes = 5 * 1024;
    private const int DefaultCacheLimitMegabytes = 1024;
    private const int DeviceFreeSpaceReserveMegabytes = 1024;
    private const long BytesPerMegabyte = 1024L * 1024L;

    // Artwork is tiny next to audio, so a twentieth of the budget covers a large catalogue while
    // leaving the audio cache essentially untouched.
    private const int ImageCacheBudgetDivisor = 20;
    private const int MinimumImageCacheLimitMegabytes = 8;
    private const int MaximumImageCacheLimitMegabytes = 64;

    private readonly IAppPreferenceStore _preferenceStore;

    public OfflineCacheSettingsService(IAppPreferenceStore preferenceStore)
    {
        _preferenceStore = preferenceStore;
    }

    /// <summary>
    /// The preference key and default, exposed so a platform component that cannot reach DI - the
    /// Android Media3 cache provider builds its evictor from a static context - reads the same value
    /// this service does.
    /// </summary>
    public static string CacheLimitPreferenceKey => MobilePreferenceKeys.OfflineCacheLimitMb;

    public static int DefaultCacheLimitMegabytesValue => DefaultCacheLimitMegabytes;

    /// <summary>
    /// The audio budget for a given configured limit, as a pure function so the instance API and the
    /// Android cache evictor cannot drift apart. Sizing the evictor off a different number than
    /// <see cref="GetAudioCacheLimitBytes"/> would let the cache sit permanently just above the
    /// download ceiling - which is exactly the state that silently stops all downloading.
    /// </summary>
    public static long ComputeAudioCacheLimitBytes(int configuredLimitMb)
    {
        var limitBytes = ClampCacheLimitMb(configuredLimitMb) * BytesPerMegabyte;
        return limitBytes - ComputeImageCacheLimitBytes(configuredLimitMb);
    }

    public static long ComputeImageCacheLimitBytes(int configuredLimitMb) => Math.Clamp(
        ClampCacheLimitMb(configuredLimitMb) * BytesPerMegabyte / ImageCacheBudgetDivisor,
        MinimumImageCacheLimitMegabytes * BytesPerMegabyte,
        MaximumImageCacheLimitMegabytes * BytesPerMegabyte);

    private static int ClampCacheLimitMb(int limitMb)
        => Math.Clamp(limitMb, MinimumCacheLimitMegabytes, MaximumCacheLimitMegabytes);

    public int MinimumCacheLimitMb => MinimumCacheLimitMegabytes;

    public int MaximumCacheLimitMb => MaximumCacheLimitMegabytes;

    public int DefaultCacheLimitMb => DefaultCacheLimitMegabytes;

    public int DeviceFreeSpaceReserveMb => DeviceFreeSpaceReserveMegabytes;

    public int GetOfflineCacheLimitMb()
    {
        var configuredLimit = _preferenceStore.GetInt(
            MobilePreferenceKeys.OfflineCacheLimitMb,
            DefaultCacheLimitMegabytes);
        return NormalizeCacheLimitMb(configuredLimit);
    }

    public long GetOfflineCacheLimitBytes() => GetOfflineCacheLimitMb() * BytesPerMegabyte;

    public long GetDeviceFreeSpaceReserveBytes() => DeviceFreeSpaceReserveMegabytes * BytesPerMegabyte;

    public long GetImageCacheLimitBytes() => ComputeImageCacheLimitBytes(GetOfflineCacheLimitMb());

    public long GetAudioCacheLimitBytes() => ComputeAudioCacheLimitBytes(GetOfflineCacheLimitMb());

    public void SetOfflineCacheLimitMb(int limitMb)
        => _preferenceStore.SetInt(
            MobilePreferenceKeys.OfflineCacheLimitMb,
            NormalizeCacheLimitMb(limitMb));

    public int NormalizeCacheLimitMb(int limitMb) => ClampCacheLimitMb(limitMb);
}
