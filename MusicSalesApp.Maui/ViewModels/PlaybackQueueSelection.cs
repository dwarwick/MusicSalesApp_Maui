using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.ViewModels;

internal static class PlaybackQueueSelection
{
    public static bool HasEquivalentActiveQueue(IPlaybackService playbackService, IReadOnlyList<SongDto> songs)
    {
        var activeQueue = playbackService.Playlist;
        if (activeQueue == null || activeQueue.Count != songs.Count)
        {
            return false;
        }

        for (var index = 0; index < songs.Count; index++)
        {
            if (activeQueue[index].Id != songs[index].Id)
            {
                return false;
            }
        }

        return true;
    }

    public static SongDto? ResolveCurrentSong(IPlaybackService playbackService, IReadOnlyList<SongDto> songs)
    {
        var currentSongId = playbackService.CurrentSong?.Id;
        if (!currentSongId.HasValue)
        {
            return null;
        }

        return songs.FirstOrDefault(song => song.Id == currentSongId.Value);
    }

    public static bool HasCurrentSongOutsideQueue(IPlaybackService playbackService, IReadOnlyList<SongDto> songs)
    {
        var currentSongId = playbackService.CurrentSong?.Id;
        return currentSongId.HasValue && !songs.Any(song => song.Id == currentSongId.Value);
    }

    /// <summary>
    /// The index of the playing song, or -1 when it is not in this list.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="ResolveCurrentSongIndex"/>, which answers 0 for "not found" because
    /// its caller is starting a queue and the top is the right place to start. A caller that SCROLLS
    /// needs the two apart: falling back to 0 would jump the list to the top whenever the playing
    /// song is filtered out, which reads as the list losing its place.
    /// </remarks>
    public static int TryResolveCurrentSongIndex(IPlaybackService playbackService, IReadOnlyList<SongDto> songs)
    {
        var currentSongId = playbackService.CurrentSong?.Id;
        if (!currentSongId.HasValue)
        {
            return -1;
        }

        for (var index = 0; index < songs.Count; index++)
        {
            if (songs[index].Id == currentSongId.Value)
            {
                return index;
            }
        }

        return -1;
    }

    public static int ResolveCurrentSongIndex(IPlaybackService playbackService, IReadOnlyList<SongDto> songs)
    {
        var currentSongId = playbackService.CurrentSong?.Id;
        if (!currentSongId.HasValue)
        {
            return 0;
        }

        for (var index = 0; index < songs.Count; index++)
        {
            if (songs[index].Id == currentSongId.Value)
            {
                return index;
            }
        }

        return 0;
    }
}
