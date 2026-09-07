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
/// </remarks>
public interface IArtistFollowStateCoordinator
{
    /// <summary>Raised after the cached set has been updated, never before.</summary>
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
    private readonly ILogger<ArtistFollowStateCoordinator> _logger;

    private readonly HashSet<int> _followed = [];
    private readonly object _gate = new();

    public ArtistFollowStateCoordinator(
        IFollowService followService,
        IArtistFollowNotifier notifier,
        ILogger<ArtistFollowStateCoordinator> logger)
    {
        _followService = followService;
        _notifier = notifier;
        _logger = logger;

        _notifier.FollowStateChanged += OnNotifierFollowStateChanged;
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
        // brings every other card for this artist with it - including this one, if the server
        // settled on something other than what was asked for.
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

        FollowStateChanged?.Invoke(this, change);
    }
}
