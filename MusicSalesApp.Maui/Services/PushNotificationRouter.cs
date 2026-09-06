using Microsoft.Extensions.Logging;
using MusicSalesApp.Common.Helpers;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Services;

/// <summary>
/// Turns a tapped notification into a destination in the app.
/// </summary>
/// <remarks>
/// Platform-neutral on purpose: the two heads capture the payload very differently - iOS from
/// <c>UNUserNotificationCenter</c>, Android from an Intent's extras - but both end up with the same
/// flat string dictionary the server sent, so all the deciding happens here where it can be tested.
/// </remarks>
public interface IPushNotificationRouter
{
    /// <summary>
    /// Navigates to whatever the notification is about. Safe to call for any payload: an unknown
    /// kind, a missing id, or a song that is no longer in the catalogue all leave the user where
    /// they landed rather than failing at them.
    /// </summary>
    Task HandleAsync(IReadOnlyDictionary<string, string?>? data);

    /// <summary>
    /// Holds a payload that arrived before the app could navigate, for
    /// <see cref="FlushPendingAsync"/> to replay.
    /// </summary>
    /// <remarks>
    /// A tap on a notification while the app is closed launches the app AND delivers the payload,
    /// long before Shell exists - navigating then throws or silently does nothing. This is the
    /// difference between the cold-start tap working and only the warm one working, which is easy
    /// to miss because testing usually leaves the app running.
    /// </remarks>
    void QueuePending(IReadOnlyDictionary<string, string?>? data);

    /// <summary>Replays a queued payload, if there is one. Called once navigation is available.</summary>
    Task FlushPendingAsync();
}

/// <inheritdoc />
public sealed class PushNotificationRouter : IPushNotificationRouter
{
    private readonly INavigationService _navigationService;
    private readonly IMusicService _musicService;
    private readonly ILogger<PushNotificationRouter> _logger;
    private readonly object _pendingLock = new();

    private IReadOnlyDictionary<string, string?>? _pending;

    public PushNotificationRouter(
        INavigationService navigationService,
        IMusicService musicService,
        ILogger<PushNotificationRouter> logger)
    {
        _navigationService = navigationService;
        _musicService = musicService;
        _logger = logger;
    }

    /// <inheritdoc />
    public void QueuePending(IReadOnlyDictionary<string, string?>? data)
    {
        if (data is null || data.Count == 0)
        {
            return;
        }

        lock (_pendingLock)
        {
            // Last tap wins. Two notifications tapped before the app is up is not a queue worth
            // keeping - the user is asking for the one they just touched.
            _pending = data;
        }
    }

    /// <inheritdoc />
    public async Task FlushPendingAsync()
    {
        IReadOnlyDictionary<string, string?>? pending;

        lock (_pendingLock)
        {
            pending = _pending;
            _pending = null;
        }

        if (pending is not null)
        {
            await HandleAsync(pending);
        }
    }

    /// <inheritdoc />
    public async Task HandleAsync(IReadOnlyDictionary<string, string?>? data)
    {
        if (data is null || data.Count == 0)
        {
            return;
        }

        try
        {
            if (!TryGetValue(data, PushDataKeys.Kind, out var kind))
            {
                return;
            }

            // The destination has to match what the notification said. A summary that names an
            // artist and a count opens that artist; one naming a song opens the song. Anything
            // that promised neither - an artist message, or a digest spanning several artists -
            // leaves the user on Home, which is where the app opens anyway.
            if (string.Equals(kind, PushNotificationKinds.Digest, StringComparison.Ordinal))
            {
                await HandleDigestAsync(data);
                return;
            }

            if (!string.Equals(kind, PushNotificationKinds.Release, StringComparison.Ordinal))
            {
                // Artist messages have no destination yet - the Artist Messages page does not
                // exist. Landing on Home is the current behaviour and stays correct until it does.
                return;
            }

            if (!TryGetValue(data, PushDataKeys.SongId, out var rawSongId) ||
                !int.TryParse(rawSongId, out var songId))
            {
                return;
            }

            var song = await FindSongAsync(songId);

            if (song is null)
            {
                // Withdrawn since the push was sent, or the catalogue is unreachable and the
                // offline snapshot does not have it. Home is a better answer than an error.
                _logger.LogInformation(
                    "A tapped release notification named song {SongId}, which is not in the catalogue.", songId);
                return;
            }

            // The same parameter the library and Home pass, because the player takes the whole
            // SongDto rather than an id - see MusicLibraryViewModel.OpenSongAsync.
            await _navigationService.GoToAsync(NavigationRoutes.SongPlayer, new Dictionary<string, object>
            {
                ["Song"] = song
            });
        }
        catch (Exception ex)
        {
            // A tap must never be able to crash the app on launch, which is exactly when this runs.
            _logger.LogWarning(ex, "Could not route a tapped push notification.");
        }
    }

    private async Task HandleDigestAsync(IReadOnlyDictionary<string, string?> data)
    {
        if (!TryGetValue(data, PushDataKeys.ArtistName, out var artistName))
        {
            // A digest spanning several artists carries no name, because it has no single
            // destination. Home is the honest answer.
            return;
        }

        // The artist page is the playlist player filtered by artist - the same route
        // NavigateToArtistAsync uses from a song card, addressed by name rather than persona id.
        // Taking the name from the payload is what keeps this working with no catalogue round
        // trip, which matters on a cold start with no network.
        await _navigationService.GoToAsync(NavigationRoutes.PlaylistPlayer, new Dictionary<string, object>
        {
            ["ArtistName"] = artistName
        });
    }

    private async Task<SongDto?> FindSongAsync(int songId)
    {
        // GetSongsAsync goes through OfflineAwareMusicService, so this still resolves from the
        // cached catalogue when the server is unreachable.
        var songs = await _musicService.GetSongsAsync();
        return songs?.FirstOrDefault(song => song.Id == songId);
    }

    private static bool TryGetValue(IReadOnlyDictionary<string, string?> data, string key, out string value)
    {
        value = string.Empty;

        if (!data.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        value = raw;
        return true;
    }
}
