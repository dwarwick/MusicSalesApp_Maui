using Microsoft.Extensions.Logging;
using MusicSalesApp.Common.Helpers;

namespace MusicSalesApp.Maui.Services;

/// <summary>
/// Keeps the server's idea of this device in step with reality.
/// </summary>
/// <remarks>
/// <para>
/// Modelled on <see cref="AdminMessageCoordinator"/>, and for the same reason: the work is
/// triggered by several unrelated events - signing in, signing out, the app coming to the
/// foreground, the platform rotating the token - and every one of them has to converge on one
/// guarded operation rather than racing the others.
/// </para>
/// <para>
/// Everything here is platform-neutral on purpose. The MAUI test project compiles
/// <c>Services/*.cs</c> by glob, so this class is covered by tests while the native
/// implementations behind <see cref="IPushRegistrationService"/> are not.
/// </para>
/// </remarks>
public class PushNotificationCoordinator : IPushNotificationCoordinator, IDisposable
{
    private readonly IAuthService _authService;
    private readonly IPushRegistrationService _registrationService;
    private readonly IPushApiService _pushApiService;
    private readonly IAppPreferenceStore _preferenceStore;
    private readonly ILogger<PushNotificationCoordinator> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private bool _disposed;

    public PushNotificationCoordinator(
        IAuthService authService,
        IPushRegistrationService registrationService,
        IPushApiService pushApiService,
        IAppPreferenceStore preferenceStore,
        ILogger<PushNotificationCoordinator> logger)
    {
        _authService = authService;
        _registrationService = registrationService;
        _pushApiService = pushApiService;
        _preferenceStore = preferenceStore;
        _logger = logger;

        _authService.AuthStateChanged += OnAuthStateChanged;
        _registrationService.TokenRefreshed += OnTokenRefreshed;
    }

    /// <inheritdoc />
    public async Task SyncAsync()
    {
        if (!_registrationService.IsSupported)
        {
            return;
        }

        // Never block on the gate. Sync is called from app activation, auth changes and token
        // refreshes, which can arrive together; if one is already running it will pick up whatever
        // the others would have seen, because it re-reads the state rather than being handed it.
        if (!await _gate.WaitAsync(0))
        {
            return;
        }

        try
        {
            if (!_authService.IsLoggedIn)
            {
                await UnregisterCurrentDeviceAsync();
                return;
            }

            // Permission is never requested here. This runs on activation and on sign-in, and a
            // system prompt appearing unexplained at either moment is how people deny it - and a
            // denial is close to permanent, since neither platform shows the prompt twice.
            var permission = await _registrationService.GetPermissionStatusAsync();

            if (permission != PushPermissionStatus.Granted)
            {
                return;
            }

            await RegisterCurrentDeviceAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Push synchronisation failed.");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<PushPermissionStatus> RequestPermissionAndRegisterAsync()
    {
        if (!_registrationService.IsSupported)
        {
            return PushPermissionStatus.Unsupported;
        }

        var status = await _registrationService.RequestPermissionAsync();

        if (status == PushPermissionStatus.Granted)
        {
            await SyncAsync();
        }

        return status;
    }

    private async Task RegisterCurrentDeviceAsync()
    {
        var token = await _registrationService.GetTokenAsync();

        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        var outcome = await _pushApiService.RegisterDeviceAsync(CurrentPlatform, token, DeviceId);

        switch (outcome)
        {
            case PushRegistrationOutcome.Registered:
                // Remembered so sign-out can unregister the exact token that was registered. The
                // platform may well hand back a different one by then, and unregistering the wrong
                // one leaves the old registration live - which is how a signed-out phone keeps
                // receiving notifications.
                _preferenceStore.SetString(MobilePreferenceKeys.RegisteredPushToken, token);
                break;

            case PushRegistrationOutcome.Rejected:
                // Permanent. Forget it rather than retrying every activation forever.
                _preferenceStore.Remove(MobilePreferenceKeys.RegisteredPushToken);
                _logger.LogWarning("The server rejected this device's push token.");
                break;

            case PushRegistrationOutcome.Deferred:
                // Leave whatever was stored alone; the next activation tries again.
                break;
        }
    }

    private async Task UnregisterCurrentDeviceAsync()
    {
        var token = _preferenceStore.GetString(MobilePreferenceKeys.RegisteredPushToken);

        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        // Cleared first. A failed unregister must not leave the app trying forever, and the server
        // reassigns a token to whoever registers it next anyway - so the stale row is corrected the
        // moment anyone signs in on this device.
        _preferenceStore.Remove(MobilePreferenceKeys.RegisteredPushToken);
        await _pushApiService.UnregisterDeviceAsync(token);
    }

    /// <summary>
    /// The platform name the server files this token under. It decides which transport delivers to
    /// it, so it comes from the shared constants rather than a literal.
    /// </summary>
    private static string CurrentPlatform =>
#if ANDROID
        PushPlatforms.Android;
#elif IOS
        PushPlatforms.Ios;
#else
        "";
#endif

    /// <summary>
    /// A stable id for this install, so a rotated token replaces its predecessor server-side
    /// instead of leaving a dead row behind.
    /// </summary>
    /// <remarks>
    /// Generated and stored on first use rather than read from the device, which keeps it free of
    /// any hardware identifier - it identifies an installation, not a person or a handset.
    /// </remarks>
    private string DeviceId
    {
        get
        {
            var existing = _preferenceStore.GetString(MobilePreferenceKeys.PushDeviceId);

            if (!string.IsNullOrWhiteSpace(existing))
            {
                return existing;
            }

            var generated = Guid.NewGuid().ToString("N");
            _preferenceStore.SetString(MobilePreferenceKeys.PushDeviceId, generated);
            return generated;
        }
    }

    private void OnAuthStateChanged() => _ = SyncAsync();

    private void OnTokenRefreshed(object? sender, string token) => _ = SyncAsync();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _authService.AuthStateChanged -= OnAuthStateChanged;
        _registrationService.TokenRefreshed -= OnTokenRefreshed;
        _gate.Dispose();
    }
}
