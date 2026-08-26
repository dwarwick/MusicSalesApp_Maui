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

/// <summary>
/// DTO for the idempotent set-state response from PUT /api/music/like-state/{id}.
/// </summary>
public class LikeStateResult
{
    public bool? UserLikeStatus { get; set; }

    /// <summary>
    /// Null means "no authoritative count available" - the state was applied, but the counts could not
    /// be read. The server always sends both; only the toggle-endpoint compatibility path can leave
    /// them unset, and a caller must then keep whatever it is already showing rather than treat the
    /// absence as zero.
    /// </summary>
    public int? LikeCount { get; set; }

    /// <inheritdoc cref="LikeCount"/>
    public int? DislikeCount { get; set; }
}

/// <summary>
/// Outcome of a thumbs-up/down tap.
///
/// The three cases need different UI: <see cref="Result"/> non-null means the server has authoritative
/// counts to apply; <see cref="Queued"/> means keep the optimistic values until the queue drains; and
/// neither means the request failed outright and the caller should roll back.
/// </summary>
public readonly record struct SetLikeStateOutcome(LikeStateResult? Result, bool Queued)
{
    public static SetLikeStateOutcome Applied(LikeStateResult result) => new(result, false);

    public static SetLikeStateOutcome QueuedForRetry() => new(null, true);

    public static SetLikeStateOutcome Failed() => new(null, false);

    /// <summary>
    /// The server refused because the caller has never streamed this song. A subset of
    /// <see cref="Failed"/> - the caller still rolls back, but it has something to tell the user.
    /// </summary>
    public static SetLikeStateOutcome RequiresStream() => new(null, false) { NeedsStream = true };

    /// <inheritdoc cref="RequiresStream"/>
    public bool NeedsStream { get; init; }
}

/// <summary>
/// What the current user has done with one song: their rating, and whether they have streamed it.
///
/// Both come from GET api/music/likes/user-status in a single round trip, since every screen that wants
/// one wants the other.
/// </summary>
/// <param name="LikeStatus">true = liked, false = disliked, null = no opinion.</param>
/// <param name="HasStreamed">Whether the user may set a rating on this song.</param>
public readonly record struct UserSongRatingState(bool? LikeStatus, bool HasStreamed);

/// <summary>
/// What happened to a thumbs-up/down tap, as far as the screen that hosts the buttons is concerned.
/// </summary>
public enum LikeApplyOutcome
{
    /// <summary>Applied, queued for retry, or rolled back - nothing for the user to be told.</summary>
    Handled,

    /// <summary>Refused because the user has not streamed this song. Tell them why.</summary>
    NeedsStream
}
