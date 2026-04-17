namespace MusicSalesApp.Maui.Services;

/// <summary>
/// Provides resolved app configuration values.
/// Centralizes the UseLocalHost / DavidTest / Production override logic
/// so every consumer gets the correct URLs without duplicating resolution.
/// </summary>
public interface IAppConfig
{
    /// <summary>
    /// The base URL for API calls (e.g. https://davidtest.dev or https://localhost:7173).
    /// </summary>
    string ApiBaseUrl { get; }

    /// <summary>
    /// The public web URL used for sharing links (e.g. https://davidtest.dev or https://streamtunes.net).
    /// This is always the externally-accessible URL, never localhost.
    /// </summary>
    string WebBaseUrl { get; }
}
