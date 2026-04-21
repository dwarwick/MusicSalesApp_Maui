using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Services;

public class AddToPlaylistHandler : IAddToPlaylistHandler
{
    internal const string NewPlaylistOption = "+ New playlist\u2026";
    internal const string SubscribeTitle = "Subscription required";
    internal const string SubscribeMessage =
        "A subscription is required to save songs to playlists. Subscribe on the Account page to start building playlists.";
    internal const string LoginTitle = "Log in to save songs";
    internal const string LoginMessage =
        "Log in and subscribe to save songs to your playlists.";

    private readonly IAuthService _authService;
    private readonly IPlaylistService _playlistService;
    private readonly IAlertService _alertService;

    public AddToPlaylistHandler(
        IAuthService authService,
        IPlaylistService playlistService,
        IAlertService alertService)
    {
        _authService = authService;
        _playlistService = playlistService;
        _alertService = alertService;
    }

    public async Task ShowAsync(int songMetadataId, string songTitle)
    {
        if (songMetadataId <= 0) return;

        if (!_authService.IsLoggedIn)
        {
            await _alertService.DisplayAlertAsync(LoginTitle, LoginMessage, "OK");
            return;
        }

        if (!_authService.HasActiveSubscription)
        {
            await _alertService.DisplayAlertAsync(SubscribeTitle, SubscribeMessage, "OK");
            return;
        }

        var playlists = await _playlistService.GetMyPlaylistsAsync();
        var customPlaylists = playlists
            .Where(p => !p.IsSystemGenerated)
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Filter out playlists that already contain this song (parallel fetch)
        var songCheckTasks = customPlaylists.Select(async p =>
        {
            var songsDto = await _playlistService.GetPlaylistSongsAsync(p.Id);
            var alreadyAdded = songsDto?.Songs.Any(s => s.SongMetadataId == songMetadataId) == true;
            return (Playlist: p, AlreadyAdded: alreadyAdded);
        });
        var songChecks = await Task.WhenAll(songCheckTasks);
        var availablePlaylists = songChecks
            .Where(r => !r.AlreadyAdded)
            .Select(r => r.Playlist)
            .ToList();

        var buttons = new List<string>(availablePlaylists.Select(p => p.Name)) { NewPlaylistOption };

        var title = string.IsNullOrWhiteSpace(songTitle)
            ? "Add to playlist"
            : $"Add \"{songTitle}\" to playlist";
        var choice = await _alertService.ShowActionSheetAsync(title, "Cancel", null, [.. buttons]);
        if (string.IsNullOrEmpty(choice) || choice == "Cancel") return;

        int targetPlaylistId;
        string targetName;
        if (choice == NewPlaylistOption)
        {
            var newName = await _alertService.ShowPromptAsync(
                "New playlist",
                "Enter a name for your new playlist:",
                accept: "Create",
                cancel: "Cancel",
                placeholder: "Playlist name",
                maxLength: 100);
            if (string.IsNullOrWhiteSpace(newName)) return;

            var createResult = await _playlistService.CreatePlaylistAsync(newName.Trim());
            if (!createResult.Success || createResult.Value is null)
            {
                var msg = createResult.RequiresSubscription
                    ? SubscribeMessage
                    : createResult.ErrorMessage ?? "Failed to create playlist.";
                await _alertService.DisplayAlertAsync("Couldn't create playlist", msg, "OK");
                return;
            }
            targetPlaylistId = createResult.Value.Id;
            targetName = createResult.Value.Name;
        }
        else
        {
            var picked = availablePlaylists.FirstOrDefault(p => p.Name == choice);
            if (picked == null) return;
            targetPlaylistId = picked.Id;
            targetName = picked.Name;
        }

        var addResult = await _playlistService.AddSongAsync(targetPlaylistId, songMetadataId);
        if (!addResult.Success)
        {
            var msg = addResult.RequiresSubscription
                ? SubscribeMessage
                : addResult.ErrorMessage ?? "Failed to add song to playlist.";
            await _alertService.DisplayAlertAsync("Couldn't add song", msg, "OK");
            return;
        }

        await _alertService.DisplayAlertAsync(
            "Added to playlist",
            $"\"{songTitle}\" was added to \"{targetName}\".",
            "OK");
    }
}
