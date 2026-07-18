using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace MusicSalesApp.Maui.Services;

public class AppSettingsService : IAppSettingsService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AppSettingsService> _logger;

    private const int DefaultStreamQualifyingSeconds = 30;

    private MobileSettingsDto? _cached;

    public AppSettingsService(IHttpClientFactory httpClientFactory, ILogger<AppSettingsService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<int> GetStreamQualifyingSecondsAsync()
    {
        var settings = await FetchSettingsAsync();
        return settings?.StreamQualifyingSeconds ?? DefaultStreamQualifyingSeconds;
    }

    private async Task<MobileSettingsDto?> FetchSettingsAsync()
    {
        if (_cached is not null)
            return _cached;

        var client = _httpClientFactory.CreateClient("MusicSalesApi");
        try
        {
            _cached = await client.GetFromJsonAsync<MobileSettingsDto>("api/mobile-settings");
            return _cached;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch mobile settings, using defaults");
            return null;
        }
    }

    internal sealed record MobileSettingsDto(int StreamQualifyingSeconds);
}
