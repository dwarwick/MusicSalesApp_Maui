using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.ViewModels;

public partial class ConfigViewModel : ObservableObject
{
    private readonly IOfflineCacheSettingsService _offlineCacheSettingsService;
    private double _offlineCacheLimitMb;

    public ConfigViewModel(IOfflineCacheSettingsService offlineCacheSettingsService)
    {
        _offlineCacheSettingsService = offlineCacheSettingsService;
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

    public void Refresh()
    {
        OfflineCacheLimitMb = _offlineCacheSettingsService.GetOfflineCacheLimitMb();
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
}
