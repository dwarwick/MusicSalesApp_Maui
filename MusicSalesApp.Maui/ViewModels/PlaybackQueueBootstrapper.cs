using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.ViewModels;

internal static class PlaybackQueueBootstrapper
{
    public static async Task<bool> StartQueueAsync(
        IReadOnlyList<SongDto> songs,
        IMediaPlaybackOnboardingService mediaPlaybackOnboardingService,
        IPlaybackService playbackService,
        SongDto? startSong = null)
    {
        if (songs.Count == 0)
        {
            return false;
        }

        await mediaPlaybackOnboardingService.EnsureBackgroundPlaybackExplainedAsync();

        var startIndex = ResolveStartIndex(songs, startSong);
        playbackService.SetPlaylist(songs.ToList(), startIndex);
        return true;
    }

    private static int ResolveStartIndex(IReadOnlyList<SongDto> songs, SongDto? startSong)
    {
        if (startSong == null)
        {
            return 0;
        }

        for (var index = 0; index < songs.Count; index++)
        {
            if (songs[index].Id == startSong.Id)
            {
                return index;
            }
        }

        return 0;
    }
}