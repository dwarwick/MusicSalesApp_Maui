namespace MusicSalesApp.Maui.ViewModels;

/// <summary>
/// DTO for bulk like/dislike counts returned by GET /api/music/likes/bulk.
/// </summary>
public class LikeCountDto
{
    public int SongMetadataId { get; set; }
    public int LikeCount { get; set; }
    public int DislikeCount { get; set; }
}

/// <summary>
/// DTO for like/dislike toggle response from POST /api/music/like or /api/music/dislike.
/// </summary>
public class LikeToggleResult
{
    public bool IsLiked { get; set; }
    public bool IsDisliked { get; set; }
    public int LikeCount { get; set; }
    public int DislikeCount { get; set; }
}
