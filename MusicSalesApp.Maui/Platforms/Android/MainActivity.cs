using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Content.Res;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Views.InputMethods;

namespace MusicSalesApp.Maui;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
[IntentFilter(
    [Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataScheme = "https",
    DataHost = "streamtunes.net",
    DataPathPrefix = "/song/",
    AutoVerify = true)]
[IntentFilter(
    [Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataScheme = "https",
    DataHost = "streamtunes.net",
    DataPathPrefix = "/share/",
    AutoVerify = true)]
[IntentFilter(
    [Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataScheme = "https",
    DataHost = "davidtest.dev",
    DataPathPrefix = "/share/",
    AutoVerify = true)]
[IntentFilter(
    [Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataScheme = "streamtunes",
    DataHost = "share")]
[IntentFilter(
    [Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataScheme = "streamtunes",
    DataHost = "tip")]
public class MainActivity : MauiAppCompatActivity
{
    public override bool DispatchTouchEvent(MotionEvent? ev)
    {
        if (ev?.Action == MotionEventActions.Down && CurrentFocus is Android.Widget.EditText editText)
        {
            var bounds = new Android.Graphics.Rect();
            editText.GetGlobalVisibleRect(bounds);

            if (!bounds.Contains((int)ev.RawX, (int)ev.RawY))
            {
                editText.ClearFocus();

                if (GetSystemService(InputMethodService) is InputMethodManager inputMethodManager)
                {
                    inputMethodManager.HideSoftInputFromWindow(editText.WindowToken, HideSoftInputFlags.None);
                }
            }
        }

        return base.DispatchTouchEvent(ev);
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        SetTheme(Resource.Style.MainThemeEdgeToEdge);

        base.OnCreate(savedInstanceState);

        // Register a fallback back-pressed callback so the system back button
        // moves the app to background instead of finishing the activity.
        OnBackPressedDispatcher.AddCallback(this, new BackPressedCallback(this));

        // Handle deep link from initial launch
        HandleDeepLink(Intent);

        // Cold start: queue only. Shell does not exist yet, so navigating here does nothing -
        // AppActivationCoordinator replays it once the app is up.
        QueueTappedNotification(Intent);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        HandleDeepLink(intent);

        // Warm start: the app is already running and can navigate immediately.
        QueueTappedNotification(intent);
        FlushTappedNotification();
    }

    /// <summary>
    /// FCM puts the message's data payload on the launch intent as extras - both for a notification
    /// Android displayed itself while the app was backgrounded, and for one
    /// StreamTunesFirebaseMessagingService posted in the foreground, which copies the same keys.
    /// </summary>
    private static void QueueTappedNotification(Intent? intent)
    {
        var extras = intent?.Extras;

        if (extras is null)
        {
            return;
        }

        var data = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (var key in extras.KeySet() ?? [])
        {
            // Only strings: the extras also carry Android's own bundles, and GetString on those
            // returns null rather than throwing, so this stays quiet about them.
            var value = extras.GetString(key);

            if (value is not null)
            {
                data[key] = value;
            }
        }

        Router?.QueuePending(data);
    }

    private static void FlushTappedNotification()
    {
        if (Router is { } router)
        {
            _ = router.FlushPendingAsync();
        }
    }

    private static MusicSalesApp.Maui.Services.IPushNotificationRouter? Router =>
        IPlatformApplication.Current?.Services.GetService(typeof(MusicSalesApp.Maui.Services.IPushNotificationRouter))
            as MusicSalesApp.Maui.Services.IPushNotificationRouter;

    private void HandleDeepLink(Intent? intent)
    {
        if (intent?.Action != Intent.ActionView || intent.Data == null)
            return;

        var url = intent.Data.ToString();
        if (!string.IsNullOrEmpty(url))
        {
            Platform.CurrentActivity?.RunOnUiThread(() =>
            {
                var uri = new Uri(url);
                Microsoft.Maui.Controls.Application.Current?.SendOnAppLinkRequestReceived(uri);
            });
        }
    }

    public override void OnConfigurationChanged(Configuration newConfig)
    {
        base.OnConfigurationChanged(newConfig);
        SyncAppTheme(newConfig);
    }

    /// <summary>
    /// Explicitly syncs the Android system dark/light mode with MAUI's UserAppTheme.
    /// Called on configuration changes (e.g., user toggles dark mode in Android Settings).
    /// Initial theme sync happens in App.xaml.cs where Application.Current is guaranteed to exist.
    /// </summary>
    internal static void SyncAppTheme(Configuration? config)
    {
        if (config == null || Microsoft.Maui.Controls.Application.Current == null) return;

        var nightMode = config.UiMode & UiMode.NightMask;
        Microsoft.Maui.Controls.Application.Current.UserAppTheme = nightMode == UiMode.NightYes
            ? AppTheme.Dark
            : AppTheme.Light;
    }

    /// <summary>
    /// Callback for the Android OnBackPressedDispatcher that moves the app to
    /// background (rather than finishing/destroying the activity).
    /// </summary>
    private sealed class BackPressedCallback(Activity activity)
        : AndroidX.Activity.OnBackPressedCallback(true)
    {
        public override void HandleOnBackPressed()
        {
            activity.MoveTaskToBack(true);
        }
    }
}
