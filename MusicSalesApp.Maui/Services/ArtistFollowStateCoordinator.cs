using Microsoft.Extensions.Logging;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Services;

/// <summary>
/// Keeps the follow bells on a page of songs in step with each other and with the server.
/// </summary>
/// <remarks>
/// Every surface that lists songs needs the same three things - resolve the states in one round
/// trip, toggle one optimistically, and make every other card for that artist agree - so they live
/// here once rather than in each ViewModel.
///
/// <para>
/// Singleton, so the followed set survives navigation between the library and home. It subscribes
/// to <see cref="IArtistFollowNotifier"/> itself and re-raises afterwards, which means a subscriber
/// is guaranteed to see the updated set: subscribing to the notifier directly would race it.
/// </para>
///
/// <para>
/// Surviving navigation is the point; surviving a sign-out is not. The set is one account's private
/// data, so it is dropped whenever the signed-in identity moves - without that, the next person to
/// sign in on the same handset sees the previous one's follows, in a feature whose whole privacy
/// rule is that nobody learns who follows whom.
/// </para>
/// </remarks>
public interface IArtistFollowStateCoordinator
{
    /// <summary>
    /// Raised after the cached set has been updated, never before, and always on the UI thread.
    /// </summary>
    /// <remarks>
    /// Marshalled here rather than by each subscriber. A handler that stamps state onto SongDtos
    /// looks like ordinary code and only fails once those properties are bound to something a
    /// platform renders, so leaving it to the subscriber means the next surface has to know.
    /// </remarks>
    event EventHandler<ArtistFollowChange>? FollowStateChanged;

    /// <summary>
    /// Resolves the follow state for these songs and stamps it onto them.
    /// </summary>
    Task LoadForAsync(IEnumerable<SongDto> songs);

    /// <summary>
    /// Stamps the already-known state onto these songs, without asking the server.
    /// </summary>
    void ApplyKnownState(IEnumerable<SongDto> songs);

    /// <summary>
    /// Follows or unfollows this song's artist, and returns whether the change stuck.
    /// </summary>
    Task<bool> ToggleAsync(SongDto song);
}

/// <inheritdoc />
public sealed class ArtistFollowStateCoordinator : IArtistFollowStateCoordinator
{
    private readonly IFollowService _followService;
    private readonly IArtistFollowNotifier _notifier;
    private readonly IAuthService _authService;
    private readonly ILogger<ArtistFollowStateCoordinator> _logger;

    private readonly HashSet<int> _followed = [];
    private readonly object _gate = new();

    /// <summary>
    /// Whose follows <see cref="_followed"/> holds. Null while signed out.
    /// </summary>
    private int? _followedUserId;

    public ArtistFollowStateCoordinator(
        IFollowService followService,
        IArtistFollowNotifier notifier,
        IAuthService authService,
        ILogger<ArtistFollowStateCoordinator> logger)
    {
        _followService = followService;
        _notifier = notifier;
        _authService = authService;
        _logger = logger;

        _followedUserId = _authService.UserId;

        _notifier.FollowStateChanged += OnNotifierFollowStateChanged;
        _authService.AuthStateChanged += OnAuthStateChanged;
    }

    /// <summary>
    /// Drops the cached set when the signed-in identity moves, so one account's follows never reach
    /// the next. Covers sign-out, sign-in and switching accounts with the one comparison.
    /// </summary>
    private void OnAuthStateChanged()
    {
        var userId = _authService.UserId;

        lock (_gate)
        {
            if (_followedUserId == userId)
            {
                return;
            }

            _followedUserId = userId;
            _followed.Clear();
        }

        _logger.LogInformation("Signed-in identity changed; dropped the cached follow set.");
    }

    public event EventHandler<ArtistFollowChange>? FollowStateChanged;

    /// <inheritdoc />
    public async Task LoadForAsync(IEnumerable<SongDto> songs)
    {
        var list = songs?.Where(song => song.PersonaId is > 0).ToList() ?? [];
        if (list.Count == 0)
        {
            return;
        }

        var personaIds = list.Select(song => song.PersonaId!.Value).Distinct().ToList();
        var followed = await _followService.GetFollowedPersonaIdsAsync(personaIds);

        if (followed is null)
        {
            // We asked and got no answer. Stamping "not following" here would record a network
            // failure as a fact about the user's data and, because this set is shared by every
            // surface, would unfollow their whole library until the next successful round trip.
            // Apply what is already known instead - which still stamps ownership.
            _logger.LogInformation(
                "Follow states for {Count} personas are unknown; kept the cached set.",
                personaIds.Count);

            ApplyKnownState(list);
            return;
        }

        lock (_gate)
        {
            // Replace only what was asked about. Removing ids outside this page would drop the
            // state for songs on a screen the user can navigate straight back to.
            foreach (var personaId in personaIds)
            {
                if (followed.Contains(personaId))
                {
                    _followed.Add(personaId);
                }
                else
                {
                    _followed.Remove(personaId);
                }
            }
        }

        ApplyKnownState(list);
    }

    /// <inheritdoc />
    public void ApplyKnownState(IEnumerable<SongDto> songs)
    {
        if (songs is null)
        {
            return;
        }

        foreach (var song in songs)
        {
            // Ownership first, and OUTSIDE the persona guard below. It is a purely local
            // comparison, so it holds offline and while signed out, and this is the one method
            // every surface runs its songs through - the card list, both players, and every
            // notifier event - which makes it the only place the bell can be hidden once.
            song.IsOwnArtist = ArtistFollowPolicy.IsOwnArtist(song, _authService);

            if (song.PersonaId is not int personaId || personaId <= 0)
            {
                continue;
            }

            bool isFollowing;
            lock (_gate)
            {
                isFollowing = _followed.Contains(personaId);
            }

            song.IsFollowingArtist = isFollowing;
        }
    }

    /// <inheritdoc />
    public async Task<bool> ToggleAsync(SongDto song)
    {
        if (song?.PersonaId is not int personaId || personaId <= 0)
        {
            return false;
        }

        // Mirrors the server's own CannotFollowSelf rather than trusting the bell to be hidden. The
        // hidden control is the courtesy; this is what stops a stale binding turning a tap into an
        // optimistic flip that a 400 undoes a round trip later, which just looks like a bug.
        if (ArtistFollowPolicy.IsOwnArtist(song, _authService))
        {
            return false;
        }

        var wanted = !song.IsFollowingArtist;

        // Optimistic, like the thumbs: the bell moves under the finger and is put back if the
        // server disagrees. A bell that waits for a round trip reads as an unresponsive control.
        song.IsFollowingArtist = wanted;

        var result = await _followService.SetFollowStateAsync(personaId, wanted, song.Id);

        if (result is null)
        {
            song.IsFollowingArtist = !wanted;
            _logger.LogInformation(
                "Follow change for persona {PersonaId} did not stick; put the bell back.", personaId);
            return false;
        }

        // FollowService raises the notifier, which lands on OnNotifierFollowStateChanged below and
        // brings every other card for this artist with it. This song is stamped directly as well:
        // the notifier only reaches cards a subscribed ViewModel is holding, and the tapped song
        // can be one that is currently filtered out of its own list.
        ApplyKnownState([song]);

        return result.Following == wanted;
    }

    private void OnNotifierFollowStateChanged(object? sender, ArtistFollowChange change)
    {
        lock (_gate)
        {
            if (change.IsFollowing)
            {
                _followed.Add(change.PersonaId);
            }
            else
            {
                _followed.Remove(change.PersonaId);
            }
        }

        // Marshalled once, here, because this is the single choke point every subscriber goes
        // through. FollowService raises the notifier from a ConfigureAwait(false) continuation, so
        // without this the handlers set bound properties - and therefore Path.Fill, Path.Data and
        // Grid.IsVisible - from a thread pool thread, which Android rejects outright.
        MainThreadDispatch.Run(() => FollowStateChanged?.Invoke(this, change));
    }
}
