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
    private readonly IMediaPlaybackOnboardingService _mediaPlaybackOnboardingService;
    private readonly ISignalRService _signalRService;
    private readonly IAppConfig _appConfig;
    private readonly IBillingService _billingService;

    public SongPlayerViewModel(
        IMusicService musicService,
        IAlertService alertService,
        IAuthService authService,
        INavigationService navigationService,
        IPlaybackService playbackService,
        IMediaPlaybackOnboardingService mediaPlaybackOnboardingService,
        ISignalRService signalRService,
        IAppConfig appConfig,
        IBillingService billingService)
    {
        _musicService = musicService;
        _alertService = alertService;
        _authService = authService;
        _navigationService = navigationService;
        _playbackService = playbackService;
        _mediaPlaybackOnboardingService = mediaPlaybackOnboardingService;
        _signalRService = signalRService;
        _appConfig = appConfig;
        _billingService = billingService;

        _signalRService.OnLikeCountUpdated += HandleLikeCountUpdated;
        _playbackService.ShowSubscribeCtaRequested += OnShowSubscribeCta;
    }

    public IPlaybackService PlaybackService => _playbackService;

    [ObservableProperty]
    public partial SongDto? Song { get; set; }

    [ObservableProperty]
    public partial bool HasActiveSubscription { get; set; }

    [ObservableProperty]
    public partial bool IsRefreshing { get; set; }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (Song == null) { IsRefreshing = false; return; }

        IsRefreshing = true;
        try
        {
            HasActiveSubscription = _authService.HasActiveSubscription;

            if (_authService.IsLoggedIn)
            {
                try
                {
                    var statuses = await _musicService.GetBulkUserLikeStatusAsync([Song.Id]);
                    if (statuses.TryGetValue(Song.Id, out var status))
                        Song.UserLikeStatus = status;
                }
                catch { /* Non-fatal */ }

                try
                {
                    var likeCounts = await _musicService.GetBulkLikeCountsAsync([Song.Id]);
                    var lc = likeCounts.FirstOrDefault(l => l.SongMetadataId == Song.Id);
                    if (lc != null)
                    {
                        Song.LikeCount = lc.LikeCount;
                        Song.DislikeCount = lc.DislikeCount;
                    }
                }
                catch { /* Non-fatal */ }
            }
        }
        finally
        {
            IsRefreshing = false;
        }
    }

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

        var isCurrentSong = _playbackService.CurrentSong?.Id == song.Id;

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

        // Preserve current playback state when navigation lands on the active song.
        if (isCurrentSong)
        {
            return;
        }

        await _mediaPlaybackOnboardingService.EnsureBackgroundPlaybackExplainedAsync();

        // Start playback for a different song.
        _playbackService.PlaySong(song);
    }

    [RelayCommand]
    private async Task PlaySongAsync()
    {
        await PlayDisplayedSongQueueAsync();
    }

    public Task<bool> PlayDisplayedSongQueueAsync()
    {
        if (Song == null)
        {
            return Task.FromResult(false);
        }

        return PlaybackQueueBootstrapper.StartQueueAsync(
            [Song],
            _mediaPlaybackOnboardingService,
            _playbackService,
            Song);
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

    [RelayCommand]
    private async Task ReportSongAsync()
    {
        if (Song == null) return;
        if (!await RequireValidatedUserAsync("report songs")) return;

        var reason = await _alertService.ShowActionSheetAsync(
            "Report Song", "Cancel", null,
            "Copyright Violation", "Terms of Use Violation");

        if (string.IsNullOrEmpty(reason) || reason == "Cancel")
            return;

        try
        {
            var success = await _musicService.ReportSongAsync(Song.Id, reason);
            await _alertService.DisplayAlertAsync(
                success ? "Report Submitted" : "Error",
                success ? "Thank you. Your report has been submitted for review."
                        : "Failed to submit report. Please try again later.",
                "OK");
        }
        catch (InvalidOperationException ex)
        {
            await _alertService.DisplayAlertAsync("Already Reported", ex.Message, "OK");
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

    private async Task<bool> RequireValidatedUserAsync(string action)
    {
        if (!await RequireAuthenticatedUserAsync(action))
            return false;

        if (!_authService.Roles.Contains("User"))
        {
            await _alertService.DisplayAlertAsync("Not Authorized",
                "Your account must be fully verified to " + action + ".", "OK");
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

    private async Task OnShowSubscribeCta()
    {
        var subscribe = await _alertService.ShowConfirmAsync("Preview Limit",
            "Subscribe for unlimited listening!", "Subscribe Now", "Not Now");

        if (!subscribe)
            return;

        var result = await _billingService.PurchaseSubscriptionAsync();

        if (!result.Success)
        {
            if (result.ErrorMessage != "Purchase was cancelled.")
                await _alertService.DisplayAlertAsync("Subscribe", result.ErrorMessage ?? "Purchase failed.", "OK");
            return;
        }

        var verificationResult = await _musicService.VerifyGooglePlayPurchaseAsync(result.PurchaseToken!, result.OrderId);

        if (verificationResult.Success)
        {
            await _authService.RefreshUserStatusAsync();
            _playbackService.HandleSubscriptionActivated();
            HasActiveSubscription = true;
            await _alertService.DisplayAlertAsync("Success", "You're now subscribed! Enjoy unlimited music.", "OK");
        }
        else
        {
            var errorMessage = string.IsNullOrWhiteSpace(verificationResult.ErrorMessage)
                ? "Purchase succeeded but server verification failed. Please try again."
                : verificationResult.ErrorMessage;
            await _alertService.DisplayAlertAsync("Subscribe", errorMessage, "OK");
        }
    }

    [RelayCommand]
    private async Task ViewBioAsync()
    {
        if (Song == null || string.IsNullOrEmpty(Song.ArtistName)) return;

        await _navigationService.GoToAsync("persona", new Dictionary<string, object>
        {
            ["PersonaName"] = Song.ArtistName,
            ["PersonaImageUrl"] = Song.PersonaImageUrl ?? string.Empty,
            ["PersonaBio"] = Song.PersonaBio ?? string.Empty
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

    public void Cleanup()
    {
        _signalRService.OnLikeCountUpdated -= HandleLikeCountUpdated;
        _playbackService.ShowSubscribeCtaRequested -= OnShowSubscribeCta;
    }
}
