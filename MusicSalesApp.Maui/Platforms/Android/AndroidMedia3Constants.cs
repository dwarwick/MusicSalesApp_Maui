namespace MusicSalesApp.Maui.Platforms.Android;

internal static class AndroidMedia3Constants
{
    public const string PlaybackServiceAction = "androidx.media3.session.MediaSessionService";
    public const string DownloadRestartAction = "androidx.media3.exoplayer.downloadService.action.RESTART";
    public const string DownloadServiceCategory = "android.intent.category.DEFAULT";
    public const string PlaybackNotificationChannelId = "streamtunes_playback";
    public const string DownloadNotificationChannelId = "streamtunes_downloads";
    public const int DownloadNotificationId = 3201;
    public const int DownloadForegroundNotificationId = 3202;
    public const int DownloadJobId = 3203;
}
