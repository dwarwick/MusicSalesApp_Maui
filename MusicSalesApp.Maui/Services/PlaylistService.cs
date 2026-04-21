using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Services;

public class PlaylistService : IPlaylistService
{
    private const string Client = "MusicSalesApi";
    private const string BaseRoute = "api/mobile/playlists";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PlaylistService> _logger;

    public PlaylistService(IHttpClientFactory httpClientFactory, ILogger<PlaylistService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    private HttpClient CreateClient() => _httpClientFactory.CreateClient(Client);

    public async Task<HomePlaylistsDto?> GetHomePlaylistsAsync()
    {
        try
        {
            return await CreateClient().GetFromJsonAsync<HomePlaylistsDto>($"{BaseRoute}/home");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load home playlists");
            return null;
        }
    }

    public async Task<List<PlaylistDto>> GetMyPlaylistsAsync()
    {
        try
        {
            return await CreateClient().GetFromJsonAsync<List<PlaylistDto>>(BaseRoute) ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load my playlists");
            return [];
        }
    }

    public async Task<PlaylistSongsDto?> GetPlaylistSongsAsync(int playlistId)
    {
        try
        {
            return await CreateClient().GetFromJsonAsync<PlaylistSongsDto>($"{BaseRoute}/{playlistId}/songs");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load songs for playlist {PlaylistId}", playlistId);
            return null;
        }
    }

    public async Task<PlaylistSongsDto?> GetRecommendedSongsAsync()
    {
        try
        {
            return await CreateClient().GetFromJsonAsync<PlaylistSongsDto>($"{BaseRoute}/recommended");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load recommended songs");
            return null;
        }
    }

    public async Task<PlaylistOperationResult<PlaylistDto>> CreatePlaylistAsync(string name)
    {
        try
        {
            var response = await CreateClient().PostAsJsonAsync(BaseRoute, new { Name = name });
            if (response.StatusCode == HttpStatusCode.Forbidden)
                return PlaylistOperationResult<PlaylistDto>.NeedsSubscription();
            if (!response.IsSuccessStatusCode)
                return PlaylistOperationResult<PlaylistDto>.Fail($"Create failed: {response.StatusCode}");

            var dto = await response.Content.ReadFromJsonAsync<PlaylistDto>();
            return dto != null
                ? PlaylistOperationResult<PlaylistDto>.Ok(dto)
                : PlaylistOperationResult<PlaylistDto>.Fail("Create returned empty response.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create playlist");
            return PlaylistOperationResult<PlaylistDto>.Fail(ex.Message);
        }
    }

    public async Task<PlaylistOperationResult> RenamePlaylistAsync(int playlistId, string name)
    {
        try
        {
            var response = await CreateClient().PutAsJsonAsync($"{BaseRoute}/{playlistId}", new { Name = name });
            return InterpretResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rename playlist {PlaylistId}", playlistId);
            return PlaylistOperationResult.Fail(ex.Message);
        }
    }

    public async Task<PlaylistOperationResult> DeletePlaylistAsync(int playlistId)
    {
        try
        {
            var response = await CreateClient().DeleteAsync($"{BaseRoute}/{playlistId}");
            return InterpretResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete playlist {PlaylistId}", playlistId);
            return PlaylistOperationResult.Fail(ex.Message);
        }
    }

    public async Task<AvailableSongsResponse> GetAvailableSongsAsync(int playlistId)
    {
        try
        {
            var result = await CreateClient()
                .GetFromJsonAsync<AvailableSongsResponse>($"{BaseRoute}/{playlistId}/available-songs");
            return result ?? new AvailableSongsResponse();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load available songs for playlist {PlaylistId}", playlistId);
            return new AvailableSongsResponse();
        }
    }

    public async Task<PlaylistOperationResult> AddSongAsync(int playlistId, int songMetadataId)
    {
        try
        {
            var response = await CreateClient()
                .PostAsJsonAsync($"{BaseRoute}/{playlistId}/songs", new { SongMetadataId = songMetadataId });
            return InterpretResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add song {SongId} to playlist {PlaylistId}", songMetadataId, playlistId);
            return PlaylistOperationResult.Fail(ex.Message);
        }
    }

    public async Task<PlaylistOperationResult> RemoveSongAsync(int playlistId, int userPlaylistId)
    {
        try
        {
            var response = await CreateClient()
                .DeleteAsync($"{BaseRoute}/{playlistId}/songs/{userPlaylistId}");
            return InterpretResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove song {UserPlaylistId} from playlist {PlaylistId}", userPlaylistId, playlistId);
            return PlaylistOperationResult.Fail(ex.Message);
        }
    }

    public async Task<PlaylistOperationResult> ReorderAsync(int playlistId, IReadOnlyList<int> userPlaylistIdsInOrder)
    {
        try
        {
            var response = await CreateClient().PutAsJsonAsync(
                $"{BaseRoute}/{playlistId}/reorder",
                new { UserPlaylistIds = userPlaylistIdsInOrder });
            return InterpretResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reorder playlist {PlaylistId}", playlistId);
            return PlaylistOperationResult.Fail(ex.Message);
        }
    }

    private static PlaylistOperationResult InterpretResponse(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.Forbidden)
            return PlaylistOperationResult.NeedsSubscription();
        if (response.IsSuccessStatusCode)
            return PlaylistOperationResult.Ok();
        return PlaylistOperationResult.Fail($"Request failed: {response.StatusCode}");
    }
}
