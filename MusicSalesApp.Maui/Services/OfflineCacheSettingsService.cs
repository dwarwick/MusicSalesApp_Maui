namespace MusicSalesApp.Maui.Services;

public interface IOfflineCacheSettingsService
{
    int MinimumCacheLimitMb { get; }

    int MaximumCacheLimitMb { get; }

    int DefaultCacheLimitMb { get; }

    int DeviceFreeSpaceReserveMb { get; }

    int GetOfflineCacheLimitMb();

    long GetOfflineCacheLimitBytes();

    long GetDeviceFreeSpaceReserveBytes();

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

    private readonly IAppPreferenceStore _preferenceStore;

    public OfflineCacheSettingsService(IAppPreferenceStore preferenceStore)
    {
        _preferenceStore = preferenceStore;
    }

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

    public void SetOfflineCacheLimitMb(int limitMb)
        => _preferenceStore.SetInt(
            MobilePreferenceKeys.OfflineCacheLimitMb,
            NormalizeCacheLimitMb(limitMb));

    public int NormalizeCacheLimitMb(int limitMb)
        => Math.Clamp(limitMb, MinimumCacheLimitMegabytes, MaximumCacheLimitMegabytes);
}
