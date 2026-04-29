using Foundation;
using MediaManager;
using Microsoft.Maui.Authentication;
using UIKit;

namespace MusicSalesApp.Maui;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
	public override bool FinishedLaunching(UIKit.UIApplication application, Foundation.NSDictionary launchOptions)
	{
		CrossMediaManager.Current.Init();
		return base.FinishedLaunching(application, launchOptions);
	}

	public override bool OpenUrl(UIApplication app, NSUrl url, NSDictionary options)
	{
		if (WebAuthenticator.Default.OpenUrl(app, url, options))
		{
			return true;
		}

		return base.OpenUrl(app, url, options);
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
