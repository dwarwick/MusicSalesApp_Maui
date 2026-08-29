using Microsoft.Extensions.Logging;
using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui;

public partial class App : Application
{
	private readonly IAuthService _authService;
	private readonly IAdminMessageCoordinator _adminMessageCoordinator;
	private readonly IMusicService _musicService;
	private readonly IBillingService _billingService;
	private readonly ITestingServerBannerService _testingServerBannerService;
	private readonly IBrowserService _browserService;
	private readonly IAppConfig _appConfig;
	private readonly IAutoScrollSettingsService _autoScrollSettingsService;
	private readonly ITipFlowHandler _tipFlowHandler;
	private readonly ISignalRConnectionManager _signalRConnectionManager;
	private readonly PlaybackFailureNotificationCoordinator _playbackFailureNotificationCoordinator;
	private readonly ILogger<App> _logger;

	public App(
		IAuthService authService,
		IAdminMessageCoordinator adminMessageCoordinator,
		IMusicService musicService,
		IBillingService billingService,
		ITestingServerBannerService testingServerBannerService,
		IBrowserService browserService,
		IAppConfig appConfig,
		IAutoScrollSettingsService autoScrollSettingsService,
		ITipFlowHandler tipFlowHandler,
		ISignalRConnectionManager signalRConnectionManager,
		PlaybackFailureNotificationCoordinator playbackFailureNotificationCoordinator,
		ILogger<App> logger)
	{
		InitializeComponent();
		_logger = logger;
		_authService = authService;
		_adminMessageCoordinator = adminMessageCoordinator;
		_musicService = musicService;
		_billingService = billingService;
		_testingServerBannerService = testingServerBannerService;
		_browserService = browserService;
		_appConfig = appConfig;
		_autoScrollSettingsService = autoScrollSettingsService;
		_tipFlowHandler = tipFlowHandler;
		_signalRConnectionManager = signalRConnectionManager;
		_playbackFailureNotificationCoordinator = playbackFailureNotificationCoordinator;

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
		var window = new Window(new AppShell(_authService, _adminMessageCoordinator, _testingServerBannerService, _browserService, _appConfig, _autoScrollSettingsService));

		window.Created += async (_, _) =>
		{
			// Start connecting to platform billing, but do not wait for it here. The platform store
			// answers through a callback that can be slow or, on a device where the store is
			// disabled or wedged, never arrive — and waiting on that used to hold up everything
			// below it, leaving a signed-in user looking signed out. Session restore reaches
			// billing through the same connection gate, so it still gets a connected client;
			// it just no longer queues behind this call.
			StartBillingInitialization();

			await _signalRConnectionManager.InitializeAsync();

			// Restore saved session and silently verify any unsynced purchases
			await _authService.TryRestoreSessionAsync();
			await _adminMessageCoordinator.InitializeAsync();
			await _adminMessageCoordinator.ProcessPendingMessagesAsync();
		};

		return window;
	}

	/// <summary>
	/// Kicks off the billing connection without joining it to the startup chain.
	/// </summary>
	private void StartBillingInitialization()
	{
		_ = Task.Run(async () =>
		{
			try
			{
				await _billingService.InitializeAsync();
			}
			catch (Exception ex)
			{
				// The gate converts its own failures into a false result, so anything reaching here
				// came from outside it. An empty catch would swallow that without a trace — exactly
				// the silence that has already caused a wrong diagnosis on this work.
				_logger.LogError(ex, "Billing initialization failed at startup");
			}
		});
	}

	protected override async void OnAppLinkRequestReceived(Uri uri)
	{
		base.OnAppLinkRequestReceived(uri);

		if (await _tipFlowHandler.HandleAppLinkAsync(uri))
		{
			return;
		}

		// Handle deep links like https://streamtunes.net/song/{title}
		if (uri.Scheme == "https"
		    && uri.AbsolutePath.StartsWith("/song/", StringComparison.OrdinalIgnoreCase)
		    && uri.AbsolutePath.Length > "/song/".Length)
		{
			var songTitle = Uri.UnescapeDataString(uri.AbsolutePath["/song/".Length..]);
			await Shell.Current.GoToAsync("song-player", new Dictionary<string, object>
			{
				["SongTitle"] = songTitle
			});
		}
		// Handle deep links like https://streamtunes.net/share/{id}
		// or custom scheme streamtunes://share/{id}
		else if (TryParseShareSongId(uri, out var songId))
		{
			var songs = await _musicService.GetSongsAsync();
			var song = songs.FirstOrDefault(s => s.Id == songId);
			if (song != null)
			{
				await Shell.Current.GoToAsync("song-player", new Dictionary<string, object>
				{
					["Song"] = song
				});
			}
		}
	}

	/// <summary>
	/// Tries to extract a song ID from a /share/{id} deep link URL.
	/// Supports both https://host/share/{id} and streamtunes://share/{id}.
	/// </summary>
	private static bool TryParseShareSongId(Uri uri, out int songId)
	{
		songId = 0;

		// Custom scheme: streamtunes://share/{id} → Host="share", AbsolutePath="/{id}"
		if (uri.Scheme.Equals("streamtunes", StringComparison.OrdinalIgnoreCase)
		    && uri.Host.Equals("share", StringComparison.OrdinalIgnoreCase)
		    && uri.AbsolutePath.Length > 1)
		{
			return int.TryParse(uri.AbsolutePath[1..], out songId);
		}

		// HTTPS scheme: https://host/share/{id}
		if (uri.Scheme == "https"
		    && uri.AbsolutePath.StartsWith("/share/", StringComparison.OrdinalIgnoreCase))
		{
			return int.TryParse(uri.AbsolutePath["/share/".Length..], out songId);
		}

		return false;
	}
}
