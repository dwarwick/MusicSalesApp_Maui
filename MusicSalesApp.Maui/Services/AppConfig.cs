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
    private const string ProductionFallbackUrl = "https://streamtunes.net";

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
            resolvedUrl = FirstNonEmpty(
                configuration["DavidTest:ApiBaseUrl"],
                configuration["ApiBaseUrl"],
                ProductionFallbackUrl);
        }
        else
        {
            // true or missing (Production) → use top-level ApiBaseUrl
            resolvedUrl = FirstNonEmpty(
                configuration["ApiBaseUrl"],
                ProductionFallbackUrl);
        }

        ApiBaseUrl = resolvedUrl.TrimEnd('/');
        WebBaseUrl = ApiBaseUrl;

        System.Diagnostics.Debug.WriteLine($"[AppConfig] UseLocalHost={useLocalHost}, ApiBaseUrl={ApiBaseUrl}, WebBaseUrl={WebBaseUrl}");
        Console.WriteLine($"[AppConfig] UseLocalHost={useLocalHost}, ApiBaseUrl={ApiBaseUrl}, WebBaseUrl={WebBaseUrl}");
    }

    internal static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }
}
