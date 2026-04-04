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
		// Android emulator can't reach host's "localhost" — use 10.0.2.2 instead.
		// Also use HTTP to avoid dev certificate issues on the emulator.
		// Skip remapping when using a tunnel URL (e.g. ngrok).
		if (apiBaseUrl.Contains("localhost"))
		{
			apiBaseUrl = apiBaseUrl
				.Replace("localhost", "10.0.2.2")
				.Replace("https://", "http://")
				.Replace(":7173", ":5162");
		}
#endif
		builder.Services.AddHttpClient("MusicSalesApi", client =>
		{
			client.BaseAddress = new Uri(apiBaseUrl);
			// Required for ngrok free tier to skip the browser interstitial page
			client.DefaultRequestHeaders.Add("ngrok-skip-browser-warning", "true");
		})
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
		builder.Services.AddSingleton<IMusicService, MusicService>();

		// Register ViewModels and Pages
		builder.Services.AddTransient<MusicLibraryViewModel>();
		builder.Services.AddTransient<MusicLibraryPage>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
