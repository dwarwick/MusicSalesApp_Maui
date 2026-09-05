using Android.App;
using Android.Content;
using Firebase.Messaging;
using MusicSalesApp.Common.Helpers;
using AndroidX.Core.App;
using Application = Android.App.Application;

namespace MusicSalesApp.Maui.Platforms.Android;

/// <summary>
/// Receives FCM messages and token rotations.
/// </summary>
/// <remarks>
/// <para>
/// Android constructs this itself, outside the DI container and often with no app in the
/// foreground, so it cannot take dependencies. Anything it needs to tell the rest of the app goes
/// through <see cref="AndroidPushTokenBroker"/>.
/// </para>
/// <para>
/// The intent filter is what makes it discoverable at all; without <c>MESSAGING_EVENT</c>, FCM
/// delivers nothing and there is no error to notice. <c>Exported=false</c> because only the system
/// should be able to start it.
/// </para>
/// </remarks>
[Service(Exported = false)]
[IntentFilter(["com.google.firebase.MESSAGING_EVENT"])]
public sealed class StreamTunesFirebaseMessagingService : FirebaseMessagingService
{
    public override void OnNewToken(string token)
    {
        base.OnNewToken(token);

        // Fires when FCM rotates the token - a restore to a new phone, a reinstall, or the service
        // simply deciding to. Without acting on it the server keeps a token it will be told is
        // dead, and the user silently stops receiving anything.
        AndroidPushTokenBroker.RaiseTokenRefreshed(token);
    }

    public override void OnMessageReceived(RemoteMessage message)
    {
        base.OnMessageReceived(message);

        // The server sends a notification block as well as data, so Android displays the alert
        // itself whenever the app is backgrounded and this method is never called. It IS called
        // when the app is in the foreground, where the system shows nothing - so posting it here
        // is what stops a notification vanishing purely because the user had the app open.
        var title = message.GetNotification()?.Title;
        var body = message.GetNotification()?.Body;

        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(body))
        {
            return;
        }

        try
        {
            ShowNotification(title ?? string.Empty, body ?? string.Empty, message.Data);
        }
        catch (Exception)
        {
            // A failure to display must never take the process down - this runs on a system-managed
            // service thread where an escaping exception is a crash the user sees as the app dying
            // in the background.
        }
    }

    private static void ShowNotification(string title, string body, IDictionary<string, string>? data)
    {
        var context = Application.Context;
        var packageName = context.PackageName;

        if (string.IsNullOrEmpty(packageName))
        {
            return;
        }

        var launchIntent = context.PackageManager?.GetLaunchIntentForPackage(packageName);

        if (launchIntent is not null && data is not null)
        {
            // Carried through so the app can route the tap once it is open. Read back in
            // MainActivity via PushDataKeys.
            foreach (var pair in data)
            {
                launchIntent.PutExtra(pair.Key, pair.Value);
            }

            launchIntent.AddFlags(ActivityFlags.SingleTop);
        }

        var pendingIntent = PendingIntent.GetActivity(
            context,
            0,
            launchIntent,
            // Immutable is required from Android 12, and UpdateCurrent so a second notification
            // replaces the first one's extras rather than reusing stale ones.
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        // Written as statements rather than a fluent chain: every NotificationCompat.Builder
        // setter is bound as returning a nullable Builder, so chaining them produces a
        // dereference warning per call for no benefit.
        var builder = new NotificationCompat.Builder(context, PushNotificationChannels.ArtistUpdates);
        builder.SetContentTitle(title);
        builder.SetContentText(body);
        builder.SetStyle(new NotificationCompat.BigTextStyle().BigText(body));
        builder.SetSmallIcon(global::Android.Resource.Drawable.IcDialogInfo);
        builder.SetAutoCancel(true);
        builder.SetContentIntent(pendingIntent);

        // A per-message id, so several notifications stack rather than each replacing the last.
        var notificationId = (int)(DateTime.UtcNow.Ticks % int.MaxValue);

        NotificationManagerCompat.From(context)?.Notify(notificationId, builder.Build());
    }
}

/// <summary>
/// The one-way channel from Android's messaging service back into the app.
/// </summary>
/// <remarks>
/// Static because <see cref="StreamTunesFirebaseMessagingService"/> is constructed by the platform
/// with no access to the DI container, and can run when no activity exists. Handlers are held
/// weakly by convention - the registration service subscribes for the life of the app, so there is
/// nothing to leak.
/// </remarks>
public static class AndroidPushTokenBroker
{
    public static event EventHandler<string>? TokenRefreshed;

    public static void RaiseTokenRefreshed(string token)
    {
        if (!string.IsNullOrWhiteSpace(token))
        {
            TokenRefreshed?.Invoke(null, token);
        }
    }
}
