using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Services;

public interface IMusicService
{
    event Action<int, int>? OnStreamCountRecorded;

    string? LastSongsError { get; }
    Task<List<SongDto>> GetSongsAsync();
    Task<SongDto?> GetSongByTitleAsync(string title);
    Task<int> GetStreamQualifyingSecondsAsync();
    Task<int?> RecordStreamAsync(int songMetadataId);
    Task FlushPendingStreamRecordsAsync();
    Task ClearPendingStreamRecordsAsync();
    Task<List<LikeCountDto>> GetBulkLikeCountsAsync(IEnumerable<int> songIds);
    Task<Dictionary<int, bool?>> GetBulkUserLikeStatusAsync(IEnumerable<int> songIds);
    Task<LikeToggleResult?> ToggleLikeAsync(int songMetadataId);
    Task<LikeToggleResult?> ToggleDislikeAsync(int songMetadataId);

    /// <summary>
    /// Sends a Google Play purchase token to the server for verification and subscription recording.
    /// </summary>
    Task<(bool Success, string ErrorMessage)> VerifyGooglePlayPurchaseAsync(string purchaseToken, string? orderId);

    Task<SubscriptionStatusDto?> GetSubscriptionStatusAsync();

    /// <summary>
    /// Calls the server to cancel the user's active subscription (routes to the correct provider).
    /// </summary>
    Task<(bool Success, DateTime? EndDate)> CancelSubscriptionAsync();

    /// <summary>
    /// Reports a song for a policy violation (copyright or terms of use).
    /// </summary>
    Task<bool> ReportSongAsync(int songMetadataId, string reason);
}
