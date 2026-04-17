namespace MusicSalesApp.Maui.Views;

public partial class FacebookShareButton : ContentView
{
    private const string FacebookAppPackage = "com.facebook.katana";

    public static readonly BindableProperty ShareUrlProperty =
        BindableProperty.Create(nameof(ShareUrl), typeof(string), typeof(FacebookShareButton));

    public string ShareUrl
    {
        get => (string)GetValue(ShareUrlProperty);
        set => SetValue(ShareUrlProperty, value);
    }

    public FacebookShareButton()
    {
        InitializeComponent();
    }

    private async void OnFbShareClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrEmpty(ShareUrl)) return;

        System.Diagnostics.Debug.WriteLine($"[FacebookShare] ShareUrl = '{ShareUrl}'");

#if ANDROID
        if (TryShareViaFacebookApp())
            return;
#endif
        // Fallback: open sharer.php in the browser if the FB app is not installed
        var encodedUrl = Uri.EscapeDataString(ShareUrl);
        var facebookShareUrl = $"https://www.facebook.com/sharer/sharer.php?u={encodedUrl}";
        System.Diagnostics.Debug.WriteLine($"[FacebookShare] Fallback browser: {facebookShareUrl}");
        await Browser.Default.OpenAsync(facebookShareUrl, BrowserLaunchMode.SystemPreferred);
    }

#if ANDROID
    private bool TryShareViaFacebookApp()
    {
        try
        {
            var intent = new Android.Content.Intent(Android.Content.Intent.ActionSend);
            intent.SetType("text/plain");
            intent.PutExtra(Android.Content.Intent.ExtraText, ShareUrl);
            intent.SetPackage(FacebookAppPackage);

            var activity = Platform.CurrentActivity;
            if (activity is null)
                return false;

            // Verify the Facebook app can handle this intent
            var resolveInfo = activity.PackageManager?.ResolveActivity(
                intent, Android.Content.PM.PackageInfoFlags.MatchDefaultOnly);
            if (resolveInfo is null)
                return false;

            System.Diagnostics.Debug.WriteLine($"[FacebookShare] Sharing via FB app intent: {ShareUrl}");
            activity.StartActivity(intent);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FacebookShare] Intent failed: {ex.Message}");
            return false;
        }
    }
#endif
}
