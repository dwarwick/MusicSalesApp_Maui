using System.Reflection;
using CommunityToolkit.Maui;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.ViewModels;
using MusicSalesApp.Maui.Views;

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
			.UseMauiCommunityToolkitMediaElement(isAndroidForegroundServiceEnabled: false)
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

		// Load configuration from embedded appsettings JSON files
		var assembly = Assembly.GetExecutingAssembly();
		builder.Configuration.AddJsonStream(
			assembly.GetManifestResourceStream("MusicSalesApp.Maui.appsettings.json")!);

		var environment = "Development";
#if RELEASE
		environment = "Production";
#endif
		var envStream = assembly.GetManifestResourceStream($"MusicSalesApp.Maui.appsettings.{environment}.json");
		if (envStream is not null)
		{
			builder.Configuration.AddJsonStream(envStream);
		}

		// Register HttpClientFactory with the API base URL
		var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? "https://localhost:7173";
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
		builder.Services.AddTransient<AuthDelegatingHandler>();
		var mobileApiKey = builder.Configuration["MobileApiKey"];
		builder.Services.AddHttpClient("MusicSalesApi", client =>
		{
			client.BaseAddress = new Uri(apiBaseUrl);
			// Required for ngrok free tier to skip the browser interstitial page
			client.DefaultRequestHeaders.Add("ngrok-skip-browser-warning", "true");
			// API key for mobile-only endpoints
			if (!string.IsNullOrEmpty(mobileApiKey))
				client.DefaultRequestHeaders.Add("X-Api-Key", mobileApiKey);
		})
		.AddHttpMessageHandler<AuthDelegatingHandler>()
#if DEBUG
		.ConfigurePrimaryHttpMessageHandler(() =>
		{
			// Use SocketsHttpHandler to bypass Android's default AndroidMessageHandler,
			// which doesn't support custom certificate validation.
			return new SocketsHttpHandler
			{
				SslOptions = new System.Net.Security.SslClientAuthenticationOptions
				{
					// Accept any certificate in debug builds (ngrok, dev certs, etc.)
					RemoteCertificateValidationCallback = (_, _, _, _) => true
				}
			};
		})
#endif
		;

		// Register IConfiguration as a singleton (already available via builder.Configuration,
		// but this makes it injectable via DI throughout the app)
		builder.Services.AddSingleton<IConfiguration>(builder.Configuration);

		// Register services
		builder.Services.AddSingleton<IAuthService, AuthService>();
		builder.Services.AddSingleton<IMusicService, MusicService>();
		builder.Services.AddSingleton<IAlertService, AlertService>();
		builder.Services.AddSingleton<ISignalRService, SignalRService>();
		builder.Services.AddSingleton<INavigationService, NavigationService>();

		// Register ViewModels and Pages
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

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
