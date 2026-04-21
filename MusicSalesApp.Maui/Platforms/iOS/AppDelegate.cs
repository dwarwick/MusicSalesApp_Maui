using Foundation;
using MediaManager;

namespace MusicSalesApp.Maui;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
	public override bool FinishedLaunching(UIKit.UIApplication application, Foundation.NSDictionary launchOptions)
	{
		CrossMediaManager.Current.Init();
		return base.FinishedLaunching(application, launchOptions);
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
