using MusicSalesApp.Maui.Resources.Styles;
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
	private ImageSource _currentLogo = ImageSource.FromFile("logo_light_small.png");
	private readonly IAutoScrollSettingsService _autoScrollSettingsService;
	private bool _isAutoScrollToggleVisible;
	private Color _navBarAccentColor = AppColors.AccentFill;
	private Color _navBarTextColor = Colors.Black;

	/// <summary>
	/// The logo artwork for the bar as it is currently coloured.
	/// </summary>
	/// <remarks>
	/// A property rather than a direct assignment to a named Image: the title view can be rebuilt,
	/// and an x:Name field then refers to an instance no longer on screen. The two logos are not
	/// interchangeable - the light-theme one is dark ink on an opaque light ground, so on a dark
	/// bar it shows as a white block.
	/// </remarks>
	public ImageSource CurrentLogo
	{
		get => _currentLogo;
		private set
		{
			if (_currentLogo == value)
			{
				return;
			}

			_currentLogo = value;
			OnPropertyChanged();
		}
	}

	/// <summary>
	/// Whether the navigation bar's Auto-scroll checkbox is shown.
	/// </summary>
	/// <remarks>
	/// Only the music library and the playlist player have a song list for it to govern, so it is
	/// hidden everywhere else rather than sitting inert on pages it cannot affect. Like
	/// <see cref="CurrentLogo"/>, this is a bound PROPERTY and never an assignment to an x:Name -
	/// the title view can be rebuilt, and the field would then point at a discarded instance.
	/// </remarks>
	public bool IsAutoScrollToggleVisible
	{
		get => _isAutoScrollToggleVisible;
		private set
		{
			if (_isAutoScrollToggleVisible == value)
			{
				return;
			}

			_isAutoScrollToggleVisible = value;
			OnPropertyChanged();
		}
	}

	/// <summary>
	/// The Auto-scroll setting, surfaced for the checkbox's two-way binding.
	/// </summary>
	public bool IsAutoScrollEnabled
	{
		get => _autoScrollSettingsService.ScrollAutomatically;
		set
		{
			if (_autoScrollSettingsService.ScrollAutomatically == value)
			{
				return;
			}

			_autoScrollSettingsService.ScrollAutomatically = value;
			OnPropertyChanged();
		}
	}

	/// <summary>
	/// Tick colour for the Auto-scroll checkbox, and the colour of its label.
	/// </summary>
	/// <remarks>
	/// Recomputed per page for the same reason the logo is: the bar is dark in either OS theme on
	/// the playlist player and follows the theme on the music library, so no single value is right
	/// on both. A converter cannot do this either - it resolves the theme once and freezes it, which
	/// is the fault written up on PillStyleConverter in MusicLibraryPage.xaml.cs.
	/// </remarks>
	public Color NavBarAccentColor
	{
		get => _navBarAccentColor;
		private set
		{
			if (_navBarAccentColor == value)
			{
				return;
			}

			_navBarAccentColor = value;
			OnPropertyChanged();
		}
	}

	public Color NavBarTextColor
	{
		get => _navBarTextColor;
		private set
		{
			if (_navBarTextColor == value)
			{
				return;
			}

			_navBarTextColor = value;
			OnPropertyChanged();
		}
	}

	public AppShell(
		IAuthService authService,
		IAdminMessageCoordinator adminMessageCoordinator,
		ITestingServerBannerService testingServerBannerService,
		IBrowserService browserService,
		IAppConfig appConfig,
		IAutoScrollSettingsService autoScrollSettingsService)
	{
		_autoScrollSettingsService = autoScrollSettingsService;

		InitializeComponent();

		_authService = authService;
		_adminMessageCoordinator = adminMessageCoordinator;
		_browserService = browserService;
		_appConfig = appConfig;
		_authService.AuthStateChanged += OnAuthStateChanged;
		InitializeTestingServerBanner(testingServerBannerService.GetBannerInfo());

		// The bar's COLOURS follow the theme on their own - they are SetAppThemeColor bindings.
		// The logo cannot: it is an image chosen by an if, resolved once, and ApplyChromeForCurrentPage
		// only runs on navigation. So an OS theme switch mid-session repainted the bar and left the
		// previous theme's artwork on it - a black block on a white bar, and the reverse going the
		// other way. Re-running the whole method keeps the two in step.
		if (Application.Current is { } app)
		{
			app.RequestedThemeChanged += OnRequestedThemeChanged;
		}

		CopyrightLabel.Text = $"\u00A9 {DateTime.Now.Year} Streamtunes";
		VersionLabel.Text = $"version: {AppInfo.Current.VersionString} ({AppInfo.Current.BuildString})";

		// Register routes for pages that aren't in the flyout
		Routing.RegisterRoute(NavigationRoutes.Login, typeof(LoginPage));
		Routing.RegisterRoute(NavigationRoutes.Register, typeof(RegisterPage));
		Routing.RegisterRoute(NavigationRoutes.VerifyEmail, typeof(VerifyEmailPage));
		Routing.RegisterRoute(NavigationRoutes.ForgotPassword, typeof(ForgotPasswordPage));
		Routing.RegisterRoute(NavigationRoutes.ResetPassword, typeof(ResetPasswordPage));
		Routing.RegisterRoute(NavigationRoutes.SongPlayer, typeof(SongPlayerPage));
		Routing.RegisterRoute(NavigationRoutes.PlaylistPlayer, typeof(PlaylistPlayerPage));
		Routing.RegisterRoute(NavigationRoutes.AccountSettings, typeof(AccountSettingsPage));
		Routing.RegisterRoute(NavigationRoutes.Config, typeof(ConfigPage));
		Routing.RegisterRoute(NavigationRoutes.Policy, typeof(PolicyPage));
		Routing.RegisterRoute(NavigationRoutes.MyPlaylists, typeof(MyPlaylistsPage));
		Routing.RegisterRoute(NavigationRoutes.ContactUs, typeof(ContactUsPage));

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

	/// <summary>
	/// The label is part of the checkbox's hit target - a bare CheckBox is a small target in a
	/// navigation bar, and the words are the larger half of the control.
	/// </summary>
	private void OnAutoScrollLabelTapped(object? sender, TappedEventArgs e) =>
		IsAutoScrollEnabled = !IsAutoScrollEnabled;

	private void UpdateMenuVisibility()
	{
		Shell.SetFlyoutItemIsVisible(LoginMenuItem, !_authService.IsLoggedIn);
		Shell.SetFlyoutItemIsVisible(RegisterMenuItem, !_authService.IsLoggedIn);
		Shell.SetFlyoutItemIsVisible(ValidateEmailMenuItem, _authService.IsLoggedIn && !_authService.EmailConfirmed);
		Shell.SetFlyoutItemIsVisible(AccountSettingsMenuItem, _authService.IsLoggedIn);
		Shell.SetFlyoutItemIsVisible(LogoutMenuItem, _authService.IsLoggedIn);
		Shell.SetFlyoutItemIsVisible(MyPlaylistsMenuItem, _authService.IsLoggedIn && _authService.EmailConfirmed);
		Shell.SetFlyoutItemIsVisible(ContactUsMenuItem, _authService.IsLoggedIn && _authService.EmailConfirmed);
		UploadYourOwnMusicFooterRow.IsVisible =
			FlyoutMenuVisibilityPolicy.ShouldShowUploadYourOwnMusic(_authService.IsLoggedIn, _authService.IsCreator);
	}

	private void OnRequestedThemeChanged(object? sender, AppThemeChangedEventArgs e) =>
		Dispatcher.Dispatch(ApplyChromeForCurrentPage);

	protected override void OnNavigated(ShellNavigatedEventArgs args)
	{
		base.OnNavigated(args);

		ApplyChromeForCurrentPage();

		if (_authService.IsLoggedIn)
		{
			_ = _adminMessageCoordinator.ProcessPendingMessagesAsync();
		}
	}

	/// <summary>
	/// Darken the navigation bar on the player pages, and restore it everywhere else.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The players are dark in EITHER OS theme, so on a light-mode device a themed navigation bar
	/// sat white above a dark page. The page cannot fix this alone: Shell chrome and the title view
	/// belong to the shell, not to the page.
	/// </para>
	/// <para>
	/// Recomputed from the destination on every navigation rather than toggled on entry and exit,
	/// so it cannot drift - a dark bar stranded on an ordinary page is exactly what a toggle
	/// produces. Restoring re-applies the theme BINDINGS through SetAppTheme rather than a fixed
	/// colour, because assigning a plain value would break light/dark switching for the rest of the
	/// session.
	/// </para>
	/// <para>
	/// Nothing here is allowed to take down startup. This runs on the FIRST navigation, before the
	/// title view is necessarily realised, and chrome colour is never worth a crash - the app died
	/// before its own logger existed when this threw, which leaves no diagnostic behind at all.
	/// </para>
	/// </remarks>
	private void ApplyChromeForCurrentPage()
	{
		try
		{
			var onPlayer = CurrentPage is SongPlayerPage or PlaylistPlayerPage;

			// Only these two pages own a song list for Auto-scroll to move.
			IsAutoScrollToggleVisible = CurrentPage is MusicLibraryPage or PlaylistPlayerPage;

			if (onPlayer)
			{
				SetValue(Shell.BackgroundColorProperty, AppColors.PlayerBg);
				SetValue(Shell.ForegroundColorProperty, AppColors.PlayerText);
				SetValue(Shell.TitleColorProperty, AppColors.PlayerText);
				// The light-theme logo is dark ink on an opaque light ground; it shows as a
				// white block on a dark bar.
				CurrentLogo = ImageSource.FromFile("logo_dark_small.png");
				NavBarAccentColor = AppColors.BlueBright;
				NavBarTextColor = AppColors.PlayerText;
				return;
			}

			this.SetAppThemeColor(Shell.BackgroundColorProperty, Colors.White, AppColors.NavBarDark);
			this.SetAppThemeColor(Shell.ForegroundColorProperty, Colors.Black, Colors.White);
			this.SetAppThemeColor(Shell.TitleColorProperty, Colors.Black, Colors.White);
			var isDarkTheme = Application.Current?.RequestedTheme == AppTheme.Dark;
			CurrentLogo = ImageSource.FromFile(
				isDarkTheme
					? "logo_dark_small.png"
					: "logo_light_small.png");
			NavBarAccentColor = AppColors.AccentFill;
			NavBarTextColor = isDarkTheme ? Colors.White : Colors.Black;
		}
		catch (Exception ex)
		{
			// Deliberately swallowed. Chrome colour is cosmetic and this runs on the startup
			// navigation, where an exception kills the app before its own logger exists - which is
			// exactly what happened, and it left no diagnostic anywhere.
			System.Diagnostics.Debug.WriteLine($"Navigation-bar chrome not applied: {ex}");
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

	private async void OnConfigClicked(object? sender, EventArgs e)
	{
		Shell.Current.FlyoutIsPresented = false;
		await GoToAsync(NavigationRoutes.Config);
	}

	private async void OnMyPlaylistsClicked(object? sender, EventArgs e)
	{
		Shell.Current.FlyoutIsPresented = false;
		await GoToAsync(NavigationRoutes.MyPlaylists);
	}

	private async void OnUploadYourOwnMusicClicked(object? sender, EventArgs e)
	{
		Shell.Current.FlyoutIsPresented = false;
		await _browserService.OpenExternalAsync(ExternalWebLinks.UploadYourOwnMusicUrl);
	}

	private async void OnContactUsClicked(object? sender, EventArgs e)
	{
		Shell.Current.FlyoutIsPresented = false;
		await GoToAsync(NavigationRoutes.ContactUs);
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
