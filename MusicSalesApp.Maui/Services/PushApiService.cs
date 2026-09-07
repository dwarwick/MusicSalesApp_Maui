using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace MusicSalesApp.Maui.Services;

/// <summary>
/// Tells the server which device this is.
/// </summary>
public interface IPushApiService
{
    /// <summary>
    /// Registers, or re-registers, this device's token.
    /// </summary>
    /// <returns>
    /// True when the server accepted it. False for a refusal the client should NOT retry - the
    /// token was malformed or the platform unknown - which is a permanent condition the server
    /// signals with a 400.
    /// </returns>
    Task<PushRegistrationOutcome> RegisterDeviceAsync(string platform, string token, string deviceId);

    Task<bool> UnregisterDeviceAsync(string token);
}

/// <summary>
/// What the server said.
/// </summary>
/// <remarks>
/// Three values rather than a bool because the caller has to tell "done" from "try again" from
/// "never going to work" - retrying a rejected token forever is the failure mode this avoids, and
/// giving up on a network blip is the one on the other side of it.
/// </remarks>
public enum PushRegistrationOutcome
{
    Registered,

    /// <summary>Transient - offline, a timeout, or a 5xx. Worth another attempt later.</summary>
    Deferred,

    /// <summary>The server refused, permanently. Retrying cannot help.</summary>
    Rejected,
}

/// <inheritdoc />
public class PushApiService : IPushApiService
{
    private const string DevicesPath = "api/mobile/push/devices";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PushApiService> _logger;

    public PushApiService(IHttpClientFactory httpClientFactory, ILogger<PushApiService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<PushRegistrationOutcome> RegisterDeviceAsync(string platform, string token, string deviceId)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return PushRegistrationOutcome.Rejected;
        }

        var client = _httpClientFactory.CreateClient("MusicSalesApi");

        try
        {
            var response = await client.PutAsJsonAsync(
                DevicesPath,
                new { Platform = platform, Token = token, DeviceId = deviceId });

            if (response.IsSuccessStatusCode)
            {
                return PushRegistrationOutcome.Registered;
            }

            // A 401 is transient from here: the token expired or the app is mid-sign-in, and the
            // coordinator will try again on the next auth change.
            if (response.StatusCode == HttpStatusCode.Unauthorized
                || (int)response.StatusCode >= 500)
            {
                _logger.LogWarning("Push device registration deferred ({Status}).", response.StatusCode);
                return PushRegistrationOutcome.Deferred;
            }

            _logger.LogWarning("Push device registration refused ({Status}).", response.StatusCode);
            return PushRegistrationOutcome.Rejected;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to register the push device.");
            return PushRegistrationOutcome.Deferred;
        }
    }

    /// <inheritdoc />
    public async Task<bool> UnregisterDeviceAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var client = _httpClientFactory.CreateClient("MusicSalesApi");

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete, DevicesPath)
            {
                Content = JsonContent.Create(new { Token = token }),
            };

            var response = await client.SendAsync(request);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to unregister the push device.");
            return false;
        }
    }
}
