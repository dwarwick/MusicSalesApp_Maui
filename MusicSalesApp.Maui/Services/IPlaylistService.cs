using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Services;

/// <summary>
/// Client-side service for the MAUI app's playlist feature. Wraps the
/// <c>api/mobile/playlists</c> endpoints on the server.
/// </summary>
public interface IPlaylistService
{
    Task<HomePlaylistsDto?> GetHomePlaylistsAsync();
    Task<List<PlaylistDto>> GetMyPlaylistsAsync();
    Task<PlaylistSongsDto?> GetPlaylistSongsAsync(int playlistId);
    Task<PlaylistSongsDto?> GetRecommendedSongsAsync();

    /// <summary>
    /// The five global "most streamed" playlists as tiles, in display order, empty ones omitted.
    /// Not personal, so this works signed out.
    /// </summary>
    Task<List<PlaylistDto>> GetTopStreamedPlaylistsAsync();

    /// <summary>
    /// One "most streamed" playlist's songs, in rank order - most streamed first. Callers must not
    /// re-sort the result.
    /// </summary>
    Task<PlaylistSongsDto?> GetTopStreamedSongsAsync(string window);

    Task<PlaylistOperationResult<PlaylistDto>> CreatePlaylistAsync(string name);
    Task<PlaylistOperationResult> RenamePlaylistAsync(int playlistId, string name);
    Task<PlaylistOperationResult> DeletePlaylistAsync(int playlistId);

    Task<AvailableSongsResponse> GetAvailableSongsAsync(int playlistId);
    Task<PlaylistOperationResult> AddSongAsync(int playlistId, int songMetadataId);
    Task<PlaylistOperationResult> RemoveSongAsync(int playlistId, int userPlaylistId);
    Task<PlaylistOperationResult> ReorderAsync(int playlistId, IReadOnlyList<int> userPlaylistIdsInOrder);
}
