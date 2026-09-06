using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using MusicSalesApp.Common.Helpers;

namespace MusicSalesApp.Maui.Services;

/// <summary>
/// The account-level notification preferences, as the app sees them.
/// </summary>
/// <remarks>
/// These live on the account rather than on the device: they follow the listener to a new phone,
/// and the server is what acts on them. In particular the frequency is enforced by the dispatcher
/// before it sends - see <see cref="ArtistPushFrequency"/> - so this is genuinely a request to the
/// server, not a local setting the app could keep to itself.
/// </remarks>
public interface INotificationPreferenceApiService
{
    /// <summary>The caller's preferences, or null when they could not be read.</summary>
    Task<NotificationPreferences?> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>Saves them. False when the server did not accept the change.</summary>
    Task<bool> SetAsync(NotificationPreferences preferences, CancellationToken cancellationToken = default);
}

/// <summary>
/// Mirrors the server's ArtistNotificationPreferences. Property names must match, because they are
/// bound by name over JSON.
/// </summary>
public sealed class NotificationPreferences
{
    public bool ReceiveArtistReleaseEmails { get; set; }

    public bool ReceiveArtistMessageEmails { get; set; }

    public bool ReceiveArtistReleasePush { get; set; }

    public bool ReceiveArtistMessagePush { get; set; }

    public ArtistPushFrequency ArtistPushFrequency { get; set; }
}

/// <inheritdoc />
public class NotificationPreferenceApiService : INotificationPreferenceApiService
{
    private const string PreferencesPath = "api/mobile/follows/notification-preferences";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<NotificationPreferenceApiService> _logger;

    public NotificationPreferenceApiService(
        IHttpClientFactory httpClientFactory,
        ILogger<NotificationPreferenceApiService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<NotificationPreferences?> GetAsync(CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("MusicSalesApi");

        try
        {
            var response = await client.GetAsync(PreferencesPath, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // 401 is ordinary here - the settings page is reachable signed out - so this is a
                // null rather than a warning the log would fill up with.
                return null;
            }

            return await response.Content.ReadFromJsonAsync<NotificationPreferences>(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read the notification preferences.");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<bool> SetAsync(
        NotificationPreferences preferences,
        CancellationToken cancellationToken = default)
    {
        if (preferences is null)
        {
            return false;
        }

        var client = _httpClientFactory.CreateClient("MusicSalesApi");

        try
        {
            var response = await client.PutAsJsonAsync(PreferencesPath, preferences, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not save the notification preferences.");
            return false;
        }
    }
}
