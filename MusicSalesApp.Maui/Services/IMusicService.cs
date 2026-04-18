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

    /// <summary>
    /// Sends a Google Play purchase token to the server for verification and subscription recording.
    /// </summary>
    Task<bool> VerifyGooglePlayPurchaseAsync(string purchaseToken, string? orderId);

    /// <summary>
    /// Calls the server to cancel the user's active subscription (routes to the correct provider).
    /// </summary>
    Task<(bool Success, DateTime? EndDate)> CancelSubscriptionAsync();
}
