namespace MusicSalesApp.Maui.ViewModels;

/// <summary>
/// DTO matching the SongListItemDto returned by GET /api/music/songs.
/// </summary>
public class SongDto
{
    public int Id { get; set; }
    public string SongTitle { get; set; } = string.Empty;
    public string ArtistName { get; set; } = string.Empty;
    public string Genre { get; set; } = string.Empty;
    public string? AlbumArtUrl { get; set; }
    public string StreamUrl { get; set; } = string.Empty;
    public int StreamCount { get; set; }
    public double? TrackLengthSeconds { get; set; }
}
