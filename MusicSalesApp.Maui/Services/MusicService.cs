using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Services;

public class MusicService : IMusicService
{
    private const string SongsRequestPath = "api/music/songs";
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IAppSettingsService _appSettingsService;
    private readonly ILogger<MusicService> _logger;

    public string? LastSongsError { get; private set; }

    public MusicService(IHttpClientFactory httpClientFactory, IAppSettingsService appSettingsService, ILogger<MusicService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _appSettingsService = appSettingsService;
        _logger = logger;
    }

    public async Task<List<SongDto>> GetSongsAsync()
    {
        var client = _httpClientFactory.CreateClient("MusicSalesApi");
        LastSongsError = null;

        try
        {
            var response = await client.GetAsync(SongsRequestPath);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                LastSongsError = ApiErrorMessageFormatter.FormatRequestFailure(
                    client.BaseAddress,
                    SongsRequestPath,
                    response.StatusCode,
                    responseBody);

                _logger.LogWarning(
                    "Failed to fetch songs from {RequestUri}: {StatusCode} {ResponseBody}",
                    new Uri(client.BaseAddress ?? new Uri("https://localhost/"), SongsRequestPath),
                    response.StatusCode,
                    responseBody);

                return [];
            }

            try
            {
                return JsonSerializer.Deserialize<List<SongDto>>(responseBody, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];
            }
            catch (JsonException ex)
            {
                LastSongsError = ApiErrorMessageFormatter.FormatException(client.BaseAddress, SongsRequestPath, ex);
                _logger.LogError(ex, "Failed to deserialize songs response from {RequestUri}. Body: {ResponseBody}",
                    new Uri(client.BaseAddress ?? new Uri("https://localhost/"), SongsRequestPath), responseBody);
                return [];
            }
        }
        catch (Exception ex)
        {
            LastSongsError = ApiErrorMessageFormatter.FormatException(client.BaseAddress, SongsRequestPath, ex);
            _logger.LogError(ex, "Failed to fetch songs from API");
            return [];
        }
    }

    public async Task<SongDto?> GetSongByTitleAsync(string title)
    {
        var client = _httpClientFactory.CreateClient("MusicSalesApi");
        try
        {
            var encoded = Uri.EscapeDataString(title);
            return await client.GetFromJsonAsync<SongDto>($"api/music/song-by-title/{encoded}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch song by title '{Title}'", title);
            return null;
        }
    }

    public Task<int> GetStreamQualifyingSecondsAsync()
    {
        return _appSettingsService.GetStreamQualifyingSecondsAsync();
    }

    public async Task RecordStreamAsync(int songMetadataId)
    {
        var client = _httpClientFactory.CreateClient("MusicSalesApi");
        try
        {
            await client.PostAsync($"api/music/stream/{songMetadataId}", null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record stream for song {SongMetadataId}", songMetadataId);
        }
    }

    public async Task<List<LikeCountDto>> GetBulkLikeCountsAsync(IEnumerable<int> songIds)
    {
        var client = _httpClientFactory.CreateClient("MusicSalesApi");
        try
        {
            var ids = string.Join(",", songIds);
            if (string.IsNullOrEmpty(ids)) return [];

            var result = await client.GetFromJsonAsync<List<LikeCountDto>>($"api/music/likes/bulk?ids={ids}");
            return result ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch bulk like counts");
            return [];
        }
    }

    public async Task<LikeToggleResult?> ToggleLikeAsync(int songMetadataId)
    {
        var client = _httpClientFactory.CreateClient("MusicSalesApi");
        try
        {
            var response = await client.PostAsync($"api/music/like/{songMetadataId}", null);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<LikeToggleResult>();
            }
            _logger.LogWarning("ToggleLike returned {StatusCode}", response.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to toggle like for song {SongMetadataId}", songMetadataId);
            return null;
        }
    }

    public async Task<LikeToggleResult?> ToggleDislikeAsync(int songMetadataId)
    {
        var client = _httpClientFactory.CreateClient("MusicSalesApi");
        try
        {
            var response = await client.PostAsync($"api/music/dislike/{songMetadataId}", null);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<LikeToggleResult>();
            }
            _logger.LogWarning("ToggleDislike returned {StatusCode}", response.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to toggle dislike for song {SongMetadataId}", songMetadataId);
            return null;
        }
    }

    public async Task<Dictionary<int, bool?>> GetBulkUserLikeStatusAsync(IEnumerable<int> songIds)
    {
        var client = _httpClientFactory.CreateClient("MusicSalesApi");
        try
        {
            var ids = string.Join(",", songIds);
            if (string.IsNullOrEmpty(ids)) return new();

            var result = await client.GetFromJsonAsync<List<UserLikeStatusDto>>($"api/music/likes/user-status?ids={ids}");
            if (result == null) return new();

            return result.ToDictionary(r => r.SongMetadataId, r => r.UserLikeStatus);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch bulk user like status");
            return new();
        }
    }

    private sealed record UserLikeStatusDto(int SongMetadataId, bool? UserLikeStatus);

    public async Task<(bool Success, string ErrorMessage)> VerifyGooglePlayPurchaseAsync(string purchaseToken, string? orderId)
    {
        var client = _httpClientFactory.CreateClient("MusicSalesApi");
        try
        {
            var payload = new { PurchaseToken = purchaseToken, OrderId = orderId ?? "" };
            var response = await client.PostAsJsonAsync("api/subscription/google-play/verify", payload);

            if (response.IsSuccessStatusCode)
            {
                return (true, string.Empty);
            }

            var errorMessage = await ApiErrorMessageFormatter.ReadDisplayMessageAsync(response);
            _logger.LogWarning("Google Play purchase verification failed: {StatusCode} {ErrorMessage}",
                response.StatusCode, errorMessage);
            return (false, errorMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to verify Google Play purchase with server");
            return (false, $"Unable to connect to server: {ex.Message}");
        }
    }

    public async Task<SubscriptionStatusDto?> GetSubscriptionStatusAsync()
    {
        var client = _httpClientFactory.CreateClient("MusicSalesApi");
        try
        {
            return await client.GetFromJsonAsync<SubscriptionStatusDto>("api/subscription/status");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load subscription status");
            return null;
        }
    }

    public async Task<(bool Success, DateTime? EndDate)> CancelSubscriptionAsync()
    {
        var client = _httpClientFactory.CreateClient("MusicSalesApi");
        try
        {
            var response = await client.PostAsync("api/subscription/cancel", null);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<CancelResponse>();
                return (result?.Success ?? false, result?.EndDate);
            }

            var errorBody = await response.Content.ReadAsStringAsync();
            _logger.LogWarning("Cancel subscription failed: {Status} {Body}", response.StatusCode, errorBody);
            return (false, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cancel subscription");
            return (false, null);
        }
    }

    private sealed record CancelResponse(bool Success, DateTime? EndDate);

    public async Task<bool> ReportSongAsync(int songMetadataId, string reason)
    {
        var client = _httpClientFactory.CreateClient("MusicSalesApi");
        try
        {
            var response = await client.PostAsJsonAsync($"api/music/report/{songMetadataId}", new { Reason = reason });
            if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
                throw new InvalidOperationException("You have already reported this song.");
            return response.IsSuccessStatusCode;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to report song {SongMetadataId}", songMetadataId);
            return false;
        }
    }
}
