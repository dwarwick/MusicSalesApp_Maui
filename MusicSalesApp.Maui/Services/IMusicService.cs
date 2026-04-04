using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Services;

public interface IMusicService
{
    Task<List<SongDto>> GetSongsAsync();
    Task RecordStreamAsync(int songMetadataId);
}
