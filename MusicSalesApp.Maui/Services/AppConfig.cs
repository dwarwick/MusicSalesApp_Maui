using Microsoft.Extensions.Configuration;

namespace MusicSalesApp.Maui.Services;

/// <summary>
/// Resolves app configuration once at startup.
/// 
/// Resolution logic:
///   1. If "UseLocalHost" is missing or true → use top-level "ApiBaseUrl" (localhost dev)
///   2. If "UseLocalHost" is false → use "DavidTest:ApiBaseUrl" (remote test server)
///   3. In Production ("UseLocalHost" absent, top-level "ApiBaseUrl" set to production URL)
///      → use top-level "ApiBaseUrl" directly
///
/// WebBaseUrl always equals the resolved ApiBaseUrl (they share the same domain).
/// </summary>
public class AppConfig : IAppConfig
{
    public string ApiBaseUrl { get; }
    public string WebBaseUrl { get; }

    public AppConfig(IConfiguration configuration)
    {
        // UseLocalHost defaults to true when missing (Production won't have it)
        var useLocalHost = configuration.GetValue<bool?>("UseLocalHost");

        string resolvedUrl;
        if (useLocalHost == false)
        {
            // Explicitly set to false → use DavidTest section
            resolvedUrl = configuration["DavidTest:ApiBaseUrl"]
                ?? configuration["ApiBaseUrl"]
                ?? "https://streamtunes.net";
        }
        else
        {
            // true or missing (Production) → use top-level ApiBaseUrl
            resolvedUrl = configuration["ApiBaseUrl"] ?? "https://streamtunes.net";
        }

        ApiBaseUrl = resolvedUrl.TrimEnd('/');
        WebBaseUrl = ApiBaseUrl;

        System.Diagnostics.Debug.WriteLine($"[AppConfig] UseLocalHost={useLocalHost}, ApiBaseUrl={ApiBaseUrl}, WebBaseUrl={WebBaseUrl}");
        Console.WriteLine($"[AppConfig] UseLocalHost={useLocalHost}, ApiBaseUrl={ApiBaseUrl}, WebBaseUrl={WebBaseUrl}");
    }
}
