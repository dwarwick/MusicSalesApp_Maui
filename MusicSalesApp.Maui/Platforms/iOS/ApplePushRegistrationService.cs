using Firebase.CloudMessaging;
using Foundation;
using Microsoft.Extensions.Logging;
using MusicSalesApp.Maui.Services;
using UIKit;
using UserNotifications;

namespace MusicSalesApp.Maui.Platforms.iOS;

/// <summary>
/// iOS push registration: Apple issues the device token, Firebase turns it into the FCM token.
/// </summary>
/// <remarks>
/// <para>
/// FCM on iOS is a <b>relay</b>, not a replacement. The app still asks for authorization and still
/// calls <c>RegisterForRemoteNotifications</c> to get an APNs device token from Apple - but that
/// token is handed to Firebase rather than to the server, and what the server stores is the FCM
/// registration token Firebase gives back. Sending the raw APNs token instead produces rows the
/// dispatcher can never deliver to, indistinguishable from uninstalled devices.
/// </para>
/// <para>
/// Firebase is configured <b>lazily</b>, on first use, rather than in the AppDelegate at launch.
/// This head carries documented App Store launch-crash workarounds (<c>MtouchRegistrar=static</c>,
/// LLVM AOT) and a perceived-ANR budget, and initialising a large native SDK on the startup path is
/// what reopens both. Nothing touches Firebase until push is actually being set up.
/// </para>
/// <para>
/// Neither token arrives from a method that returns it - APNs answers on the AppDelegate, Firebase
/// on a delegate callback - so <see cref="ApplePushTokenBroker"/> bridges both back to here.
/// </para>
/// </remarks>
public sealed class ApplePushRegistrationService : IPushRegistrationService
{
    private readonly ILogger<ApplePushRegistrationService> _logger;
    private readonly FirebaseTokenListener _tokenListener;
    private readonly SemaphoreSlim _configureGate = new(1, 1);
    private bool _firebaseConfigured;

    public ApplePushRegistrationService(ILogger<ApplePushRegistrationService> logger)
    {
        _logger = logger;
        _tokenListener = new FirebaseTokenListener();
        ApplePushTokenBroker.TokenRefreshed += OnBrokerTokenRefreshed;

        // The APNs token can land before anything asks for a token - iOS re-delivers it on every
        // launch once the user has granted permission - so the broker holds it and replays it here
        // the moment Firebase is ready to receive it.
        ApplePushTokenBroker.ApnsTokenReceived += OnApnsTokenReceived;
    }

    /// <inheritdoc />
    public bool IsSupported => true;

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

            await EnsureFirebaseConfiguredAsync();

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

        if (!await EnsureFirebaseConfiguredAsync())
        {
            return null;
        }

        // Registration is idempotent and cheap - it returns the cached APNs token when nothing has
        // changed - and both answers arrive on callbacks, so a null here is normal on a cold start
        // and the coordinator picks the token up from the refresh event instead.
        await MainThread.InvokeOnMainThreadAsync(
            () => UIApplication.SharedApplication.RegisterForRemoteNotifications());

        var fcmToken = Messaging.SharedInstance?.FcmToken;

        if (!string.IsNullOrWhiteSpace(fcmToken))
        {
            ApplePushTokenBroker.SetFcmToken(fcmToken);
            return fcmToken;
        }

        return ApplePushTokenBroker.CurrentToken;
    }

    /// <inheritdoc />
    public event EventHandler<string>? TokenRefreshed;

    /// <summary>
    /// Starts Firebase once, and points its messaging delegate at the broker.
    /// </summary>
    /// <remarks>
    /// <c>App.Configure()</c> throws if it runs twice, and it reads GoogleService-Info.plist from
    /// the bundle - which is gitignored, so a clone without it gets a failure here rather than a
    /// crash. Returning false in that case leaves iOS out of the device table entirely, which is
    /// the same honest state the platform reported before Firebase existed.
    /// </remarks>
    private async Task<bool> EnsureFirebaseConfiguredAsync()
    {
        if (_firebaseConfigured)
        {
            return true;
        }

        await _configureGate.WaitAsync();

        try
        {
            if (_firebaseConfigured)
            {
                return true;
            }

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (Firebase.Core.App.DefaultInstance is null)
                {
                    Firebase.Core.App.Configure();
                }

                Messaging.SharedInstance.Delegate = _tokenListener;
            });

            _firebaseConfigured = true;

            // Replays an APNs token that arrived before Firebase was up.
            ApplyApnsTokenToFirebase(ApplePushTokenBroker.ApnsToken);

            return true;
        }
        catch (Exception ex)
        {
            // Almost always a missing or wrong GoogleService-Info.plist. Warning rather than throw:
            // push failing must never stop the app starting or the user playing music.
            _logger.LogWarning(ex, "Firebase could not be configured, so iOS push is unavailable.");
            return false;
        }
        finally
        {
            _configureGate.Release();
        }
    }

    private void OnApnsTokenReceived(object? sender, NSData token)
    {
        if (_firebaseConfigured)
        {
            ApplyApnsTokenToFirebase(token);
        }
    }

    private void ApplyApnsTokenToFirebase(NSData? token)
    {
        if (token is null)
        {
            return;
        }

        try
        {
            // This is the hand-off the whole iOS path turns on. Firebase mints the FCM token from
            // it and answers on the delegate below; until it is set, FcmToken stays null.
            Messaging.SharedInstance.ApnsToken = token;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Handing the APNs token to Firebase failed.");
        }
    }

    private void OnBrokerTokenRefreshed(object? sender, string token) => TokenRefreshed?.Invoke(this, token);

    /// <summary>
    /// Firebase's side of the exchange: the FCM registration token, and every later rotation.
    /// </summary>
    private sealed class FirebaseTokenListener : NSObject, IMessagingDelegate
    {
        // The selector is exported explicitly because this protocol member is optional: without it
        // Firebase finds nothing to call and the token silently never arrives.
        [Export("messaging:didReceiveRegistrationToken:")]
        public void DidReceiveRegistrationToken(Messaging messaging, string? fcmToken)
            => ApplePushTokenBroker.SetFcmToken(fcmToken);
    }
}

/// <summary>
/// Bridges the two asynchronous token callbacks - Apple's and Firebase's - to the registration
/// service.
/// </summary>
/// <remarks>
/// Static because the APNs callback lands on the AppDelegate, which iOS owns and which has no
/// access to the DI container at the moment it fires.
/// </remarks>
public static class ApplePushTokenBroker
{
    /// <summary>
    /// The FCM registration token for this install, or null. This - not the APNs token - is what
    /// the server stores and sends to.
    /// </summary>
    public static string? CurrentToken { get; private set; }

    /// <summary>The raw APNs device token, held so it can be replayed once Firebase is up.</summary>
    public static NSData? ApnsToken { get; private set; }

    /// <summary>Raised with the FCM token, on first issue and on every rotation.</summary>
    public static event EventHandler<string>? TokenRefreshed;

    /// <summary>Raised when Apple issues a device token, for hand-off to Firebase.</summary>
    public static event EventHandler<NSData>? ApnsTokenReceived;

    /// <summary>
    /// Called from <c>AppDelegate.RegisteredForRemoteNotifications</c>.
    /// </summary>
    /// <remarks>
    /// The bytes are kept as <see cref="NSData"/> rather than converted to hex, because Firebase
    /// wants the raw token. Hex is what a server talking to APNs directly would need, and this app
    /// does not: converting here is how the wrong token ends up on the wire.
    /// </remarks>
    public static void SetApnsToken(NSData deviceToken)
    {
        if (deviceToken is null)
        {
            return;
        }

        ApnsToken = deviceToken;
        ApnsTokenReceived?.Invoke(null, deviceToken);
    }

    /// <summary>Called from Firebase's messaging delegate.</summary>
    public static void SetFcmToken(string? fcmToken)
    {
        if (string.IsNullOrWhiteSpace(fcmToken) || string.Equals(fcmToken, CurrentToken, StringComparison.Ordinal))
        {
            return;
        }

        CurrentToken = fcmToken;
        TokenRefreshed?.Invoke(null, fcmToken);
    }

    /// <summary>
    /// Called when iOS refuses to register - no entitlement, no network, or a simulator without a
    /// paired push capability.
    /// </summary>
    public static void Clear()
    {
        ApnsToken = null;
        CurrentToken = null;
    }
}
