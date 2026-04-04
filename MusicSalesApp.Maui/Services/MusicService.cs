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
}
