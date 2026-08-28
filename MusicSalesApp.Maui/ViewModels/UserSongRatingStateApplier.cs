using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.ViewModels;

/// <summary>
/// Puts the current user's rating state onto loaded songs.
///
/// Shared by every screen that shows thumbs, for the same reason
/// <see cref="OptimisticLikeStateUpdater"/> is: four copies of this would drift.
/// </summary>
public static class UserSongRatingStateApplier
{
    /// <summary>
    /// Applies what the server reported, and folds it into the local store so a stream from another
    /// device or from the web survives the next offline session.
    ///
    /// Eligibility is OR-ed with the local store rather than overwritten by the server. Offline, a
    /// qualifying stream is only queued, so the server legitimately does not know about it yet - and
    /// taking its "no" as authoritative would grey out a button the user has already earned.
    /// </summary>
    public static void ApplyServerStatuses(
        IReadOnlyDictionary<int, UserSongRatingState> statuses,
        IEnumerable<SongDto> songs,
        IUserStreamedSongStore? streamedSongStore = null)
    {
        if (statuses.Count > 0)
        {
            streamedSongStore?.MergeFromServer(
                statuses.Where(entry => entry.Value.HasStreamed).Select(entry => entry.Key));
        }

        foreach (var song in songs)
        {
            if (!statuses.TryGetValue(song.Id, out var state))
            {
                continue;
            }

            song.UserLikeStatus = state.LikeStatus;
            song.HasStreamed = state.HasStreamed || streamedSongStore?.HasStreamed(song.Id) == true;
        }
    }

    /// <summary>
    /// Marks what this device already knows the user has streamed.
    ///
    /// Called on every catalogue load, including the offline ones the server status fetch skips: without
    /// it, a song streamed offline would lose its eligibility as soon as the list was rebuilt from the
    /// snapshot, and the thumbs would go dead until connectivity returned.
    /// </summary>
    public static void SeedFromLocalStore(IEnumerable<SongDto> songs, IUserStreamedSongStore? streamedSongStore)
    {
        if (streamedSongStore == null)
        {
            return;
        }

        var streamedSongIds = streamedSongStore.GetStreamedSongIds();
        if (streamedSongIds.Count == 0)
        {
            return;
        }

        foreach (var song in songs)
        {
            if (streamedSongIds.Contains(song.Id))
            {
                song.HasStreamed = true;
            }
        }
    }
}
