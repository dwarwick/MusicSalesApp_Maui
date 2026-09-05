using Android.App;
using Android.Content;
using Android.OS;

namespace MusicSalesApp.Maui.Platforms.Android;

/// <summary>
/// The notification channels push notifications are posted to, and their creation.
/// </summary>
/// <remarks>
/// A channel has to exist before <c>Notify</c> is called or Android 8+ drops the notification
/// silently - nothing is shown, nothing is logged, and the result is indistinguishable from a push
/// that never arrived. Creation is idempotent, so this is safe to call on every message.
/// Mirrors <c>AndroidMedia3CacheProvider.EnsureNotificationChannels</c>, which does the same job
/// for the playback channel.
/// </remarks>
internal static class PushNotificationChannels
{
    public const string ArtistUpdates = "streamtunes_artist_updates";

    public static void EnsureCreated(Context context)
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O)
        {
            return;
        }

        if (context.GetSystemService(Context.NotificationService) is not NotificationManager notificationManager)
        {
            return;
        }

        notificationManager.CreateNotificationChannel(new NotificationChannel(
            ArtistUpdates,
            "Artist updates",
            NotificationImportance.Default));
    }
}
