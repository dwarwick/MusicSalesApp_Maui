using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicSalesApp.Common.Helpers;
using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.ViewModels;

public partial class ConfigViewModel : ObservableObject
{
    private readonly IOfflineCacheSettingsService _offlineCacheSettingsService;
    private readonly IAudioCacheService _audioCacheService;
    private double _offlineCacheLimitMb;
    private long _cacheUsageBytes;
    private bool _isCacheUsageLoading;

    // Trailing and optional, so the existing tests that construct this with two services keep
    // working and simply get no notification section.
    private readonly INotificationPreferenceApiService? _notificationPreferences;

    public ConfigViewModel(
        IOfflineCacheSettingsService offlineCacheSettingsService,
        IAudioCacheService audioCacheService,
        INotificationPreferenceApiService? notificationPreferences = null)
    {
        _offlineCacheSettingsService = offlineCacheSettingsService;
        _audioCacheService = audioCacheService;
        _notificationPreferences = notificationPreferences;
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

    // ---- Notification frequency ----------------------------------------------------------------
    //
    // A cap on interruptions, not a delay dial: on a window the server sends ONE push summarising
    // everything that happened in it. It has to be enforced server-side - once FCM has delivered a
    // push the phone cannot retract it - so this is a round trip rather than a local preference,
    // and the section hides entirely when the server cannot be reached or nobody is signed in.

    public IReadOnlyList<ArtistPushFrequency> NotificationFrequencies { get; } =
        Enum.GetValues<ArtistPushFrequency>();

    private NotificationPreferences? _preferences;
    private ArtistPushFrequency _notificationFrequency;
    private bool _isNotificationFrequencyAvailable;
    private bool _isSavingNotificationFrequency;

    /// <summary>False until the preferences have been read, which hides the whole section.</summary>
    public bool IsNotificationFrequencyAvailable
    {
        get => _isNotificationFrequencyAvailable;
        private set => SetProperty(ref _isNotificationFrequencyAvailable, value);
    }

    public bool IsSavingNotificationFrequency
    {
        get => _isSavingNotificationFrequency;
        private set => SetProperty(ref _isSavingNotificationFrequency, value);
    }

    public string NotificationFrequencyStatus { get; private set; } = string.Empty;

    public ArtistPushFrequency NotificationFrequency
    {
        get => _notificationFrequency;
        set
        {
            if (!SetProperty(ref _notificationFrequency, value))
            {
                return;
            }

            OnPropertyChanged(nameof(NotificationFrequencyDescription));

            // Saved on change rather than behind a button, matching the cache slider above it,
            // which also commits as you move it.
            _ = SaveNotificationFrequencyAsync(value);
        }
    }

    public string NotificationFrequencyDescription => NotificationFrequency switch
    {
        ArtistPushFrequency.TwelveHours => "At most one notification every 12 hours, summarising what you missed.",
        ArtistPushFrequency.Daily => "At most one notification a day, summarising what you missed.",
        _ => "Notified as soon as an artist you follow posts something.",
    };

    public async Task LoadNotificationPreferencesAsync(CancellationToken cancellationToken = default)
    {
        if (_notificationPreferences is null)
        {
            IsNotificationFrequencyAvailable = false;
            return;
        }

        _preferences = await _notificationPreferences.GetAsync(cancellationToken).ConfigureAwait(true);

        if (_preferences is null)
        {
            // Signed out, or the server is unreachable. Showing a picker that cannot save would be
            // worse than showing nothing.
            IsNotificationFrequencyAvailable = false;
            return;
        }

        // Set through the field, not the property: going through the setter would post the value
        // straight back to the server on every page open.
        SetProperty(ref _notificationFrequency, _preferences.ArtistPushFrequency, nameof(NotificationFrequency));
        OnPropertyChanged(nameof(NotificationFrequencyDescription));
        IsNotificationFrequencyAvailable = true;
    }

    private async Task SaveNotificationFrequencyAsync(ArtistPushFrequency frequency)
    {
        if (_notificationPreferences is null || _preferences is null)
        {
            return;
        }

        IsSavingNotificationFrequency = true;
        SetStatus(string.Empty);

        try
        {
            // The whole record goes back, because the endpoint replaces all of it - sending only
            // the frequency would switch every other preference off.
            _preferences.ArtistPushFrequency = frequency;

            if (!await _notificationPreferences.SetAsync(_preferences).ConfigureAwait(true))
            {
                SetStatus("Could not save that just now.");
                return;
            }

            // Read back rather than trusting the 200. A server older than this build simply ignores
            // a property it does not know, answers OK, and drops the choice - so without this the
            // app says "Saved" and the setting is back to its old value next time the page opens.
            // A mobile client is always some other version than the server it is talking to, which
            // makes that worth one extra round trip.
            var confirmed = await _notificationPreferences.GetAsync().ConfigureAwait(true);

            if (confirmed is null)
            {
                SetStatus("Saved.");
                return;
            }

            _preferences = confirmed;

            if (confirmed.ArtistPushFrequency != frequency)
            {
                SetProperty(ref _notificationFrequency, confirmed.ArtistPushFrequency, nameof(NotificationFrequency));
                OnPropertyChanged(nameof(NotificationFrequencyDescription));
                SetStatus("This server does not support notification frequency yet.");
                return;
            }

            SetStatus("Saved.");
        }
        finally
        {
            IsSavingNotificationFrequency = false;
        }
    }

    private void SetStatus(string status)
    {
        NotificationFrequencyStatus = status;
        OnPropertyChanged(nameof(NotificationFrequencyStatus));
    }
}
