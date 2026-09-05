using Foundation;
using Microsoft.Extensions.Logging;
using MusicSalesApp.Maui.Services;
using UIKit;
using UserNotifications;

namespace MusicSalesApp.Maui.Platforms.iOS;

/// <summary>
/// iOS push registration, straight to APNs.
/// </summary>
/// <remarks>
/// <para>
/// No Firebase here on purpose. The server talks to APNs directly, so the iOS head needs nothing
/// beyond Apple's own frameworks - which matters because this head already carries documented App
/// Store launch-crash workarounds (<c>MtouchRegistrar=static</c>, LLVM AOT), and adding a large
/// native SDK is exactly the kind of change that reopens them.
/// </para>
/// <para>
/// The device token arrives asynchronously on the AppDelegate, not from a method that returns it,
/// so <see cref="ApplePushTokenBroker"/> bridges the callback back to whoever is waiting.
/// </para>
/// </remarks>
public sealed class ApplePushRegistrationService : IPushRegistrationService
{
    private readonly ILogger<ApplePushRegistrationService> _logger;

    public ApplePushRegistrationService(ILogger<ApplePushRegistrationService> logger)
    {
        _logger = logger;
        ApplePushTokenBroker.TokenRefreshed += OnBrokerTokenRefreshed;
    }

    /// <summary>
    /// <b>False until the Firebase iOS SDK is wired in.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// Everything below is correct and still needed - FCM on iOS is a RELAY, so the app must still
    /// ask for authorization and still call RegisterForRemoteNotifications to get an APNs device
    /// token. What is missing is the last hop: Firebase exchanges that APNs token for an FCM
    /// registration token, and the FCM token is what the server must store.
    /// </para>
    /// <para>
    /// Reporting true today would register the raw APNs token instead, which FCM rejects on every
    /// send - filling PushDeviceTokens with rows that can never be delivered to and look, from the
    /// dispatcher's side, exactly like uninstalled devices. Returning false keeps iOS out of the
    /// table entirely until the exchange exists, which is the honest state.
    /// </para>
    /// <para>
    /// To finish: add the Firebase iOS Cloud Messaging binding, call
    /// <c>Messaging.SharedInstance.ApnsToken = deviceToken</c> from the AppDelegate callback, and
    /// return <c>Messaging.SharedInstance.FcmToken</c> from <see cref="GetTokenAsync"/>.
    /// </para>
    /// </remarks>
    public bool IsSupported => false;

    /// <inheritdoc />
    public async Task<PushPermissionStatus> GetPermissionStatusAsync()
    {
        var settings = await UNUserNotificationCenter.Current.GetNotificationSettingsAsync();

        return settings.AuthorizationStatus switch
        {
            UNAuthorizationStatus.Authorized => PushPermissionStatus.Granted,
            UNAuthorizationStatus.Provisional => PushPermissionStatus.Granted,
            UNAuthorizationStatus.Ephemeral => PushPermissionStatus.Granted,
            UNAuthorizationStatus.NotDetermined => PushPermissionStatus.NotDetermined,
            _ => PushPermissionStatus.Denied,
        };
    }

    /// <inheritdoc />
    public async Task<PushPermissionStatus> RequestPermissionAsync()
    {
        try
        {
            var (granted, error) = await UNUserNotificationCenter.Current.RequestAuthorizationAsync(
                UNAuthorizationOptions.Alert | UNAuthorizationOptions.Badge | UNAuthorizationOptions.Sound);

            if (error is not null)
            {
                _logger.LogWarning("Requesting notification authorization failed: {Error}", error.LocalizedDescription);
            }

            if (!granted)
            {
                return PushPermissionStatus.Denied;
            }

            // Authorization and REGISTRATION are separate steps on iOS, and this is the one people
            // forget: without it the user has said yes and no token is ever issued, so the app
            // looks permitted and receives nothing. It must run on the main thread.
            await MainThread.InvokeOnMainThreadAsync(
                () => UIApplication.SharedApplication.RegisterForRemoteNotifications());

            return PushPermissionStatus.Granted;
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
        if (await GetPermissionStatusAsync() != PushPermissionStatus.Granted)
        {
            return null;
        }

        // A token already delivered to the AppDelegate this launch.
        if (!string.IsNullOrWhiteSpace(ApplePushTokenBroker.CurrentToken))
        {
            return ApplePushTokenBroker.CurrentToken;
        }

        // Otherwise ask iOS to deliver one. Registration is idempotent and cheap - it returns the
        // cached token when nothing has changed - and the answer arrives on the AppDelegate, so
        // this returns null and the coordinator picks the token up from the refresh event.
        await MainThread.InvokeOnMainThreadAsync(
            () => UIApplication.SharedApplication.RegisterForRemoteNotifications());

        return ApplePushTokenBroker.CurrentToken;
    }

    /// <inheritdoc />
    public event EventHandler<string>? TokenRefreshed;

    private void OnBrokerTokenRefreshed(object? sender, string token) => TokenRefreshed?.Invoke(this, token);
}

/// <summary>
/// Bridges the AppDelegate's device-token callback to the registration service.
/// </summary>
/// <remarks>
/// Static because the callback lands on the AppDelegate, which iOS owns and which has no access to
/// the DI container at the moment it fires.
/// </remarks>
public static class ApplePushTokenBroker
{
    /// <summary>The most recent device token this launch, as lowercase hex, or null.</summary>
    public static string? CurrentToken { get; private set; }

    public static event EventHandler<string>? TokenRefreshed;

    /// <summary>
    /// Called from <c>AppDelegate.RegisteredForRemoteNotifications</c>.
    /// </summary>
    /// <remarks>
    /// APNs hands over raw bytes, and the provider API wants lowercase hex. Getting this conversion
    /// wrong produces a token APNs rejects as BadDeviceToken - which reads like a configuration
    /// problem rather than a formatting one.
    /// </remarks>
    public static void SetToken(NSData deviceToken)
    {
        if (deviceToken is null)
        {
            return;
        }

        var bytes = deviceToken.ToArray();
        var hex = Convert.ToHexString(bytes).ToLowerInvariant();

        if (string.Equals(hex, CurrentToken, StringComparison.Ordinal))
        {
            return;
        }

        CurrentToken = hex;
        TokenRefreshed?.Invoke(null, hex);
    }

    /// <summary>
    /// Called when iOS refuses to register - no entitlement, no network, or a simulator without a
    /// paired push capability.
    /// </summary>
    public static void Clear() => CurrentToken = null;
}
