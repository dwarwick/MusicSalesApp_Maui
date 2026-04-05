namespace MusicSalesApp.Maui;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();

		// Sync the Android system theme to MAUI at startup.
		// Application.Current is now set (we're in the constructor), so this is safe.
#if ANDROID
		var config = Android.App.Application.Context.Resources?.Configuration;
		if (config != null)
		{
			var nightMode = config.UiMode & Android.Content.Res.UiMode.NightMask;
			UserAppTheme = nightMode == Android.Content.Res.UiMode.NightYes
				? AppTheme.Dark
				: AppTheme.Light;
		}
#endif
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new AppShell());
	}
}