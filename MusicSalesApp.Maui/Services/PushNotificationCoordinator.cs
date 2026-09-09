using System.Globalization;
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

    // Trailing and optional, so the existing tests construct this without it. Null simply means
    // the account preferences are left alone.
    private readonly INotificationPreferenceApiService? _notificationPreferences;

    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Set when a sync arrived while one was already running, so the running one loops once more
    /// rather than the trigger being lost. Read and written with Volatile because the setter and
    /// the reader are different threads.
    /// </summary>
    private int _syncRequestedAgain;

    private bool _disposed;

    public PushNotificationCoordinator(
        IAuthService authService,
        IPushRegistrationService registrationService,
        IPushApiService pushApiService,
        IAppPreferenceStore preferenceStore,
        ILogger<PushNotificationCoordinator> logger,
        INotificationPreferenceApiService? notificationPreferences = null)
    {
        _authService = authService;
        _registrationService = registrationService;
        _pushApiService = pushApiService;
        _preferenceStore = preferenceStore;
        _logger = logger;
        _notificationPreferences = notificationPreferences;

        _authService.AuthStateChanged += OnAuthStateChanged;
        _authService.SigningOut += OnSigningOutAsync;
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
        // refreshes, which can arrive together, and a queue of them would just repeat the same work.
        //
        // But a dropped call cannot simply be discarded. The running one read IsLoggedIn once, at
        // the top, BEFORE the event that was dropped happened - so a sign-out arriving mid-sync used
        // to vanish, and the in-flight run went on to register the device for the account that had
        // just signed out. Record the drop instead and re-run once, which collapses any number of
        // triggers into exactly one extra pass.
        if (!await _gate.WaitAsync(0))
        {
            Volatile.Write(ref _syncRequestedAgain, 1);
            return;
        }

        try
        {
            do
            {
                Volatile.Write(ref _syncRequestedAgain, 0);
                await SyncOnceAsync();
            }
            while (Volatile.Read(ref _syncRequestedAgain) == 1);
        }
        finally
        {
            _gate.Release();
        }

        // One last look, AFTER releasing. A trigger that arrived between the loop's final read and
        // the release set the flag with nobody left inside to see it, and would then have waited
        // for some unrelated future sync to notice - which for a sign-out is exactly the wait that
        // must not happen. Re-entering is safe: this pass clears the flag before doing any work, so
        // two of these cannot chase each other.
        if (Volatile.Read(ref _syncRequestedAgain) == 1)
        {
            await SyncAsync();
        }
    }

    /// <summary>
    /// One pass of the sync, with every piece of state read fresh.
    /// </summary>
    private async Task SyncOnceAsync()
    {
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
            // Saying yes on the device says yes to the feature. The OS permission and the account
            // preferences are different things - one is "may this phone show notifications", the
            // other is "what do you want to be told about" - but a listener who has just been
            // asked, in an app that explained what it is for, has answered both. Leaving the
            // account switches off would register the phone and then never send it anything, which
            // reads as broken rather than as a second setting they have not found yet.
            //
            // Only ever turns them ON. Turning push off is still done per channel, on the web
            // account page, and this must not undo that on the next app launch.
            await EnableAccountPushPreferencesAsync();
            await SyncAsync();
        }

        return status;
    }

    /// <summary>
    /// Switches the account-level push preferences on, if they are not already.
    /// </summary>
    /// <remarks>
    /// Read first so the whole record can be written back: the endpoint replaces every preference,
    /// so sending only the two push flags would switch the listener's email choices off.
    /// </remarks>
    private async Task EnableAccountPushPreferencesAsync()
    {
        if (_notificationPreferences is null)
        {
            return;
        }

        try
        {
            var preferences = await _notificationPreferences.GetAsync();

            if (preferences is null)
            {
                return;
            }

            if (preferences.ReceiveArtistReleasePush && preferences.ReceiveArtistMessagePush)
            {
                return;
            }

            preferences.ReceiveArtistReleasePush = true;
            preferences.ReceiveArtistMessagePush = true;

            if (!await _notificationPreferences.SetAsync(preferences))
            {
                _logger.LogWarning(
                    "Notification permission was granted, but the account push preferences could not be switched on.");
            }
        }
        catch (Exception ex)
        {
            // Registration has already succeeded by this point. Failing to flip the preferences is
            // worth a line in the log, not worth reporting a denied permission to the caller.
            _logger.LogWarning(ex, "Could not switch on the account push preferences.");
        }
    }

    /// <inheritdoc />
    public Task<PushPermissionStatus> GetPermissionStatusAsync() =>
        _registrationService.IsSupported
            ? _registrationService.GetPermissionStatusAsync()
            : Task.FromResult(PushPermissionStatus.Unsupported);

    /// <summary>
    /// How long a successful registration is trusted before it is refreshed anyway.
    /// </summary>
    /// <remarks>
    /// The upper bound on how long a device stays dark if the server loses its row - a reinstall of
    /// the database, a pruning job, a bug. Registration is idempotent, so refreshing costs one PUT.
    /// </remarks>
    private static readonly TimeSpan RegistrationRefreshInterval = TimeSpan.FromDays(1);

    private async Task RegisterCurrentDeviceAsync()
    {
        var token = await _registrationService.GetTokenAsync();

        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        if (IsRegistrationStillGood(token))
        {
            return;
        }

        var outcome = await _pushApiService.RegisterDeviceAsync(CurrentPlatform, token, DeviceId);

        switch (outcome)
        {
            case PushRegistrationOutcome.Registered:
                // Logged in full, and deliberately: this is the only way to get the token off the
                // device to send a test message with from the Firebase console, and it is what
                // separates "Apple and Firebase are configured" from "the server never sent". It is
                // a device address rather than a credential, and the file log lives inside the app
                // container.
                _logger.LogInformation(
                    "Registered this device for push. Platform={Platform}, Token={Token}",
                    CurrentPlatform,
                    token);

                // Remembered so sign-out can unregister the exact token that was registered. The
                // platform may well hand back a different one by then, and unregistering the wrong
                // one leaves the old registration live - which is how a signed-out phone keeps
                // receiving notifications.
                _preferenceStore.SetString(MobilePreferenceKeys.RegisteredPushToken, token);
                _preferenceStore.SetString(
                    MobilePreferenceKeys.PushTokenRegisteredAtUtcTicks,
                    DateTimeOffset.UtcNow.UtcTicks.ToString(CultureInfo.InvariantCulture));
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

    /// <summary>
    /// Whether the server already has this exact token, recently enough to be trusted.
    /// </summary>
    /// <remarks>
    /// Sync runs on every app activation, so without this a user who opens the app thirty times a
    /// day pays thirty registrations for a token that changes perhaps once a year - and on iOS each
    /// one puts RegisterForRemoteNotifications on the main thread, which is exactly the kind of
    /// resume-path work the ANR sweep exists to keep out.
    /// </remarks>
    private bool IsRegistrationStillGood(string token)
    {
        if (_preferenceStore.GetString(MobilePreferenceKeys.RegisteredPushToken) != token)
        {
            return false;
        }

        var stamp = _preferenceStore.GetString(MobilePreferenceKeys.PushTokenRegisteredAtUtcTicks);

        // No stamp means it was registered by a build that did not write one. Re-register once and
        // it gains a stamp; treating it as good would leave those installs never refreshing.
        if (!long.TryParse(stamp, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks))
        {
            return false;
        }

        var registeredAt = new DateTimeOffset(ticks, TimeSpan.Zero);
        var age = DateTimeOffset.UtcNow - registeredAt;

        // A negative age means the clock moved backwards; re-register rather than trust it.
        return age >= TimeSpan.Zero && age < RegistrationRefreshInterval;
    }

    /// <summary>
    /// Retires this device's registration while the session can still authenticate the call.
    /// </summary>
    /// <remarks>
    /// Hooked to <see cref="IAuthService.SigningOut"/> rather than <c>AuthStateChanged</c>. The
    /// latter is raised after the token has been cleared, so the DELETE went out unauthenticated,
    /// came back 401, and left the handset registered - still receiving the previous user's
    /// notifications - with the local token already erased so nothing could ever retry.
    /// </remarks>
    private async Task UnregisterCurrentDeviceAsync()
    {
        var token = _preferenceStore.GetString(MobilePreferenceKeys.RegisteredPushToken);

        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        var unregistered = await _pushApiService.UnregisterDeviceAsync(token);

        // Forgotten either way. A failed unregister must not leave the app trying forever, and the
        // server reassigns a token to whoever registers it next - so the stale row is corrected the
        // moment anyone signs in on this device. Cleared after the call, not before, so the call
        // itself still has the token to send.
        _preferenceStore.Remove(MobilePreferenceKeys.RegisteredPushToken);
        _preferenceStore.Remove(MobilePreferenceKeys.PushTokenRegisteredAtUtcTicks);

        if (!unregistered)
        {
            _logger.LogWarning(
                "The server did not confirm this device was unregistered; it may keep receiving notifications until someone signs in here.");
        }
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

    /// <summary>
    /// Runs before the session is torn down, so the DELETE goes out with a valid bearer token.
    /// </summary>
    /// <remarks>
    /// Takes the same gate a sync does. Without it the unregister could interleave with a
    /// registration already in flight, which would re-write RegisteredPushToken and re-create the
    /// server row moments after the DELETE removed it - leaving the handset registered to the
    /// account that just signed out, which is the failure this hook exists to prevent.
    ///
    /// <para>
    /// Bounded, because the caller is a sign-out the user asked for: if a sync is genuinely stuck,
    /// unregistering unguarded is a better outcome than making them wait for it.
    /// </para>
    /// </remarks>
    private async Task OnSigningOutAsync()
    {
        var held = await _gate.WaitAsync(TimeSpan.FromSeconds(2));

        try
        {
            await UnregisterCurrentDeviceAsync();
        }
        finally
        {
            if (held)
            {
                _gate.Release();
            }
        }
    }

    private void OnTokenRefreshed(object? sender, string token) => _ = SyncAsync();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _authService.AuthStateChanged -= OnAuthStateChanged;
        _authService.SigningOut -= OnSigningOutAsync;
        _registrationService.TokenRefreshed -= OnTokenRefreshed;
        _gate.Dispose();
    }
}
