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
    private readonly IPushNotificationCoordinator? _pushNotifications;

    public ConfigViewModel(
        IOfflineCacheSettingsService offlineCacheSettingsService,
        IAudioCacheService audioCacheService,
        INotificationPreferenceApiService? notificationPreferences = null,
        IPushNotificationCoordinator? pushNotifications = null)
    {
        _offlineCacheSettingsService = offlineCacheSettingsService;
        _audioCacheService = audioCacheService;
        _notificationPreferences = notificationPreferences;
        _pushNotifications = pushNotifications;
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

    // ---- Notifications --------------------------------------------------------------------------
    //
    // Every user-facing push preference lives here, and only here. They used to be split between
    // this app and the website, which meant someone could allow notifications on their phone, have
    // the account switches still off on the web, and receive nothing - with no way to tell those
    // two states apart from the device. The OS permission and the account preferences are still
    // different things, but they are now asked for in one place, in the order they depend on each
    // other: permission first, then what to be told about, then how often.

    private NotificationPreferences? _preferences;
    private ArtistPushFrequency _notificationFrequency;
    private bool _allowPushNotifications;
    private bool _receiveReleasePush;
    private bool _receiveMessagePush;
    private bool _isNotificationSectionAvailable;
    private bool _isPushBlockedBySystem;
    private bool _isSavingNotifications;

    // Guards the writes the toggles trigger. Switching the master on sets both categories, and
    // each category setter would otherwise fire its own save - three round trips for one tap, in a
    // racing order.
    private bool _suppressNotificationWrites;

    public IReadOnlyList<ArtistPushFrequency> NotificationFrequencies { get; } =
        Enum.GetValues<ArtistPushFrequency>();

    /// <summary>False on a platform with no push transport, or before the preferences load.</summary>
    public bool IsNotificationSectionAvailable
    {
        get => _isNotificationSectionAvailable;
        private set => SetProperty(ref _isNotificationSectionAvailable, value);
    }

    /// <summary>
    /// The user refused at the OS level. Neither platform will ask again, so the toggles are shown
    /// disabled with an explanation rather than hidden - hiding them reads as the feature being
    /// missing rather than as something they turned off.
    /// </summary>
    public bool IsPushBlockedBySystem
    {
        get => _isPushBlockedBySystem;
        private set
        {
            if (SetProperty(ref _isPushBlockedBySystem, value))
            {
                OnPropertyChanged(nameof(CanEditNotifications));
                OnPropertyChanged(nameof(NotificationBlockedMessage));
            }
        }
    }

    public bool IsSavingNotifications
    {
        get => _isSavingNotifications;
        private set
        {
            if (SetProperty(ref _isSavingNotifications, value))
            {
                OnPropertyChanged(nameof(CanEditNotifications));
                OnPropertyChanged(nameof(CanEditNotificationCategories));
            }
        }
    }

    public bool CanEditNotifications => !IsPushBlockedBySystem && !IsSavingNotifications;

    /// <summary>The per-kind toggles only mean anything while push is allowed at all.</summary>
    public bool CanEditNotificationCategories => CanEditNotifications && AllowPushNotifications;

    public string NotificationBlockedMessage => IsPushBlockedBySystem
        ? "Notifications are turned off for StreamTunes on this device. Turn them back on in your device settings."
        : string.Empty;

    public string NotificationStatus { get; private set; } = string.Empty;

    /// <summary>
    /// The master switch. On means the OS permission is granted AND at least one kind is wanted,
    /// which is exactly the condition under which a notification can actually arrive - so it can
    /// never claim to be on while the user would receive nothing.
    /// </summary>
    public bool AllowPushNotifications
    {
        get => _allowPushNotifications;
        set
        {
            if (!SetProperty(ref _allowPushNotifications, value))
            {
                return;
            }

            OnPropertyChanged(nameof(CanEditNotificationCategories));

            if (!_suppressNotificationWrites)
            {
                _ = ApplyMasterToggleAsync(value);
            }
        }
    }

    public bool ReceiveReleasePush
    {
        get => _receiveReleasePush;
        set
        {
            if (SetProperty(ref _receiveReleasePush, value) && !_suppressNotificationWrites)
            {
                _ = SaveNotificationsAsync();
            }
        }
    }

    public bool ReceiveMessagePush
    {
        get => _receiveMessagePush;
        set
        {
            if (SetProperty(ref _receiveMessagePush, value) && !_suppressNotificationWrites)
            {
                _ = SaveNotificationsAsync();
            }
        }
    }

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

            if (!_suppressNotificationWrites)
            {
                _ = SaveNotificationsAsync();
            }
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
            IsNotificationSectionAvailable = false;
            return;
        }

        var permission = _pushNotifications is null
            ? PushPermissionStatus.Unsupported
            : await _pushNotifications.GetPermissionStatusAsync().ConfigureAwait(true);

        if (permission == PushPermissionStatus.Unsupported)
        {
            IsNotificationSectionAvailable = false;
            return;
        }

        _preferences = await _notificationPreferences.GetAsync(cancellationToken).ConfigureAwait(true);

        if (_preferences is null)
        {
            // Signed out, or the server is unreachable. Toggles that cannot save are worse than
            // no toggles.
            IsNotificationSectionAvailable = false;
            return;
        }

        IsPushBlockedBySystem = permission == PushPermissionStatus.Denied;
        ApplyPreferences(_preferences, permission);
        IsNotificationSectionAvailable = true;
    }

    private void ApplyPreferences(NotificationPreferences preferences, PushPermissionStatus permission)
    {
        // Assigned with writes suppressed: this is displaying what the server already has, and
        // going through the setters would post it straight back on every page open.
        _suppressNotificationWrites = true;

        try
        {
            ReceiveReleasePush = preferences.ReceiveArtistReleasePush;
            ReceiveMessagePush = preferences.ReceiveArtistMessagePush;
            NotificationFrequency = preferences.ArtistPushFrequency;
            AllowPushNotifications =
                permission == PushPermissionStatus.Granted &&
                (preferences.ReceiveArtistReleasePush || preferences.ReceiveArtistMessagePush);
        }
        finally
        {
            _suppressNotificationWrites = false;
        }
    }

    private async Task ApplyMasterToggleAsync(bool allow)
    {
        if (_pushNotifications is null || _notificationPreferences is null)
        {
            return;
        }

        IsSavingNotifications = true;
        SetStatus(string.Empty);

        try
        {
            if (allow)
            {
                // Asks the OS if it has not been asked, and switches the account preferences on -
                // both halves of "allow", which is why this goes through the coordinator rather
                // than writing preferences directly.
                var status = await _pushNotifications.RequestPermissionAndRegisterAsync().ConfigureAwait(true);

                if (status != PushPermissionStatus.Granted)
                {
                    IsPushBlockedBySystem = status == PushPermissionStatus.Denied;
                    RevertMaster(false);
                    SetStatus(status == PushPermissionStatus.Denied
                        ? "Notifications are turned off for StreamTunes in your device settings."
                        : "Notifications could not be turned on.");
                    return;
                }

                IsPushBlockedBySystem = false;

                // Re-read rather than assume: the coordinator has just written both categories on,
                // and this is what the server actually stored.
                var refreshed = await _notificationPreferences.GetAsync().ConfigureAwait(true);

                if (refreshed is not null)
                {
                    _preferences = refreshed;
                    ApplyPreferences(refreshed, PushPermissionStatus.Granted);
                }
                else
                {
                    SetCategoriesQuietly(true, true);
                }

                SetStatus("Saved.");
                return;
            }

            // Off means "send me nothing", which is the two category switches - the OS permission
            // cannot be revoked from here, and asking the user to do it in Settings to turn one
            // feature off would be absurd. The device stays registered, so turning this back on
            // does not need another prompt.
            SetCategoriesQuietly(false, false);
            await SaveNotificationsAsync().ConfigureAwait(true);
        }
        finally
        {
            IsSavingNotifications = false;
        }
    }

    private void SetCategoriesQuietly(bool release, bool message)
    {
        _suppressNotificationWrites = true;

        try
        {
            ReceiveReleasePush = release;
            ReceiveMessagePush = message;
        }
        finally
        {
            _suppressNotificationWrites = false;
        }
    }

    private void RevertMaster(bool value)
    {
        _suppressNotificationWrites = true;

        try
        {
            AllowPushNotifications = value;
        }
        finally
        {
            _suppressNotificationWrites = false;
        }
    }

    private async Task SaveNotificationsAsync()
    {
        if (_notificationPreferences is null || _preferences is null)
        {
            return;
        }

        IsSavingNotifications = true;
        SetStatus(string.Empty);

        try
        {
            // The whole record goes back, because the endpoint replaces all of it - sending only
            // what changed would switch the listener's email preferences off.
            _preferences.ReceiveArtistReleasePush = ReceiveReleasePush;
            _preferences.ReceiveArtistMessagePush = ReceiveMessagePush;
            _preferences.ArtistPushFrequency = NotificationFrequency;

            if (!await _notificationPreferences.SetAsync(_preferences).ConfigureAwait(true))
            {
                SetStatus("Could not save that just now.");
                return;
            }

            // Read back rather than trusting the 200. A server older than this build ignores a
            // property it does not know, answers OK, and drops the choice - so without this the app
            // says "Saved" and the setting is back to its old value next time the page opens.
            var confirmed = await _notificationPreferences.GetAsync().ConfigureAwait(true);

            if (confirmed is null)
            {
                SetStatus("Saved.");
                return;
            }

            var frequencyIgnored = confirmed.ArtistPushFrequency != NotificationFrequency;

            _preferences = confirmed;
            ApplyPreferences(confirmed, IsPushBlockedBySystem
                ? PushPermissionStatus.Denied
                : PushPermissionStatus.Granted);

            SetStatus(frequencyIgnored
                ? "This server does not support notification frequency yet."
                : "Saved.");
        }
        finally
        {
            IsSavingNotifications = false;
        }
    }

    private void SetStatus(string status)
    {
        NotificationStatus = status;
        OnPropertyChanged(nameof(NotificationStatus));
    }
}
