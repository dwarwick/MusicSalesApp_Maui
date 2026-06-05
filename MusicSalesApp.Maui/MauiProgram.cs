using System.Reflection;
using CommunityToolkit.Maui;
using MediaManager;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;
using Microsoft.Maui.Networking;
using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.ViewModels;
using MusicSalesApp.Maui.Views;
using SkiaSharp.Views.Maui.Controls.Hosting;

namespace MusicSalesApp.Maui;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		// Clear ENVIRONMENT env var to prevent duplicate key conflict
		// when MAUI's ConfigureEnvironmentVariables reads it into configuration
		// (VS Code debugger may inject this variable, causing a collision)
		Environment.SetEnvironmentVariable("ENVIRONMENT", null);

		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.UseMauiCommunityToolkit()
			.UseSkiaSharp()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

		// Load configuration from embedded appsettings JSON files
		var assembly = Assembly.GetExecutingAssembly();
		builder.Configuration.AddJsonStream(
			assembly.GetManifestResourceStream("MusicSalesApp.Maui.appsettings.json")!);

		var isReleaseBuild = false;
#if RELEASE
		isReleaseBuild = true;
#endif
		var settingsEnvironment = AppSettingsEnvironmentResolver.GetEnvironmentName(assembly, isReleaseBuild);
		if (AppSettingsEnvironmentResolver.HasResource(assembly.GetManifestResourceNames(), settingsEnvironment))
		{
			using var envStream = assembly.GetManifestResourceStream(AppSettingsEnvironmentResolver.GetResourceName(settingsEnvironment));
			if (envStream is not null)
			{
				builder.Configuration.AddJsonStream(envStream);
			}
		}
		Console.WriteLine($"[MauiProgram] App settings environment: {settingsEnvironment}");

		// When UseLocalHost is false, override settings with the DavidTest section
		// so the app connects to the remote test server instead of localhost.
		var useLocalHost = builder.Configuration.GetValue<bool>("UseLocalHost", true);
		if (!useLocalHost)
		{
			var davidTest = builder.Configuration.GetSection("DavidTest");
			if (davidTest.Exists())
			{
				builder.Configuration["ApiBaseUrl"] = davidTest["ApiBaseUrl"];
				builder.Configuration["MobileApiKey"] = davidTest["MobileApiKey"];
				// Override Azure settings
				var dtAzure = davidTest.GetSection("Azure");
				if (dtAzure.Exists())
				{
					foreach (var kvp in dtAzure.GetChildren())
					{
						builder.Configuration[$"Azure:{kvp.Key}"] = kvp.Value;
					}
				}
			}
		}

		var appConfig = new AppConfig(builder.Configuration);

		// Register HttpClientFactory with the resolved API base URL
		var apiBaseUrl = appConfig.ApiBaseUrl;
#if ANDROID && DEBUG
		// Android can't reach the host's "localhost" directly.
		// Emulator: 10.0.2.2 routes to the host PC.
		// Physical device via USB: use "adb reverse tcp:5162 tcp:5162" then 127.0.0.1 works.
		// Both cases use HTTP on port 5162 to avoid dev certificate issues.
		// Note: Use 127.0.0.1 instead of "localhost" because SocketsHttpHandler on some
		// Android devices doesn't resolve "localhost" to the loopback address.
		if (apiBaseUrl.Contains("localhost"))
		{
			var isEmulator = Android.OS.Build.Hardware == "ranchu" || Android.OS.Build.Hardware == "goldfish";
			var host = isEmulator ? "10.0.2.2" : "127.0.0.1";
			apiBaseUrl = apiBaseUrl
				.Replace("localhost", host)
				.Replace("https://", "http://")
				.Replace(":7173", ":5162");
		}
#endif
		var isLocalDev = apiBaseUrl.Contains("localhost") || apiBaseUrl.Contains("127.0.0.1") || apiBaseUrl.Contains("10.0.2.2");

		builder.Services.AddTransient<AuthDelegatingHandler>();
		var mobileApiKey = builder.Configuration["MobileApiKey"];
		var httpClientBuilder = builder.Services.AddHttpClient("MusicSalesApi", client =>
		{
			client.BaseAddress = new Uri(apiBaseUrl);
			// Required for ngrok free tier to skip the browser interstitial page
			client.DefaultRequestHeaders.Add("ngrok-skip-browser-warning", "true");
			// API key for mobile-only endpoints
			if (!string.IsNullOrEmpty(mobileApiKey))
				client.DefaultRequestHeaders.Add("X-Api-Key", mobileApiKey);
		})
		.AddHttpMessageHandler<AuthDelegatingHandler>();

		var audioDownloadClientBuilder = builder.Services.AddHttpClient(AudioCacheService.AudioDownloadClientName, client =>
		{
			client.Timeout = TimeSpan.FromSeconds(20);
		});
#if ANDROID
		audioDownloadClientBuilder.ConfigurePrimaryHttpMessageHandler(() => new Xamarin.Android.Net.AndroidMessageHandler());
#endif

		if (isLocalDev)
		{
#if DEBUG
			// For localhost, use SocketsHttpHandler to bypass dev certificate validation
			httpClientBuilder.ConfigurePrimaryHttpMessageHandler(() =>
			{
				return new SocketsHttpHandler
				{
					SslOptions = new System.Net.Security.SslClientAuthenticationOptions
					{
						RemoteCertificateValidationCallback = (_, _, _, _) => true
					}
				};
			});
#endif
		}
		else
		{
#if ANDROID
			// For remote servers (Cloudflare, etc.), use Android's native handler
			// which correctly negotiates TLS. SocketsHttpHandler (the HttpClientFactory
			// default) uses managed TLS that can fail with Cloudflare.
			httpClientBuilder.ConfigurePrimaryHttpMessageHandler(() => new Xamarin.Android.Net.AndroidMessageHandler());
#endif
		}

		// Register IConfiguration as a singleton (already available via builder.Configuration,
		// but this makes it injectable via DI throughout the app)
		builder.Services.AddSingleton<IConfiguration>(builder.Configuration);

		// Register centralized app config (resolves UseLocalHost / DavidTest / Production URLs once)
		builder.Services.AddSingleton<IAppConfig>(appConfig);
		builder.Services.AddSingleton<ITestingServerBannerService, TestingServerBannerService>();

		// Register services
		builder.Services.AddSingleton<IConnectivity>(Connectivity.Current);
		builder.Services.AddSingleton<IAppPreferenceStore, AppPreferenceStore>();
		builder.Services.AddSingleton<IPermissionExplainerService, PermissionExplainerService>();
		builder.Services.AddSingleton<IMicrophonePermissionService, MicrophonePermissionService>();
		builder.Services.AddSingleton<IAuthService, AuthService>();
		builder.Services.AddSingleton<IWebAuthenticatorService, WebAuthenticatorService>();
		builder.Services.AddSingleton<IAppSettingsService, AppSettingsService>();
		builder.Services.AddSingleton<IMusicService, MusicService>();
		builder.Services.AddSingleton<IAudioCacheService, AudioCacheService>();
		builder.Services.AddSingleton<IAlertService, AlertService>();
		builder.Services.AddSingleton<ISignalRService, SignalRService>();
		builder.Services.AddSingleton<ISignalRConnectionManager, SignalRConnectionManager>();
		builder.Services.AddSingleton<IAppActivationCoordinator, AppActivationCoordinator>();
		builder.Services.AddSingleton<IAdminMessageApiService, AdminMessageApiService>();
		builder.Services.AddSingleton<IAdminMessageCoordinator, AdminMessageCoordinator>();
		builder.Services.AddSingleton<INavigationService, NavigationService>();
		builder.Services.AddSingleton<IMediaManager>(CrossMediaManager.Current);
	#if ANDROID
		builder.Services.AddSingleton<IPlaybackKeepAliveService, MusicSalesApp.Maui.Platforms.Android.PlaybackKeepAliveService>();
	#else
		builder.Services.AddSingleton<IPlaybackKeepAliveService, NoOpPlaybackKeepAliveService>();
	#endif
		builder.Services.AddSingleton<IPlaybackService, PlaybackService>();
	#if ANDROID
		builder.Services.AddSingleton<IMediaPlaybackOnboardingService, MediaPlaybackOnboardingService>();
		builder.Services.AddSingleton<IAudioVisualizerService, MusicSalesApp.Maui.Platforms.Android.AudioVisualizerService>();
	#else
		builder.Services.AddSingleton<IMediaPlaybackOnboardingService, NoOpMediaPlaybackOnboardingService>();
		builder.Services.AddSingleton<IAudioVisualizerService, NoAudioVisualizerService>();
	#endif
		builder.Services.AddSingleton<IBrowserService, BrowserService>();
		builder.Services.AddSingleton<IPlaylistService, PlaylistService>();
		builder.Services.AddSingleton<IContactApiService, ContactApiService>();
		builder.Services.AddSingleton<ITipApiService, TipApiService>();
		builder.Services.AddSingleton<ITipAmountPicker, TipAmountPicker>();
		builder.Services.AddSingleton<ITipFlowHandler, TipFlowHandler>();
		builder.Services.AddSingleton<IAddToPlaylistHandler, AddToPlaylistHandler>();
			// Initialize Plugin.MediaManager with the platform context
			builder.ConfigureLifecycleEvents(events =>
			{
#if ANDROID
				events.AddAndroid(android =>
				{
					android.OnCreate((activity, _) => CrossMediaManager.Current.Init(activity));
					android.OnStop(activity =>
					{
						if (IPlatformApplication.Current?.Services.GetService(typeof(IAudioVisualizerService)) is IAudioVisualizerService audioVisualizerService)
						{
							audioVisualizerService.Suspend();
						}
					});
					android.OnResume(activity =>
					{
						if (IPlatformApplication.Current?.Services.GetService(typeof(IAudioVisualizerService)) is IAudioVisualizerService audioVisualizerService)
						{
							_ = audioVisualizerService.EnsureInitializedAsync();
						}

						if (IPlatformApplication.Current?.Services.GetService(typeof(IAppActivationCoordinator)) is IAppActivationCoordinator appActivationCoordinator)
						{
							_ = appActivationCoordinator.HandleActivationAsync();
						}
					});
				});
#elif IOS
				events.AddiOS(ios =>
				{
					ios.FinishedLaunching((app, _) =>
					{
						CrossMediaManager.Current.Init();
						return true;
					});

					ios.OnActivated(app =>
					{
						if (IPlatformApplication.Current?.Services.GetService(typeof(IAppActivationCoordinator)) is IAppActivationCoordinator appActivationCoordinator)
						{
							_ = appActivationCoordinator.HandleActivationAsync();
						}
					});
				});
#endif
			});
		// Register platform-specific services
#if ANDROID
		builder.Services.AddSingleton<IBillingService, MusicSalesApp.Maui.Platforms.Android.GooglePlayBillingService>();
#elif IOS
		builder.Services.AddSingleton<IBillingService, MusicSalesApp.Maui.Platforms.iOS.AppStoreBillingService>();
#else
		// Non-Android platforms: register a no-op billing service
		builder.Services.AddSingleton<IBillingService, NoBillingService>();
#endif

		// Register ViewModels and Pages
		builder.Services.AddTransient<HomeViewModel>();
		builder.Services.AddTransient<HomePage>();
		builder.Services.AddTransient<MusicLibraryViewModel>();
		builder.Services.AddTransient<MusicLibraryPage>();
		builder.Services.AddTransient<LoginViewModel>();
		builder.Services.AddTransient<LoginPage>();
		builder.Services.AddTransient<RegisterViewModel>();
		builder.Services.AddTransient<RegisterPage>();
		builder.Services.AddTransient<VerifyEmailViewModel>();
		builder.Services.AddTransient<VerifyEmailPage>();
		builder.Services.AddTransient<ForgotPasswordViewModel>();
		builder.Services.AddTransient<ForgotPasswordPage>();
		builder.Services.AddTransient<ResetPasswordViewModel>();
		builder.Services.AddTransient<ResetPasswordPage>();
		builder.Services.AddTransient<SongPlayerViewModel>();
		builder.Services.AddTransient<SongPlayerPage>();
		builder.Services.AddTransient<PersonaViewModel>();
		builder.Services.AddTransient<PersonaPage>();
		builder.Services.AddTransient<AccountSettingsViewModel>();
		builder.Services.AddTransient<AccountSettingsPage>();
		builder.Services.AddTransient<PolicyViewModel>();
		builder.Services.AddTransient<PolicyPage>();
		builder.Services.AddTransient<PlaylistPlayerViewModel>();
		builder.Services.AddTransient<PlaylistPlayerPage>();
		builder.Services.AddTransient<MyPlaylistsViewModel>();
		builder.Services.AddTransient<MyPlaylistsPage>();
		builder.Services.AddTransient<ContactUsViewModel>();
		builder.Services.AddTransient<ContactUsPage>();

		builder.Logging.AddProvider(new RollingFileLoggerProvider(RollingFileLoggerOptions.CreateDefault()));

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
