using System.Reflection;
using CommunityToolkit.Maui;
#if !ANDROID
using MediaManager;
#endif
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;
using Microsoft.Maui.Networking;
using Microsoft.Maui.Storage;
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
		builder.Services.AddSingleton<INetworkStatusService, NetworkStatusService>();
		builder.Services.AddSingleton<IAppPreferenceStore, AppPreferenceStore>();
		builder.Services.AddSingleton<ISecureStorage>(SecureStorage.Default);
		builder.Services.AddSingleton<IAnonymousFeaturedStreamStore, AnonymousFeaturedStreamStore>();
		builder.Services.AddSingleton<IUserStreamedSongStore, UserStreamedSongStore>();
		builder.Services.AddSingleton<IPermissionExplainerService, PermissionExplainerService>();
		builder.Services.AddSingleton<IMicrophonePermissionService, MicrophonePermissionService>();
		// Biometric sign-in. The Android adapter wraps the BiometricPrompt helper that has shipped
		// all along; the Apple one is LocalAuthentication. Anywhere else answers "not supported",
		// which is exactly what AuthService used to hard-code off Android.
#if ANDROID
		builder.Services.AddSingleton<IBiometricAuthenticator, MusicSalesApp.Maui.Platforms.Android.AndroidBiometricAuthenticator>();
#elif IOS
		builder.Services.AddSingleton<IBiometricAuthenticator, AppleBiometricAuthenticator>();
#else
		builder.Services.AddSingleton<IBiometricAuthenticator, UnsupportedBiometricAuthenticator>();
#endif
		// Sign in with Apple. Native ASAuthorizationController on iOS; everywhere else answers
		// "not supported" so the button is simply not offered.
#if IOS
		builder.Services.AddSingleton<IAppleSignInService, AppleSignInService>();
#else
		builder.Services.AddSingleton<IAppleSignInService, UnsupportedAppleSignInService>();
#endif
		builder.Services.AddSingleton<IAuthService, AuthService>();
		builder.Services.AddSingleton<IWebAuthenticatorService, WebAuthenticatorService>();
		builder.Services.AddSingleton<IAppSettingsService, AppSettingsService>();
		builder.Services.AddSingleton<IOfflineCacheSettingsService, OfflineCacheSettingsService>();
		builder.Services.AddSingleton<IAutoScrollSettingsService, AutoScrollSettingsService>();
		builder.Services.AddSingleton<IOfflineSongCatalogStore, OfflineSongCatalogStore>();
		// MusicService is registered concretely so OfflineAwareMusicService can decorate it. Every
		// consumer resolves IMusicService and therefore gets the offline fallback for free.
		builder.Services.AddSingleton<MusicService>();
		builder.Services.AddSingleton<IMusicService>(services => new OfflineAwareMusicService(
			services.GetRequiredService<MusicService>(),
			services.GetRequiredService<IOfflineSongCatalogStore>(),
			services.GetRequiredService<ITrackCacheService>(),
			services.GetRequiredService<IConnectivity>(),
			services.GetRequiredService<ILogger<OfflineAwareMusicService>>(),
			services.GetRequiredService<IImageCacheService>()));
		builder.Services.AddSingleton<IImageCacheService, ImageCacheService>();

		// Lyric timings. Fetched on demand and cached beside the audio and artwork, so a downloaded
		// song stays fully usable offline rather than losing its lyrics the moment the signal does.
		builder.Services.AddSingleton<ILyricsService, LyricsService>();
		builder.Services.AddSingleton<ISongArtworkHydrator, SongArtworkHydrator>();
		// The platform cache is registered concretely and wrapped, so a song's artwork is downloaded at
		// exactly the same moment its audio is - no separate trigger to keep in sync.
	#if ANDROID
		builder.Services.AddSingleton<MusicSalesApp.Maui.Platforms.Android.AndroidMedia3AudioCacheService>();
		builder.Services.AddSingleton<IAudioCacheService>(services => new ArtworkCachingAudioCacheService(
			services.GetRequiredService<MusicSalesApp.Maui.Platforms.Android.AndroidMedia3AudioCacheService>(),
			services.GetRequiredService<IImageCacheService>(),
			services.GetRequiredService<ILogger<ArtworkCachingAudioCacheService>>(),
			services.GetRequiredService<INetworkStatusService>(),
			services.GetRequiredService<ILyricsService>()));
	#else
		builder.Services.AddSingleton<AudioCacheService>();
		builder.Services.AddSingleton<IAudioCacheService>(services => new ArtworkCachingAudioCacheService(
			services.GetRequiredService<AudioCacheService>(),
			services.GetRequiredService<IImageCacheService>(),
			services.GetRequiredService<ILogger<ArtworkCachingAudioCacheService>>(),
			services.GetRequiredService<INetworkStatusService>(),
			services.GetRequiredService<ILyricsService>()));
	#endif
		builder.Services.AddSingleton<ITrackCacheService>(services => services.GetRequiredService<IAudioCacheService>());
		builder.Services.AddSingleton<IQueuePreparationService, QueuePreparationService>();
		builder.Services.AddSingleton<IAlertService, AlertService>();
		builder.Services.AddSingleton<ISignalRService, SignalRService>();
		builder.Services.AddSingleton<ISignalRConnectionManager, SignalRConnectionManager>();
		builder.Services.AddSingleton<IAppActivationCoordinator, AppActivationCoordinator>();
		builder.Services.AddSingleton<IAdminMessageApiService, AdminMessageApiService>();
		builder.Services.AddSingleton<IAdminMessageCoordinator, AdminMessageCoordinator>();

		// Push notifications. Android goes through Firebase Cloud Messaging; iOS registers with
		// APNs natively and the SERVER talks to Apple directly, so there is deliberately no
		// Firebase SDK in the iOS head - it already carries App Store launch-crash workarounds
		// around static registration and LLVM AOT, and a large native SDK is what reopens those.
		// Windows and Mac Catalyst get the no-op, so no calling code branches on platform.
		builder.Services.AddSingleton<IPushApiService, PushApiService>();
#if ANDROID
		builder.Services.AddSingleton<IPushRegistrationService, MusicSalesApp.Maui.Platforms.Android.AndroidPushRegistrationService>();
		builder.Services.AddSingleton<IPushNotificationCoordinator, PushNotificationCoordinator>();
#elif IOS
		builder.Services.AddSingleton<IPushRegistrationService, MusicSalesApp.Maui.Platforms.iOS.ApplePushRegistrationService>();
		builder.Services.AddSingleton<IPushNotificationCoordinator, PushNotificationCoordinator>();
#else
		builder.Services.AddSingleton<IPushRegistrationService, NoPushRegistrationService>();
		builder.Services.AddSingleton<IPushNotificationCoordinator, NoPushNotificationCoordinator>();
#endif
		builder.Services.AddSingleton<INavigationService, NavigationService>();
	#if ANDROID
		builder.Services.AddSingleton<IPlatformPlaybackRuntime, MusicSalesApp.Maui.Platforms.Android.AndroidMedia3PlaybackRuntime>();
	#else
		// Plugin.MediaManager's Apple notification manager takes lock-screen artwork from
		// IMediaItem.Image - a decoded UIImage - and never from ImageUri, and nothing populates it for
		// the Play(IMediaItem) overloads MediaManagerPlaybackRuntime uses. This fills that gap.
	#if IOS || MACCATALYST
		builder.Services.AddSingleton<INowPlayingArtworkLoader, AppleNowPlayingArtworkLoader>();
		// Also swaps the lock screen's 10-second skip buttons for previous/next track, and reports
		// transport-control pauses so they are not mistaken for a stall and "recovered" by a restart.
		builder.Services.AddSingleton<IPlaybackRemoteCommandBridge, AppleRemoteCommandBridge>();
	#else
		builder.Services.AddSingleton<INowPlayingArtworkLoader, NoOpNowPlayingArtworkLoader>();
		builder.Services.AddSingleton<IPlaybackRemoteCommandBridge, NoOpPlaybackRemoteCommandBridge>();
	#endif
		builder.Services.AddSingleton<NowPlayingArtworkCoordinator>();
		builder.Services.AddSingleton<IMediaManager>(CrossMediaManager.Current);
		builder.Services.AddSingleton<IPlatformPlaybackRuntime, MediaManagerPlaybackRuntime>();
	#endif
	#if ANDROID
		builder.Services.AddSingleton<IPlaybackKeepAliveService, MusicSalesApp.Maui.Platforms.Android.PlaybackKeepAliveService>();
	#else
		builder.Services.AddSingleton<IPlaybackKeepAliveService, NoOpPlaybackKeepAliveService>();
	#endif
		builder.Services.AddSingleton<IPlaybackService, PlaybackService>();
		builder.Services.AddSingleton<IToastService, MusicSalesApp.Maui.Notifications.ToolkitToastService>();
		builder.Services.AddSingleton<PlaybackFailureNotificationCoordinator>();
		builder.Services.AddSingleton<IAudioVisualizerLifecycleCoordinator, AudioVisualizerLifecycleCoordinator>();
	#if ANDROID
		builder.Services.AddSingleton<IMediaPlaybackOnboardingService, MediaPlaybackOnboardingService>();
		builder.Services.AddSingleton<IAudioVisualizerService, MusicSalesApp.Maui.Platforms.Android.AudioVisualizerService>();
	#else
		builder.Services.AddSingleton<IMediaPlaybackOnboardingService, NoOpMediaPlaybackOnboardingService>();
		builder.Services.AddSingleton<IAudioVisualizerService, NoAudioVisualizerService>();
	#endif
		builder.Services.AddSingleton<IBrowserService, BrowserService>();
		builder.Services.AddSingleton<IOfflinePlaylistStore, OfflinePlaylistStore>();
		// Same decorator shape as IMusicService: every playlist consumer gets offline support with no
		// call-site changes. Registered as its own singleton too, so ViewModels can read LastPlaylistSource.
		builder.Services.AddSingleton<PlaylistService>();
		builder.Services.AddSingleton(services => new OfflineAwarePlaylistService(
			services.GetRequiredService<PlaylistService>(),
			services.GetRequiredService<IOfflinePlaylistStore>(),
			services.GetRequiredService<ITrackCacheService>(),
			services.GetRequiredService<IConnectivity>(),
			services.GetRequiredService<ILogger<OfflineAwarePlaylistService>>()));
		builder.Services.AddSingleton<IPlaylistService>(services => services.GetRequiredService<OfflineAwarePlaylistService>());
		builder.Services.AddSingleton<IPlaylistDataSourceReporter>(services => services.GetRequiredService<OfflineAwarePlaylistService>());
		builder.Services.AddSingleton<IContactApiService, ContactApiService>();
		builder.Services.AddSingleton<ITipApiService, TipApiService>();
		builder.Services.AddSingleton<ITipAmountPicker, TipAmountPicker>();
		builder.Services.AddSingleton<ITipFlowHandler, TipFlowHandler>();
		builder.Services.AddSingleton<IAddToPlaylistHandler, AddToPlaylistHandler>();
			// Configure platform lifecycle hooks.
			builder.ConfigureLifecycleEvents(events =>
			{
#if ANDROID
				events.AddAndroid(android =>
				{
					android.OnStop(activity =>
					{
						if (IPlatformApplication.Current?.Services.GetService(typeof(IAudioVisualizerLifecycleCoordinator)) is IAudioVisualizerLifecycleCoordinator lifecycleCoordinator)
						{
							lifecycleCoordinator.OnApplicationStopped();
						}
					});
					android.OnResume(activity =>
					{
						if (IPlatformApplication.Current?.Services.GetService(typeof(IAudioVisualizerLifecycleCoordinator)) is IAudioVisualizerLifecycleCoordinator lifecycleCoordinator)
						{
							lifecycleCoordinator.OnApplicationResumed();
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
		builder.Services.AddTransient<AccountSettingsViewModel>();
		builder.Services.AddTransient<AccountSettingsPage>();
		builder.Services.AddTransient<ConfigViewModel>();
		builder.Services.AddTransient<ConfigPage>();
		builder.Services.AddTransient<PolicyViewModel>();
		builder.Services.AddTransient<PolicyPage>();
		builder.Services.AddTransient<PlaylistPlayerViewModel>();
		builder.Services.AddTransient<PlaylistPlayerPage>();
		builder.Services.AddTransient<MyPlaylistsViewModel>();
		builder.Services.AddTransient<MyPlaylistsPage>();
		builder.Services.AddTransient<ContactUsViewModel>();
		builder.Services.AddTransient<ContactUsPage>();

		builder.Logging.AddProvider(new RollingFileLoggerProvider(RollingFileLoggerOptions.CreateDefault()));

#if ANDROID
		builder.Logging.AddProvider(new MusicSalesApp.Maui.Platforms.Android.AndroidLogcatLoggerProvider(
#if DEBUG
			LogLevel.Information));
#else
			LogLevel.Warning));
#endif
#endif

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
