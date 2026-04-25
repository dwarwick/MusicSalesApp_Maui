using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.Views;

namespace MusicSalesApp.Maui;

public partial class AppShell : Shell
{
	private readonly IAuthService _authService;

	public AppShell(IAuthService authService)
	{
		InitializeComponent();

		_authService = authService;
		_authService.AuthStateChanged += OnAuthStateChanged;

		CopyrightLabel.Text = $"\u00A9 {DateTime.Now.Year} Streamtunes";
		VersionLabel.Text = $"version: {AppInfo.Current.VersionString} ({AppInfo.Current.BuildString})";

		// Register routes for pages that aren't in the flyout
		Routing.RegisterRoute("login", typeof(LoginPage));
		Routing.RegisterRoute("register", typeof(RegisterPage));
		Routing.RegisterRoute("verify-email", typeof(VerifyEmailPage));
		Routing.RegisterRoute("forgot-password", typeof(ForgotPasswordPage));
		Routing.RegisterRoute("reset-password", typeof(ResetPasswordPage));
		Routing.RegisterRoute("song-player", typeof(SongPlayerPage));
		Routing.RegisterRoute("persona", typeof(PersonaPage));
		Routing.RegisterRoute("playlist-player", typeof(PlaylistPlayerPage));
		Routing.RegisterRoute("account-settings", typeof(AccountSettingsPage));
		Routing.RegisterRoute("policy", typeof(PolicyPage));
		Routing.RegisterRoute("my-playlists", typeof(MyPlaylistsPage));

		UpdateMenuVisibility();
	}

	private void OnAuthStateChanged()
	{
		MainThread.BeginInvokeOnMainThread(UpdateMenuVisibility);
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

	private async void OnLoginClicked(object? sender, EventArgs e)
	{
		Shell.Current.FlyoutIsPresented = false;
		await GoToAsync("login");
	}

	private async void OnRegisterClicked(object? sender, EventArgs e)
	{
		Shell.Current.FlyoutIsPresented = false;
		await GoToAsync("register");
	}

	private async void OnValidateEmailClicked(object? sender, EventArgs e)
	{
		Shell.Current.FlyoutIsPresented = false;
		await GoToAsync("verify-email", new Dictionary<string, object>
		{
			["UserId"] = _authService.UserId ?? 0,
			["Email"] = _authService.Email ?? string.Empty,
			["Password"] = string.Empty
		});
	}

	private async void OnAccountSettingsClicked(object? sender, EventArgs e)
	{
		Shell.Current.FlyoutIsPresented = false;
		await GoToAsync("account-settings");
	}

	private async void OnMyPlaylistsClicked(object? sender, EventArgs e)
	{
		Shell.Current.FlyoutIsPresented = false;
		await GoToAsync("my-playlists");
	}

	private async void OnLogoutClicked(object? sender, EventArgs e)
	{
		Shell.Current.FlyoutIsPresented = false;
		await _authService.LogoutAsync();
	}

	private async void OnTermsOfUseClicked(object? sender, EventArgs e)
	{
		Shell.Current.FlyoutIsPresented = false;
		await GoToAsync("policy", new Dictionary<string, object>
		{
			["title"] = "Terms of Use",
			["path"] = "/terms-of-use"
		});
	}

	private async void OnPrivacyPolicyClicked(object? sender, EventArgs e)
	{
		Shell.Current.FlyoutIsPresented = false;
		await GoToAsync("policy", new Dictionary<string, object>
		{
			["title"] = "Privacy Policy",
			["path"] = "/privacy-policy"
		});
	}

	private async void OnAccountDeletionClicked(object? sender, EventArgs e)
	{
		Shell.Current.FlyoutIsPresented = false;
		await GoToAsync("policy", new Dictionary<string, object>
		{
			["title"] = "Account Deletion",
			["path"] = "/account-deletion"
		});
	}

	private async void OnRefundPolicyClicked(object? sender, EventArgs e)
	{
		Shell.Current.FlyoutIsPresented = false;
		await GoToAsync("policy", new Dictionary<string, object>
		{
			["title"] = "User Refund Policy",
			["path"] = "/user-refund-policy"
		});
	}

	private async void OnCopyrightPolicyClicked(object? sender, EventArgs e)
	{
		Shell.Current.FlyoutIsPresented = false;
		await GoToAsync("policy", new Dictionary<string, object>
		{
			["title"] = "Copyright Policy",
			["path"] = "/creator-agreement"
		});
	}

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
