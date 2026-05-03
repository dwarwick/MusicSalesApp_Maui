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