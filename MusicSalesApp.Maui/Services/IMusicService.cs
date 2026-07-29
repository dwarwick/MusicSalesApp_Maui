using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Services;

/// <summary>
/// Where the songs returned by the last <see cref="IMusicService.GetSongsAsync()"/> call came from.
/// Callers use this to distinguish "offline, showing what is downloaded" from "the server is broken",
/// which need very different UI.
/// </summary>
public enum SongCatalogSource
{
    /// <summary>Fetched from the API. Default, so an unconfigured mock behaves as it always has.</summary>
    Live = 0,

    /// <summary>Restored from the offline catalog and filtered down to songs with cached audio.</summary>
    OfflineCache = 1,

    /// <summary>Neither the API nor the offline cache produced anything.</summary>
    Unavailable = 2
}

public interface IMusicService
{
    event Action<int, int>? OnStreamCountRecorded;

    string? LastSongsError { get; }

    SongCatalogSource LastSongsSource { get; }
    Task<List<SongDto>> GetSongsAsync();
    Task<List<SongDto>> GetSongsAsync(CancellationToken cancellationToken);
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
    /// Idempotently sets the user's opinion of a song: true = thumbs up, false = thumbs down,
    /// null = no opinion. Unlike the toggle endpoints the outcome depends only on
    /// <paramref name="desiredState"/>, which is what makes an offline queue safe to replay.
    ///
    /// Offline the intent is queued and this returns a null result; the caller keeps its optimistic UI.
    /// </summary>
    Task<SetLikeStateOutcome> SetLikeStateAsync(int songMetadataId, bool? desiredState);

    Task FlushPendingLikeStatesAsync();

    Task ClearPendingLikeStatesAsync();

    /// <summary>
    /// Like/dislike intents recorded offline and not yet accepted by the server, keyed by song id.
    /// Applied over restored offline songs so an optimistic tap survives an app restart.
    /// </summary>
    Task<IReadOnlyDictionary<int, bool?>> GetPendingLikeStatesAsync();

    /// <summary>
    /// Sends a provider-aware purchase payload to the server for verification and subscription recording.
    /// </summary>
    Task<(bool Success, string ErrorMessage)> VerifySubscriptionPurchaseAsync(BillingPurchaseVerificationRequest request);

    /// <summary>
    /// Sends a Google Play purchase token to the server for verification and subscription recording.
    /// Compatibility wrapper while call sites migrate to VerifySubscriptionPurchaseAsync.
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
