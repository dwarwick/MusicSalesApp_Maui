using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.Views;

namespace MusicSalesApp.Maui;

public partial class AppShell : Shell
{
	private readonly IAuthService _authService;
	private readonly IAdminMessageCoordinator _adminMessageCoordinator;
	private readonly IBrowserService _browserService;
	private readonly IAppConfig _appConfig;
	private string _testingServerBannerUrl = string.Empty;

	public AppShell(
		IAuthService authService,
		IAdminMessageCoordinator adminMessageCoordinator,
		ITestingServerBannerService testingServerBannerService,
		IBrowserService browserService,
		IAppConfig appConfig)
	{
		InitializeComponent();

		_authService = authService;
		_adminMessageCoordinator = adminMessageCoordinator;
		_browserService = browserService;
		_appConfig = appConfig;
		_authService.AuthStateChanged += OnAuthStateChanged;
		InitializeTestingServerBanner(testingServerBannerService.GetBannerInfo());

		CopyrightLabel.Text = $"\u00A9 {DateTime.Now.Year} Streamtunes";
		VersionLabel.Text = $"version: {AppInfo.Current.VersionString} ({AppInfo.Current.BuildString})";

		// Register routes for pages that aren't in the flyout
		Routing.RegisterRoute(NavigationRoutes.Login, typeof(LoginPage));
		Routing.RegisterRoute(NavigationRoutes.Register, typeof(RegisterPage));
		Routing.RegisterRoute(NavigationRoutes.VerifyEmail, typeof(VerifyEmailPage));
		Routing.RegisterRoute(NavigationRoutes.ForgotPassword, typeof(ForgotPasswordPage));
		Routing.RegisterRoute(NavigationRoutes.ResetPassword, typeof(ResetPasswordPage));
		Routing.RegisterRoute(NavigationRoutes.SongPlayer, typeof(SongPlayerPage));
		Routing.RegisterRoute(NavigationRoutes.Persona, typeof(PersonaPage));
		Routing.RegisterRoute(NavigationRoutes.PlaylistPlayer, typeof(PlaylistPlayerPage));
		Routing.RegisterRoute(NavigationRoutes.AccountSettings, typeof(AccountSettingsPage));
		Routing.RegisterRoute(NavigationRoutes.Policy, typeof(PolicyPage));
		Routing.RegisterRoute(NavigationRoutes.MyPlaylists, typeof(MyPlaylistsPage));

		UpdateMenuVisibility();
	}

	private void InitializeTestingServerBanner(TestingServerBannerInfo bannerInfo)
	{
		TestingServerBannerBorder.IsVisible = bannerInfo.IsVisible;
		if (!bannerInfo.IsVisible)
		{
			return;
		}

		_testingServerBannerUrl = bannerInfo.Url;
		TestingServerBannerTextLabel.Text = $"{bannerInfo.MessagePrefix} ";
		TestingServerBannerUrlLabel.Text = bannerInfo.Url;
	}

	private void OnAuthStateChanged()
	{
		MainThread.BeginInvokeOnMainThread(UpdateMenuVisibility);
	}

	private async void OnTestingServerBannerUrlTapped(object? sender, TappedEventArgs e)
	{
		if (string.IsNullOrWhiteSpace(_testingServerBannerUrl))
		{
			return;
		}

		await _browserService.OpenExternalAsync(_testingServerBannerUrl);
	}

	private void UpdateMenuVisibility()
	{
		Shell.SetFlyoutItemIsVisible(LoginMenuItem, !_authService.IsLoggedIn);
		Shell.SetFlyoutItemIsVisible(RegisterMenuItem, !_authService.IsLoggedIn);
		Shell.SetFlyoutItemIsVisible(ValidateEmailMenuItem, _authService.IsLoggedIn && !_authService.EmailConfirmed);
		Shell.SetFlyoutItemIsVisible(AccountSettingsMenuItem, _authService.IsLoggedIn);
		Shell.SetFlyoutItemIsVisible(LogoutMenuItem, _authService.IsLoggedIn);
		Shell.SetFlyoutItemIsVisible(MyPlaylistsMenuItem, _authService.IsLoggedIn && _authService.EmailConfirmed);
	}

	protected override void OnNavigated(ShellNavigatedEventArgs args)
	{
		base.OnNavigated(args);

		if (_authService.IsLoggedIn)
		{
			_ = _adminMessageCoordinator.ProcessPendingMessagesAsync();
		}
	}

	private async void OnLoginClicked(object? sender, EventArgs e)
	{
		Shell.Current.FlyoutIsPresented = false;
		await GoToAsync(NavigationRoutes.LoginEntry);
	}

	private async void OnRegisterClicked(object? sender, EventArgs e)
	{
		Shell.Current.FlyoutIsPresented = false;
		await GoToAsync(NavigationRoutes.Register);
	}

	private async void OnValidateEmailClicked(object? sender, EventArgs e)
	{
		Shell.Current.FlyoutIsPresented = false;
		await GoToAsync(NavigationRoutes.VerifyEmail, new Dictionary<string, object>
		{
			["UserId"] = _authService.UserId ?? 0,
			["Email"] = _authService.Email ?? string.Empty,
			["Password"] = string.Empty
		});
	}

	private async void OnAccountSettingsClicked(object? sender, EventArgs e)
	{
		Shell.Current.FlyoutIsPresented = false;
		await GoToAsync(NavigationRoutes.AccountSettings);
	}

	private async void OnMyPlaylistsClicked(object? sender, EventArgs e)
	{
		Shell.Current.FlyoutIsPresented = false;
		await GoToAsync(NavigationRoutes.MyPlaylists);
	}

	private async void OnLogoutClicked(object? sender, EventArgs e)
	{
		Shell.Current.FlyoutIsPresented = false;
		await _authService.LogoutAsync();
	}

	private async void OnTermsOfUseClicked(object? sender, EventArgs e)
	{
		Shell.Current.FlyoutIsPresented = false;
		await _browserService.OpenExternalAsync(BuildWebUrl("/terms-of-use"));
	}

	private async void OnPrivacyPolicyClicked(object? sender, EventArgs e)
	{
		Shell.Current.FlyoutIsPresented = false;
		await _browserService.OpenExternalAsync(BuildWebUrl("/privacy-policy"));
	}

	private async void OnAccountDeletionClicked(object? sender, EventArgs e)
	{
		Shell.Current.FlyoutIsPresented = false;
		await _browserService.OpenExternalAsync(BuildWebUrl("/account-deletion"));
	}

	private async void OnRefundPolicyClicked(object? sender, EventArgs e)
	{
		Shell.Current.FlyoutIsPresented = false;
		await _browserService.OpenExternalAsync(BuildWebUrl("/user-refund-policy"));
	}

	private async void OnCopyrightPolicyClicked(object? sender, EventArgs e)
	{
		Shell.Current.FlyoutIsPresented = false;
		await _browserService.OpenExternalAsync(BuildWebUrl("/creator-agreement"));
	}

	private string BuildWebUrl(string relativePath)
		=> $"{_appConfig.WebBaseUrl.TrimEnd('/')}/{relativePath.TrimStart('/')}";

	protected override bool OnBackButtonPressed()
	{
		// At the root page, the hardware/software back button should move the app to background
		// (standard Android behaviour). Shell doesn't always propagate this to the OS.
		if (Navigation.NavigationStack.Count <= 1)
		{
#if ANDROID
			Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?.MoveTaskToBack(true);
			return true;
#endif
		}

		return base.OnBackButtonPressed();
	}
}
