namespace MusicSalesApp.Maui.Services;

/// <summary>
/// Coordinates empty-state transport actions in the now playing drawer.
/// If no song is active, it can ask the hosting page to queue the visible songs
/// before applying the requested transport toggle.
/// </summary>
public sealed class NowPlayingEmptyStateActionRunner
{
    public Task ToggleRepeatAsync(IPlaybackService? playbackService, Func<Task<bool>>? queueFromEmptyStateAsync) =>
        RunAsync(playbackService, queueFromEmptyStateAsync, service => service.ToggleRepeat());

    public Task ToggleShuffleAsync(IPlaybackService? playbackService, Func<Task<bool>>? queueFromEmptyStateAsync) =>
        RunAsync(playbackService, queueFromEmptyStateAsync, service => service.ToggleShuffle());

    private static async Task RunAsync(
        IPlaybackService? playbackService,
        Func<Task<bool>>? queueFromEmptyStateAsync,
        Action<IPlaybackService> action)
    {
        if (playbackService == null)
        {
            return;
        }

        if (playbackService.CurrentSong == null)
        {
            if (queueFromEmptyStateAsync == null)
            {
                return;
            }

            var queued = await queueFromEmptyStateAsync();
            if (!queued)
            {
                return;
            }
        }

        action(playbackService);
    }
}