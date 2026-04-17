using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Services;

public interface IMusicService
{
    Task<List<SongDto>> GetSongsAsync();
    Task<SongDto?> GetSongByTitleAsync(string title);
    Task<int> GetStreamQualifyingSecondsAsync();
    Task RecordStreamAsync(int songMetadataId);
    Task<List<LikeCountDto>> GetBulkLikeCountsAsync(IEnumerable<int> songIds);
    Task<Dictionary<int, bool?>> GetBulkUserLikeStatusAsync(IEnumerable<int> songIds);
    Task<LikeToggleResult?> ToggleLikeAsync(int songMetadataId);
    Task<LikeToggleResult?> ToggleDislikeAsync(int songMetadataId);
}
