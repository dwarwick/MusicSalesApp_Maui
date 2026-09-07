using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Gms.Extensions;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using Firebase;
using Firebase.Messaging;
using Microsoft.Extensions.Logging;
using MusicSalesApp.Maui.Services;
using Application = Android.App.Application;
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

        AndroidNotificationChannels.EnsureCreated(Application.Context);

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

        AndroidNotificationChannels.EnsureCreated(Application.Context);

        try
        {
            // GetToken returns a Google Play Services Task, not a System.Threading.Task, so it
            // cannot be awaited directly - AsAsync from Android.Gms.Extensions bridges the two.
            //
            // The binding marks GetToken [Obsolete("deprecated")] because the Java method carries
            // @Deprecated in firebase-messaging 25.x, but getToken/deleteToken are the only token
            // members it exposes - there is nothing to migrate to. Suppressed here rather than
            // project-wide, so the day a replacement ships, removing this surfaces it. Same
            // treatment as OnNewToken in StreamTunesFirebaseMessagingService.
#pragma warning disable CS0618
            var token = await FirebaseMessaging.Instance.GetToken().AsAsync<Java.Lang.Object>();
#pragma warning restore CS0618
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
}
