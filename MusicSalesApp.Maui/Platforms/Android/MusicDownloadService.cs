using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

namespace MusicSalesApp.Maui.Platforms.Android;

[Service(
    Exported = false,
    ForegroundServiceType = ForegroundService.TypeDataSync)]
public sealed class MusicDownloadService : Service
{
    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        AndroidMedia3CacheProvider.EnsureNotificationChannels(this);
        StartForeground(AndroidMedia3Constants.DownloadForegroundNotificationId, BuildNotification());
        StopSelf(startId);
        return StartCommandResult.NotSticky;
    }

    private Notification BuildNotification()
    {
        var launchIntent = PackageManager?.GetLaunchIntentForPackage(PackageName ?? string.Empty);
        var pendingIntent = launchIntent == null
            ? null
            : PendingIntent.GetActivity(
                this,
                0,
                launchIntent,
                PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        var builder = Build.VERSION.SdkInt >= BuildVersionCodes.O
            ? new Notification.Builder(this, AndroidMedia3Constants.DownloadNotificationChannelId)
            : new Notification.Builder(this);

        builder.SetSmallIcon(Resource.Mipmap.appicon);
        builder.SetContentTitle("Preparing playback");
        builder.SetContentText("Preparing tracks for playback.");
        builder.SetContentIntent(pendingIntent);
        builder.SetOngoing(false);

        return builder.Build();
    }
}
