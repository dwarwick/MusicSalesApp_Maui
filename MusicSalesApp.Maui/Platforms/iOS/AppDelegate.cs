using Foundation;
using Microsoft.Maui.Authentication;
using UIKit;

namespace MusicSalesApp.Maui;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
	public override bool OpenUrl(UIApplication app, NSUrl url, NSDictionary options)
	{
		if (WebAuthenticator.Default.OpenUrl(app, url, options))
		{
			return true;
		}

		return base.OpenUrl(app, url, options);
	}

	/// <summary>
	/// APNs has issued a device token for this install.
	/// </summary>
	/// <remarks>
	/// The only place iOS ever hands one over - there is no method that returns it - so this
	/// callback is load-bearing. Without it the app registers successfully and no token ever
	/// reaches the server, which looks exactly like push being broken server-side.
	/// </remarks>
	// [Export] with the Objective-C selector rather than `override`: MauiUIApplicationDelegate
	// does not declare these as virtual, so overriding does not compile. The selector is what iOS
	// actually dispatches on, and binding it this way works whatever shape the managed base class
	// happens to have.
	[Export("application:didRegisterForRemoteNotificationsWithDeviceToken:")]
	public void RegisteredForRemoteNotifications(UIApplication application, NSData deviceToken)
	{
		Platforms.iOS.ApplePushTokenBroker.SetToken(deviceToken);
	}

	[Export("application:didFailToRegisterForRemoteNotificationsWithError:")]
	public void FailedToRegisterForRemoteNotifications(UIApplication application, NSError error)
	{
		// No entitlement, no network, or a simulator with no push capability. Clearing means the
		// coordinator will ask again rather than registering a token that is no longer valid.
		Platforms.iOS.ApplePushTokenBroker.Clear();
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
