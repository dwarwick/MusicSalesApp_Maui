using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Gms.Extensions;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using Firebase;
using Firebase.Messaging;
using Microsoft.Extensions.Logging;
using MusicSalesApp.Common.Helpers;
using MusicSalesApp.Maui.Services;
using Application = Android.App.Application;
// MusicSalesApp.Common.Helpers.Permissions (the server's authorization policy names) collides with
// MAUI's permission API, and both are in scope here. Aliased rather than dropping the Common using,
// which is what supplies PushNotificationChannels.
using MauiPermissions = Microsoft.Maui.ApplicationModel.Permissions;

namespace MusicSalesApp.Maui.Platforms.Android;

/// <summary>
/// Android push registration, over Firebase Cloud Messaging.
/// </summary>
/// <remarks>
/// Lives under <c>Platforms/</c> so it is excluded from the test project's <c>Services/*.cs</c>
/// glob - everything here touches native APIs that cannot load on a plain net10.0 host. The
/// decisions worth testing live in <see cref="PushNotificationCoordinator"/> instead.
/// </remarks>
public sealed class AndroidPushRegistrationService : IPushRegistrationService
{
    private readonly ILogger<AndroidPushRegistrationService> _logger;
    private bool _channelCreated;

    public AndroidPushRegistrationService(ILogger<AndroidPushRegistrationService> logger)
    {
        _logger = logger;
        AndroidPushTokenBroker.TokenRefreshed += OnBrokerTokenRefreshed;
    }

    /// <summary>
    /// True only when Firebase actually initialised, which needs a google-services.json to have
    /// been compiled in. Without one the app runs normally and simply has no push.
    /// </summary>
    public bool IsSupported
    {
        get
        {
            try
            {
                return FirebaseApp.InitializeApp(Application.Context) is not null;
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex, "Firebase is not configured; Android push is unavailable.");
                return false;
            }
        }
    }

    /// <inheritdoc />
    public Task<PushPermissionStatus> GetPermissionStatusAsync()
    {
        if (!IsSupported)
        {
            return Task.FromResult(PushPermissionStatus.Unsupported);
        }

        // Below Android 13 there is no runtime notification permission at all - notifications are
        // allowed unless the user turned the app's notifications off, which
        // NotificationManagerCompat reports.
        if (!OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            var enabled = NotificationManagerCompat.From(Application.Context)?.AreNotificationsEnabled() == true;

            return Task.FromResult(enabled ? PushPermissionStatus.Granted : PushPermissionStatus.Denied);
        }

        var granted = ContextCompat.CheckSelfPermission(
            Application.Context, global::Android.Manifest.Permission.PostNotifications)
            == (int)Permission.Granted;

        if (granted)
        {
            return Task.FromResult(PushPermissionStatus.Granted);
        }

        // MAUI's Permissions API distinguishes "never asked" from "asked and refused" through
        // ShouldShowRationale, which is what stops the app prompting someone who already said no.
        var shouldExplain = MauiPermissions.ShouldShowRationale<MauiPermissions.PostNotifications>();

        return Task.FromResult(shouldExplain ? PushPermissionStatus.Denied : PushPermissionStatus.NotDetermined);
    }

    /// <inheritdoc />
    public async Task<PushPermissionStatus> RequestPermissionAsync()
    {
        if (!IsSupported)
        {
            return PushPermissionStatus.Unsupported;
        }

        EnsureChannel();

        if (!OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            return await GetPermissionStatusAsync();
        }

        try
        {
            var status = await MauiPermissions.RequestAsync<MauiPermissions.PostNotifications>();

            return status == PermissionStatus.Granted
                ? PushPermissionStatus.Granted
                : PushPermissionStatus.Denied;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Requesting the notification permission failed.");
            return PushPermissionStatus.Denied;
        }
    }

    /// <inheritdoc />
    public async Task<string?> GetTokenAsync()
    {
        if (!IsSupported)
        {
            return null;
        }

        EnsureChannel();

        try
        {
            // GetToken returns a Google Play Services Task, not a System.Threading.Task, so it
            // cannot be awaited directly - AsAsync from Android.Gms.Extensions bridges the two.
            var token = await FirebaseMessaging.Instance.GetToken().AsAsync<Java.Lang.Object>();
            return token?.ToString();
        }
        catch (Exception ex)
        {
            // Play Services missing or out of date, or the device offline. Null means "no token
            // right now"; the coordinator tries again on the next activation.
            _logger.LogWarning(ex, "Could not obtain an FCM token.");
            return null;
        }
    }

    /// <inheritdoc />
    public event EventHandler<string>? TokenRefreshed;

    private void OnBrokerTokenRefreshed(object? sender, string token) => TokenRefreshed?.Invoke(this, token);

    /// <summary>
    /// Creates the notification channel the server names in every payload.
    /// </summary>
    /// <remarks>
    /// From Android 8 a notification whose channel does not exist is dropped by the system with no
    /// error and nothing on screen, which is indistinguishable from push not working at all. The
    /// channel id comes from the shared constants so the two ends cannot drift.
    /// </remarks>
    private void EnsureChannel()
    {
        if (_channelCreated || !OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            return;
        }

        try
        {
            var manager = Application.Context.GetSystemService(Context.NotificationService) as NotificationManager;

            var channel = new NotificationChannel(
                PushNotificationChannels.ArtistUpdates,
                PushNotificationChannels.ArtistUpdatesName,
                NotificationImportance.Default)
            {
                Description = PushNotificationChannels.ArtistUpdatesDescription,
            };

            manager?.CreateNotificationChannel(channel);
            _channelCreated = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not create the artist updates notification channel.");
        }
    }
}
