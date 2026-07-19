using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.ViewModels;

public partial class ConfigViewModel : ObservableObject
{
    private readonly IOfflineCacheSettingsService _offlineCacheSettingsService;
    private readonly IAudioCacheService _audioCacheService;
    private double _offlineCacheLimitMb;
    private long _cacheUsageBytes;
    private bool _isCacheUsageLoading;

    public ConfigViewModel(
        IOfflineCacheSettingsService offlineCacheSettingsService,
        IAudioCacheService audioCacheService)
    {
        _offlineCacheSettingsService = offlineCacheSettingsService;
        _audioCacheService = audioCacheService;
        _offlineCacheLimitMb = offlineCacheSettingsService.GetOfflineCacheLimitMb();
    }

    public double MinimumCacheLimitMb => _offlineCacheSettingsService.MinimumCacheLimitMb;

    public double MaximumCacheLimitMb => _offlineCacheSettingsService.MaximumCacheLimitMb;

    public double OfflineCacheLimitMb
    {
        get => _offlineCacheLimitMb;
        set
        {
            var normalizedValue = _offlineCacheSettingsService.NormalizeCacheLimitMb((int)Math.Round(value));
            if (!SetProperty(ref _offlineCacheLimitMb, normalizedValue))
            {
                return;
            }

            _offlineCacheSettingsService.SetOfflineCacheLimitMb(normalizedValue);
            OnPropertyChanged(nameof(OfflineCacheLimitDisplay));
        }
    }

    public string OfflineCacheLimitDisplay => FormatMegabytes((int)Math.Round(OfflineCacheLimitMb));

    public string MinimumCacheLimitDisplay => FormatMegabytes(_offlineCacheSettingsService.MinimumCacheLimitMb);

    public string MaximumCacheLimitDisplay => FormatMegabytes(_offlineCacheSettingsService.MaximumCacheLimitMb);

    public string DeviceFreeSpaceReserveDisplay => FormatMegabytes(_offlineCacheSettingsService.DeviceFreeSpaceReserveMb);

    [RelayCommand]
    private void ResetOfflineCacheLimit()
    {
        OfflineCacheLimitMb = _offlineCacheSettingsService.DefaultCacheLimitMb;
    }

    public bool IsCacheUsageLoading
    {
        get => _isCacheUsageLoading;
        private set
        {
            if (!SetProperty(ref _isCacheUsageLoading, value))
            {
                return;
            }

            OnPropertyChanged(nameof(CacheUsageDisplay));
        }
    }

    public string CacheUsageDisplay => IsCacheUsageLoading
        ? "Calculating…"
        : FormatBytes(_cacheUsageBytes);

    public void Refresh()
    {
        OfflineCacheLimitMb = _offlineCacheSettingsService.GetOfflineCacheLimitMb();
    }

    public async Task RefreshCacheUsageAsync(CancellationToken cancellationToken = default)
    {
        IsCacheUsageLoading = true;
        try
        {
            _cacheUsageBytes = await _audioCacheService
                .GetCacheUsageBytesAsync(cancellationToken)
                .ConfigureAwait(true);
        }
        finally
        {
            IsCacheUsageLoading = false;
        }
    }

    private static string FormatMegabytes(int megabytes)
    {
        if (megabytes >= 1024)
        {
            var gigabytes = megabytes / 1024d;
            return $"{gigabytes:0.#} GB";
        }

        return $"{megabytes} MB";
    }

    private static string FormatBytes(long bytes)
    {
        const double bytesPerMb = 1024d * 1024d;
        var megabytes = bytes / bytesPerMb;

        if (megabytes >= 1024)
        {
            return $"{megabytes / 1024d:0.#} GB";
        }

        if (megabytes >= 1)
        {
            return $"{megabytes:0.#} MB";
        }

        var kilobytes = bytes / 1024d;
        return $"{kilobytes:0.#} KB";
    }
}
