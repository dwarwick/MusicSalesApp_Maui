using Foundation;
using Microsoft.Maui.Authentication;
using MusicSalesApp.Maui.Services;
using UIKit;
using UserNotifications;

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
		Platforms.iOS.ApplePushTokenBroker.SetApnsToken(deviceToken);
	}

	[Export("application:didFailToRegisterForRemoteNotificationsWithError:")]
	public void FailedToRegisterForRemoteNotifications(UIApplication application, NSError error)
	{
		// No entitlement, no network, or a simulator with no push capability. Clearing means the
		// coordinator will ask again rather than registering a token that is no longer valid.
		Platforms.iOS.ApplePushTokenBroker.Clear();
	}

	public override bool FinishedLaunching(UIApplication application, NSDictionary launchOptions)
	{
		var finished = base.FinishedLaunching(application, launchOptions);

		// Set before returning, and deliberately not lazily like Firebase: iOS delivers a
		// cold-start tap immediately after launching, and a delegate assigned any later simply
		// never hears about it - the app opens on Home and the tap looks ignored. This is Apple's
		// own API, so it costs nothing at startup.
		UNUserNotificationCenter.Current.Delegate = new NotificationTapDelegate();

		return finished;
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

	/// <summary>
	/// Turns a tapped notification into a route.
	/// </summary>
	private sealed class NotificationTapDelegate : UNUserNotificationCenterDelegate
	{
		public override void DidReceiveNotificationResponse(
			UNUserNotificationCenter center,
			UNNotificationResponse response,
			Action completionHandler)
		{
			try
			{
				var data = ToDictionary(response?.Notification?.Request?.Content?.UserInfo);
				var router = Router;

				router?.QueuePending(data);

				// Always attempted, never gated on ApplicationState. Resuming from a background tap
				// reports Inactive while iOS transitions, and OnActivated has usually already run
				// and found nothing pending - so gating on Active left the payload queued forever
				// and the tap merely opened the app. The router puts it back if Shell is not ready,
				// which is what makes the cold-start case work instead.
				_ = router?.FlushPendingAsync();
			}
			finally
			{
				// iOS watchdogs this. Not calling it is a hang, so it runs whatever happened above.
				completionHandler();
			}
		}

		/// <summary>
		/// Shows the alert while the app is in the foreground. Without this iOS delivers the
		/// notification silently to a running app, so a push that arrives while someone is using
		/// StreamTunes appears to have been lost.
		/// </summary>
		public override void WillPresentNotification(
			UNUserNotificationCenter center,
			UNNotification notification,
			Action<UNNotificationPresentationOptions> completionHandler)
			=> completionHandler(
				UNNotificationPresentationOptions.Banner |
				UNNotificationPresentationOptions.Sound |
				UNNotificationPresentationOptions.List);

		private static Dictionary<string, string?> ToDictionary(NSDictionary? userInfo)
		{
			var data = new Dictionary<string, string?>(StringComparer.Ordinal);

			if (userInfo is null)
			{
				return data;
			}

			foreach (var pair in userInfo)
			{
				// The payload also carries Apple's own "aps" entry, whose value is a dictionary
				// rather than a string. ToString on it would produce noise, so it is skipped.
				if (pair.Key is NSString key && pair.Value is NSString value)
				{
					data[key.ToString()] = value.ToString();
				}
			}

			return data;
		}

		private static IPushNotificationRouter? Router =>
			IPlatformApplication.Current?.Services.GetService(typeof(IPushNotificationRouter))
				as IPushNotificationRouter;
	}
}
