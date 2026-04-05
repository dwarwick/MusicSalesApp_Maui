using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Services;

public class MusicService : IMusicService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<MusicService> _logger;

    public MusicService(IHttpClientFactory httpClientFactory, ILogger<MusicService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<List<SongDto>> GetSongsAsync()
    {
        var client = _httpClientFactory.CreateClient("MusicSalesApi");
        try
        {
            var songs = await client.GetFromJsonAsync<List<SongDto>>("api/music/songs");
            return songs ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch songs from API");
            return [];
        }
    }

    public async Task<int> GetStreamQualifyingSecondsAsync()
    {
        var client = _httpClientFactory.CreateClient("MusicSalesApi");
        try
        {
            var result = await client.GetFromJsonAsync<StreamQualifyingSecondsDto>("api/music/stream-qualifying-seconds");
            return result?.StreamQualifyingSeconds ?? DefaultStreamQualifyingSeconds;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch stream qualifying seconds, using default {Default}s", DefaultStreamQualifyingSeconds);
            return DefaultStreamQualifyingSeconds;
        }
    }

    private const int DefaultStreamQualifyingSeconds = 30;

    private sealed record StreamQualifyingSecondsDto(int StreamQualifyingSeconds);

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
}
