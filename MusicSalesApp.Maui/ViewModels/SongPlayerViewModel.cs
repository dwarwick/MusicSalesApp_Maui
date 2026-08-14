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
    private readonly INetworkStatusService? _networkStatusService;
    private readonly ISongArtworkHydrator? _songArtworkHydrator;
    private bool _subscriptionsAttached;

    public SongPlayerViewModel(
        IMusicService musicService,
        IAlertService alertService,
        IAuthService authService,
        INavigationService navigationService,
        IPlaybackService playbackService,
        IMediaPlaybackOnboardingService mediaPlaybackOnboardingService,
        ISignalRService signalRService,
        IAppConfig appConfig,
        IBillingService billingService,
        INetworkStatusService? networkStatusService = null,
        ISongArtworkHydrator? songArtworkHydrator = null)
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
        _networkStatusService = networkStatusService;
        _songArtworkHydrator = songArtworkHydrator;

        AttachSubscriptions();
    }

    public void Activate()
    {
        AttachSubscriptions();
    }

    public Task StartSignalRAsync() => _signalRService.StartAsync();

    private void AttachSubscriptions()
    {
        if (_subscriptionsAttached)
        {
            return;
        }

        _musicService.OnStreamCountRecorded += HandleStreamCountUpdated;
        _signalRService.OnStreamCountUpdated += HandleStreamCountUpdated;
        _signalRService.OnLikeCountUpdated += HandleLikeCountUpdated;
        _playbackService.ShowSubscribeCtaRequested += OnShowSubscribeCta;
        if (_networkStatusService != null)
            _networkStatusService.PropertyChanged += HandleNetworkStatusChanged;
        _subscriptionsAttached = true;
    }

    private void HandleNetworkStatusChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (NetworkStatusChange.AffectsConnectivity(e.PropertyName))
            OnPropertyChanged(nameof(CanUseServerActions));
    }

    /// <summary>
    /// False when the device has no network at all. Reporting a song needs the server, so the control
    /// is hidden rather than left to fail with a generic error on tap. Deliberately not
    /// <see cref="INetworkStatusService.IsOffline"/>, which is also true on a constrained connection
    /// that can still reach the server.
    /// </summary>
    public bool CanUseServerActions => _networkStatusService?.HasNoNetworkAccess != true;

    public IPlaybackService PlaybackService => _playbackService;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCurrentSongPreviewLimited))]
    public partial SongDto? Song { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCurrentSongPreviewLimited))]
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

    public bool IsCurrentSongPreviewLimited => PreviewAccessPolicy.ShouldLimitPreview(_authService, Song);

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
            _ = HydrateArtworkAsync(value);
            _ = LoadSongDetailsAsync(value);
        }
    }

    /// <summary>
    /// Points the song's artwork at its locally cached copy. Best-effort - artwork is decorative.
    /// </summary>
    private async Task HydrateArtworkAsync(SongDto song)
    {
        if (_songArtworkHydrator == null)
            return;

        try
        {
            await _songArtworkHydrator.HydrateAsync([song]);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to hydrate song artwork: {ex.Message}");
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
            _playbackService.SetPlaylist(
                [song],
                0,
                PlaybackQueueStartBehavior.PreserveCurrentSongIfPresent,
                PlaybackQueueDescriptions.SongPage(song));
            return;
        }

        await _mediaPlaybackOnboardingService.EnsureBackgroundPlaybackExplainedAsync();

        // Start playback for a different song and shrink the active queue to this page.
        _playbackService.SetPlaylist([song], 0, PlaybackQueueDescriptions.SongPage(song));
    }

    [RelayCommand]
    private async Task PlaySongAsync()
    {
        if (Song != null
            && PlaybackIndicatorStateResolver.ShouldToggleCurrentSong(Song.Id, _playbackService.CurrentSong))
        {
            _playbackService.TogglePlayPause();
            return;
        }

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
            Song,
            PlaybackQueueDescriptions.SongPage(Song));
    }

    [RelayCommand]
    private async Task LikeSongAsync()
    {
        if (Song == null) return;
        if (!await RequireAuthenticatedUserAsync("like songs")) return;

        await OptimisticLikeStateUpdater.ApplyAsync(_musicService, Song, LikeAction.ThumbsUp);
    }

    [RelayCommand]
    private async Task DislikeSongAsync()
    {
        if (Song == null) return;
        if (!await RequireAuthenticatedUserAsync("dislike songs")) return;

        await OptimisticLikeStateUpdater.ApplyAsync(_musicService, Song, LikeAction.ThumbsDown);
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
                await _navigationService.GoToAsync(NavigationRoutes.LoginEntry);
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

    private void HandleStreamCountUpdated(int songMetadataId, int newCount)
    {
        if (Song != null && Song.Id == songMetadataId)
        {
            Song.StreamCount = newCount;
        }
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
        var isSignedIn = _authService.IsLoggedIn;
        var accepted = await _alertService.ShowConfirmAsync(
            SubscriptionPurchaseGate.PreviewLimitTitle,
            SubscriptionPurchaseGate.PreviewLimitMessage(isSignedIn),
            SubscriptionPurchaseGate.PreviewLimitAccept(isSignedIn),
            SubscriptionPurchaseGate.PreviewLimitDecline);

        if (!accepted)
            return;

        if (!isSignedIn)
        {
            await SubscriptionPurchaseGate.GoToSignInAsync(_navigationService);
            return;
        }

        var result = await _billingService.PurchaseSubscriptionAsync();

        if (!result.Success)
        {
            if (result.ErrorMessage != "Purchase was cancelled.")
                await _alertService.DisplayAlertAsync("Subscribe", result.ErrorMessage ?? "Purchase failed.", "OK");
            return;
        }

        var verificationResult = await _musicService.VerifySubscriptionPurchaseAsync(result.ToVerificationRequest());

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
            // Prefer the cached copy so the persona page renders offline too, and the larger
            // rendition because the page shows the image at 120 DIP - 360 px on a 3x screen, which
            // the 320 px thumb would visibly soften.
            ["PersonaImageUrl"] = Song.PersonaImageHeroDisplaySource ?? string.Empty,
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
        if (!_subscriptionsAttached)
        {
            return;
        }

        _musicService.OnStreamCountRecorded -= HandleStreamCountUpdated;
        _signalRService.OnStreamCountUpdated -= HandleStreamCountUpdated;
        _signalRService.OnLikeCountUpdated -= HandleLikeCountUpdated;
        _playbackService.ShowSubscribeCtaRequested -= OnShowSubscribeCta;
        if (_networkStatusService != null)
            _networkStatusService.PropertyChanged -= HandleNetworkStatusChanged;
        _subscriptionsAttached = false;
    }
}
