using Android.App;
using Android.Content;
using Android.OS;
using SharedChannels = MusicSalesApp.Common.Helpers.PushNotificationChannels;

namespace MusicSalesApp.Maui.Platforms.Android;

/// <summary>
/// Creates the Android notification channels push notifications are posted to.
/// </summary>
/// <remarks>
/// <para>
/// A channel has to exist before <c>Notify</c> is called or Android 8+ drops the notification
/// silently - nothing is shown, nothing is logged, and the result is indistinguishable from a push
/// that never arrived. Creation is idempotent, so this is safe to call on every message.
/// Mirrors <c>AndroidMedia3CacheProvider.EnsureNotificationChannels</c>, which does the same job
/// for the playback channel.
/// </para>
/// <para>
/// <b>The id and labels come from <c>MusicSalesApp.Common</c>, not from literals here.</b> The
/// server stamps the same id into every FCM payload, and a channel id that exists on one side but
/// not the other is precisely the silent-drop case above. Both repos reference Common, so the two
/// ends cannot drift.
/// </para>
/// <para>
/// Named <c>AndroidNotificationChannels</c> rather than <c>PushNotificationChannels</c> because the
/// shared type already has that name and both are in scope in this namespace - the collision is a
/// compile error, and aliasing at every call site would be worse than naming this for what it is.
/// </para>
/// </remarks>
internal static class AndroidNotificationChannels
{
    /// <summary>The shared channel id, re-exposed so callers in this namespace read one name.</summary>
    public const string ArtistUpdates = SharedChannels.ArtistUpdates;

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
            SharedChannels.ArtistUpdates,
            SharedChannels.ArtistUpdatesName,
            NotificationImportance.Default)
        {
            Description = SharedChannels.ArtistUpdatesDescription,
        });
    }
}
