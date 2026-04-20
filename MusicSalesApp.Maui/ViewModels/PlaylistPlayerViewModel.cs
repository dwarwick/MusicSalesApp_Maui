using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.ViewModels;

[QueryProperty(nameof(GenreName), "GenreName")]
[QueryProperty(nameof(ArtistName), "ArtistName")]
public partial class PlaylistPlayerViewModel : ObservableObject
{
    private readonly IMusicService _musicService;
    private readonly IAlertService _alertService;
    private readonly IAuthService _authService;
    private readonly INavigationService _navigationService;
    private readonly IPlaybackService _playbackService;
    private readonly ISignalRService _signalRService;
    private readonly IAppConfig _appConfig;
    private readonly IBillingService _billingService;

    public PlaylistPlayerViewModel(
        IMusicService musicService,
        IAlertService alertService,
        IAuthService authService,
        INavigationService navigationService,
        IPlaybackService playbackService,
        ISignalRService signalRService,
        IAppConfig appConfig,
        IBillingService billingService)
    {
        _musicService = musicService;
        _alertService = alertService;
        _authService = authService;
        _navigationService = navigationService;
        _playbackService = playbackService;
        _signalRService = signalRService;
        _appConfig = appConfig;
        _billingService = billingService;

        _signalRService.OnStreamCountUpdated += HandleStreamCountUpdated;
        _signalRService.OnLikeCountUpdated += HandleLikeCountUpdated;
        _playbackService.StateChanged += OnPlaybackStateChanged;
        _playbackService.ShowSubscribeCtaRequested += OnShowSubscribeCta;
    }

    public IPlaybackService PlaybackService => _playbackService;

    public ObservableCollection<SongDto> Songs { get; } = [];

    [ObservableProperty]
    public partial string PlaylistTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial SongDto? CurrentSong { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial bool HasActiveSubscription { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial string? GenreName { get; set; }

    [ObservableProperty]
    public partial string? ArtistName { get; set; }

    partial void OnGenreNameChanged(string? value)
    {
        if (!string.IsNullOrEmpty(value))
            _ = LoadPlaylistAsync();
    }

    partial void OnArtistNameChanged(string? value)
    {
        if (!string.IsNullOrEmpty(value))
            _ = LoadPlaylistAsync();
    }

    public string ShareUrl => CurrentSong?.ShareUrl ?? string.Empty;

    // --- Refresh ---

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsLoading = true;
        try
        {
            await LoadPlaylistCoreAsync();
        }
        finally
        {
            IsLoading = false;
        }
    }

    internal async Task LoadPlaylistAsync()
    {
        if (IsLoading) return;

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            await LoadPlaylistCoreAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load songs: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadPlaylistCoreAsync()
    {
        ErrorMessage = null;

        var allSongs = await _musicService.GetSongsAsync();
        List<SongDto> filtered;

        if (!string.IsNullOrEmpty(GenreName))
        {
            var decodedGenre = Uri.UnescapeDataString(GenreName);
            PlaylistTitle = decodedGenre;
            filtered = allSongs
                .Where(s => string.Equals(s.Genre, decodedGenre, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        else if (!string.IsNullOrEmpty(ArtistName))
        {
            var decodedArtist = Uri.UnescapeDataString(ArtistName);
            PlaylistTitle = decodedArtist;
            filtered = allSongs
                .Where(s => string.Equals(s.ArtistName, decodedArtist, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        else
        {
            filtered = [];
        }

        if (filtered.Count == 0)
        {
            ErrorMessage = !string.IsNullOrEmpty(GenreName)
                ? $"No songs found for genre \"{Uri.UnescapeDataString(GenreName)}\"."
                : $"No songs found for artist \"{Uri.UnescapeDataString(ArtistName!)}\".";
            return;
        }

        // Set share URLs
        foreach (var song in filtered)
            song.ShareUrl = SongDto.BuildShareUrl(song.Id, _appConfig.WebBaseUrl);

        // Load like counts and user status
        await Task.WhenAll(
            LoadLikeCountsAsync(filtered),
            LoadUserLikeStatusAsync(filtered));

        Songs.Clear();
        foreach (var song in filtered)
            Songs.Add(song);

        HasActiveSubscription = _authService.HasActiveSubscription;

        // Start playback
        _playbackService.SetPlaylist(filtered, 0);
        CurrentSong = _playbackService.CurrentSong;
        OnPropertyChanged(nameof(ShareUrl));
    }

    // --- Playback commands ---

    [RelayCommand]
    private void PlayTrack(SongDto? song)
    {
        if (song == null) return;
        var index = Songs.IndexOf(song);
        if (index >= 0)
            _playbackService.PlayTrackAtIndex(index);
    }

    // --- Like/Dislike ---

    [RelayCommand]
    private async Task LikeSongAsync()
    {
        if (CurrentSong == null) return;
        if (!await RequireAuthenticatedUserAsync("like songs")) return;

        var result = await _musicService.ToggleLikeAsync(CurrentSong.Id);
        if (result != null)
        {
            CurrentSong.UserLikeStatus = result.IsLiked ? true : null;
            CurrentSong.LikeCount = result.LikeCount;
            CurrentSong.DislikeCount = result.DislikeCount;
        }
    }

    [RelayCommand]
    private async Task DislikeSongAsync()
    {
        if (CurrentSong == null) return;
        if (!await RequireAuthenticatedUserAsync("dislike songs")) return;

        var result = await _musicService.ToggleDislikeAsync(CurrentSong.Id);
        if (result != null)
        {
            CurrentSong.UserLikeStatus = result.IsDisliked ? false : null;
            CurrentSong.LikeCount = result.LikeCount;
            CurrentSong.DislikeCount = result.DislikeCount;
        }
    }

    // --- Navigation ---

    [RelayCommand]
    private async Task ViewBioAsync()
    {
        if (CurrentSong == null || string.IsNullOrEmpty(CurrentSong.ArtistName)) return;

        await _navigationService.GoToAsync("persona", new Dictionary<string, object>
        {
            ["PersonaName"] = CurrentSong.ArtistName,
            ["PersonaImageUrl"] = CurrentSong.PersonaImageUrl ?? string.Empty,
            ["PersonaBio"] = CurrentSong.PersonaBio ?? string.Empty
        });
    }

    [RelayCommand]
    private async Task NavigateToGenreAsync(string? genre)
    {
        if (string.IsNullOrEmpty(genre)) return;
        await _navigationService.GoToAsync("playlist-player", new Dictionary<string, object>
        {
            ["GenreName"] = genre
        });
    }

    [RelayCommand]
    private async Task NavigateToArtistAsync(string? artist)
    {
        if (string.IsNullOrEmpty(artist)) return;
        await _navigationService.GoToAsync("playlist-player", new Dictionary<string, object>
        {
            ["ArtistName"] = artist
        });
    }

    // --- Auth helpers ---

    internal async Task<bool> RequireAuthenticatedUserAsync(string action)
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

    // --- Data loading helpers ---

    private async Task LoadLikeCountsAsync(List<SongDto> songs)
    {
        try
        {
            var ids = songs.Select(s => s.Id).ToList();
            if (ids.Count == 0) return;

            var likeCounts = await _musicService.GetBulkLikeCountsAsync(ids);
            foreach (var lc in likeCounts)
            {
                var song = songs.FirstOrDefault(s => s.Id == lc.SongMetadataId);
                if (song != null)
                {
                    song.LikeCount = lc.LikeCount;
                    song.DislikeCount = lc.DislikeCount;
                }
            }
        }
        catch
        {
            // Non-fatal
        }
    }

    private async Task LoadUserLikeStatusAsync(List<SongDto> songs)
    {
        if (!_authService.IsLoggedIn) return;

        try
        {
            var ids = songs.Select(s => s.Id).ToList();
            if (ids.Count == 0) return;

            var statuses = await _musicService.GetBulkUserLikeStatusAsync(ids);
            foreach (var (songId, status) in statuses)
            {
                var song = songs.FirstOrDefault(s => s.Id == songId);
                if (song != null)
                    song.UserLikeStatus = status;
            }
        }
        catch
        {
            // Non-fatal
        }
    }

    // --- Real-time updates ---

    private void OnPlaybackStateChanged(string propertyName)
    {
        if (propertyName == nameof(IPlaybackService.CurrentSong))
        {
            CurrentSong = _playbackService.CurrentSong;
            OnPropertyChanged(nameof(ShareUrl));
        }
    }

    private void HandleStreamCountUpdated(int songMetadataId, int newCount)
    {
        var song = Songs.FirstOrDefault(s => s.Id == songMetadataId);
        if (song != null)
            song.StreamCount = newCount;
    }

    private void HandleLikeCountUpdated(int songMetadataId, int likeCount, int dislikeCount)
    {
        var song = Songs.FirstOrDefault(s => s.Id == songMetadataId);
        if (song != null)
        {
            song.LikeCount = likeCount;
            song.DislikeCount = dislikeCount;
        }
    }

    private async Task OnShowSubscribeCta()
    {
        var subscribe = await _alertService.ShowConfirmAsync("Preview Limit",
            "Subscribe for unlimited listening!", "Subscribe Now", "Not Now");

        if (subscribe)
        {
            var result = await _billingService.PurchaseSubscriptionAsync();

            if (!result.Success)
            {
                if (result.ErrorMessage != "Purchase was cancelled.")
                    await _alertService.DisplayAlertAsync("Subscribe", result.ErrorMessage ?? "Purchase failed.", "OK");
                return;
            }

            var verified = await _musicService.VerifyGooglePlayPurchaseAsync(result.PurchaseToken!, result.OrderId);

            if (verified)
            {
                await _authService.RefreshUserStatusAsync();
                HasActiveSubscription = true;
                await _alertService.DisplayAlertAsync("Success", "You're now subscribed! Enjoy unlimited music.", "OK");
            }
            else
            {
                await _alertService.DisplayAlertAsync("Subscribe", "Purchase succeeded but server verification failed. Please try again.", "OK");
            }
        }
    }

    public void Cleanup()
    {
        _signalRService.OnStreamCountUpdated -= HandleStreamCountUpdated;
        _signalRService.OnLikeCountUpdated -= HandleLikeCountUpdated;
        _playbackService.StateChanged -= OnPlaybackStateChanged;
        _playbackService.ShowSubscribeCtaRequested -= OnShowSubscribeCta;
    }
}
