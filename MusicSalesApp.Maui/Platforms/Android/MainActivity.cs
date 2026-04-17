using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Content.Res;
using Android.OS;

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
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // Register a fallback back-pressed callback so the system back button
        // moves the app to background instead of finishing the activity.
        OnBackPressedDispatcher.AddCallback(this, new BackPressedCallback(this));

        // Handle deep link from initial launch
        HandleDeepLink(Intent);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        HandleDeepLink(intent);
    }

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
