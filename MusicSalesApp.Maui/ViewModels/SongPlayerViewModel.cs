using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.ViewModels;

[QueryProperty(nameof(Song), "Song")]
[QueryProperty(nameof(SongTitle), "SongTitle")]
public partial class SongPlayerViewModel : ObservableObject
{
    private readonly IMusicService _musicService;
    private readonly IAlertService _alertService;
    private readonly IAuthService _authService;
    private readonly INavigationService _navigationService;
    private readonly IPlaybackService _playbackService;
    private readonly ISignalRService _signalRService;
    private readonly IAppConfig _appConfig;

    public SongPlayerViewModel(
        IMusicService musicService,
        IAlertService alertService,
        IAuthService authService,
        INavigationService navigationService,
        IPlaybackService playbackService,
        ISignalRService signalRService,
        IAppConfig appConfig)
    {
        _musicService = musicService;
        _alertService = alertService;
        _authService = authService;
        _navigationService = navigationService;
        _playbackService = playbackService;
        _signalRService = signalRService;
        _appConfig = appConfig;

        _signalRService.OnLikeCountUpdated += HandleLikeCountUpdated;
    }

    public IPlaybackService PlaybackService => _playbackService;

    [ObservableProperty]
    public partial SongDto? Song { get; set; }

    [ObservableProperty]
    public partial bool HasActiveSubscription { get; set; }

    [ObservableProperty]
    public partial string? SongTitle { get; set; }

    partial void OnSongTitleChanged(string? value)
    {
        // Deep link: load song by title when navigated via URL
        if (!string.IsNullOrEmpty(value) && Song == null)
        {
            _ = LoadSongByTitleAsync(value);
        }
    }

    public string ShareUrl => Song?.ShareUrl ?? string.Empty;

    partial void OnSongChanged(SongDto? value)
    {
        if (value != null)
        {
            // Ensure ShareUrl is set (may not be if loaded via deep link)
            if (string.IsNullOrEmpty(value.ShareUrl))
            {
                value.ShareUrl = SongDto.BuildShareUrl(value.Id, _appConfig.WebBaseUrl);
            }
            OnPropertyChanged(nameof(ShareUrl));
            _ = LoadSongDetailsAsync(value);
        }
    }

    private async Task LoadSongDetailsAsync(SongDto song)
    {
        HasActiveSubscription = _authService.HasActiveSubscription;

        // Load user like status if logged in
        if (_authService.IsLoggedIn)
        {
            try
            {
                var statuses = await _musicService.GetBulkUserLikeStatusAsync([song.Id]);
                if (statuses.TryGetValue(song.Id, out var status))
                {
                    song.UserLikeStatus = status;
                }
            }
            catch
            {
                // Non-fatal
            }
        }

        // Start playback
        _playbackService.PlaySong(song);
    }

    [RelayCommand]
    private void PlaySong()
    {
        if (Song != null)
            _playbackService.PlaySong(Song);
    }

    [RelayCommand]
    private async Task LikeSongAsync()
    {
        if (Song == null) return;
        if (!await RequireAuthenticatedUserAsync("like songs")) return;

        var result = await _musicService.ToggleLikeAsync(Song.Id);
        if (result != null)
        {
            Song.UserLikeStatus = result.IsLiked ? true : null;
            Song.LikeCount = result.LikeCount;
            Song.DislikeCount = result.DislikeCount;
        }
    }

    [RelayCommand]
    private async Task DislikeSongAsync()
    {
        if (Song == null) return;
        if (!await RequireAuthenticatedUserAsync("dislike songs")) return;

        var result = await _musicService.ToggleDislikeAsync(Song.Id);
        if (result != null)
        {
            Song.UserLikeStatus = result.IsDisliked ? false : null;
            Song.LikeCount = result.LikeCount;
            Song.DislikeCount = result.DislikeCount;
        }
    }

    private async Task<bool> RequireAuthenticatedUserAsync(string action)
    {
        if (!_authService.IsLoggedIn)
        {
            bool goToLogin = await _alertService.ShowConfirmAsync("Login Required",
                $"Please log in to {action}.", "Login", "Cancel");
            if (goToLogin)
                await _navigationService.GoToAsync("login");
            return false;
        }

        if (!_authService.EmailConfirmed)
        {
            await _alertService.DisplayAlertAsync("Email Not Verified",
                "Please verify your email before you can interact with songs.", "OK");
            return false;
        }

        return true;
    }

    private void HandleLikeCountUpdated(int songMetadataId, int likeCount, int dislikeCount)
    {
        if (Song != null && Song.Id == songMetadataId)
        {
            Song.LikeCount = likeCount;
            Song.DislikeCount = dislikeCount;
        }
    }

    /// <summary>
    /// Loads a song by title (for deep linking).
    /// </summary>
    public async Task LoadSongByTitleAsync(string title)
    {
        try
        {
            var song = await _musicService.GetSongByTitleAsync(title);
            if (song != null)
            {
                Song = song;
            }
            else
            {
                await _alertService.DisplayAlertAsync("Song Not Found",
                    $"Could not find the song \"{title}\".", "OK");
            }
        }
        catch (Exception ex)
        {
            await _alertService.DisplayAlertAsync("Error",
                $"Failed to load song: {ex.Message}", "OK");
        }
    }
}
