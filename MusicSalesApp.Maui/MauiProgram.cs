using System.Reflection;
using CommunityToolkit.Maui;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MusicSalesApp.Maui;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.UseMauiCommunityToolkit()
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
		builder.Services.AddHttpClient("MusicSalesApi", client =>
		{
			client.BaseAddress = new Uri(apiBaseUrl);
		});

		// Register IConfiguration as a singleton (already available via builder.Configuration,
		// but this makes it injectable via DI throughout the app)
		builder.Services.AddSingleton<IConfiguration>(builder.Configuration);

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
