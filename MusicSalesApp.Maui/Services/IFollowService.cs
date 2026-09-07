namespace MusicSalesApp.Maui.Services;

/// <summary>
/// Following artists, against <c>api/mobile/follows</c>.
/// </summary>
/// <remarks>
/// Deliberately its own service rather than more members on <see cref="IMusicService"/>. That
/// interface is resolved through <c>OfflineAwareMusicService</c>, so every member added to it
/// obliges a pass-through in the decorator - and following is an online-only action anyway, since
/// there is no offline intent queue behind it yet.
/// </remarks>
public interface IFollowService
{
    /// <summary>
    /// Sets whether the signed-in user follows this artist, and returns the state the server ended
    /// up in.
    /// </summary>
    /// <remarks>
    /// The desired state goes in the request rather than a toggle, matching
    /// <c>PUT api/music/like-state/{id}</c>: the outcome then depends only on what was asked for,
    /// which is what makes a replayed request safe.
    ///
    /// <para>
    /// Returns null when the change could not be made. The server answers <b>400 for every domain
    /// refusal</b> - following yourself, a blocked artist, an unavailable persona - so a null here
    /// is not necessarily a transport failure and must not be retried blindly.
    /// </para>
    /// </remarks>
    Task<FollowStateResult?> SetFollowStateAsync(int personaId, bool following, int? sourceSongId = null);

    /// <summary>
    /// Which of these personas the signed-in user follows, in one round trip, or <c>null</c> when
    /// the server could not be asked.
    /// </summary>
    /// <remarks>
    /// The music library renders one bell per card and many cards share an artist; resolving per
    /// card would be a request per card.
    ///
    /// <para>
    /// <b>An empty set and a null are not the same answer, and collapsing them is a real bug.</b>
    /// Empty means the server replied and the user follows none of these. Null means we never got
    /// an answer - offline, a 5xx, an expired token - and the caller must leave what it already
    /// knows alone. Returning empty for both is what let a lift or a brief outage erase the cached
    /// follow set and visibly unfollow a user's whole library. This is the same distinction
    /// <c>IBillingService</c> draws between "the store answered, you own nothing" and "we could not
    /// ask"; see the rule in CLAUDE.md.
    /// </para>
    /// </remarks>
    Task<HashSet<int>?> GetFollowedPersonaIdsAsync(IEnumerable<int> personaIds);
}

/// <summary>
/// What the server did. <c>Outcome</c> is the server's own enum name, for logging rather than
/// branching - the boolean is the contract.
/// </summary>
public sealed record FollowStateResult(int CreatorPersonaId, bool Following, string? Outcome);
