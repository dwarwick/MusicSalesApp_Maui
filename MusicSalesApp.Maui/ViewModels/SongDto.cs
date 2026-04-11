using CommunityToolkit.Mvvm.ComponentModel;

namespace MusicSalesApp.Maui.ViewModels;

/// <summary>
/// DTO matching the SongListItemDto returned by GET /api/music/songs.
/// LikeCount/DislikeCount are populated separately from the bulk likes endpoint.
/// Extends ObservableObject so SignalR-driven property updates refresh the UI.
/// </summary>
public partial class SongDto : ObservableObject
{
    public int Id { get; set; }
    public string SongTitle { get; set; } = string.Empty;
    public string ArtistName { get; set; } = string.Empty;
    public string Genre { get; set; } = string.Empty;
    public string? AlbumArtUrl { get; set; }
    public string? PersonaImageUrl { get; set; }
    public string StreamUrl { get; set; } = string.Empty;
    public double? TrackLengthSeconds { get; set; }
    public int? CreatorUserId { get; set; }

    [ObservableProperty]
    public partial int StreamCount { get; set; }

    // Populated from bulk likes endpoint, not from songs API
    [ObservableProperty]
    public partial int LikeCount { get; set; }

    [ObservableProperty]
    public partial int DislikeCount { get; set; }
}
