using Android.App;
using Android.Content;
using Android.Content.PM;
using Microsoft.Maui.Authentication;

namespace MusicSalesApp.Maui;

[Activity(NoHistory = true, Exported = true, LaunchMode = LaunchMode.SingleTop)]
[IntentFilter(
    [Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataScheme = "streamtunes",
    DataHost = "auth")]
public class WebAuthenticationCallbackActivity : WebAuthenticatorCallbackActivity
{
}