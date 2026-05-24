using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Configuration;
using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.ViewModels;

public partial class HomeViewModel : ObservableObject
{
    private readonly IAuthService _authService;
    private readonly IAppSettingsService _appSettingsService;
    private readonly INavigationService _navigationService;
    private readonly IAlertService _alertService;
    private readonly IAppConfig _appConfig;
    private readonly IBillingService _billingService;
    private readonly IMusicService _musicService;
    private readonly ISignalRService _signalRService;
    private readonly IPlaybackService _playbackService;
    private readonly IMediaPlaybackOnboardingService _mediaPlaybackOnboardingService;
    private readonly IBrowserService _browserService;
    private readonly IConfiguration _configuration;
    private readonly IPlaylistService _playlistService;
    private bool _signalRSubscriptionsAttached;

    private const string DefaultAppleSubscriptionManagementUrl = "https://account.apple.com/account/manage/section/subscriptions";

    [ObservableProperty]
    public partial bool IsLoading { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SubscribeButtonText))]
    [NotifyPropertyChangedFor(nameof(ShowLoginRegister))]
    [NotifyPropertyChangedFor(nameof(ShowValidateEmail))]
    [NotifyPropertyChangedFor(nameof(ShowSubscribeNow))]
    [NotifyPropertyChangedFor(nameof(ShowArtistUploadHero))]
    public partial bool IsAuthenticated { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSubscriptionContent))]
    [NotifyPropertyChangedFor(nameof(ShowSubscribeNow))]
    public partial bool HasActiveSubscription { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowValidateEmail))]
    [NotifyPropertyChangedFor(nameof(ShowSubscribeNow))]
    public partial bool IsEmailVerified { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowArtistUploadHero))]
    public partial bool IsCreator { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SubscribeButtonText))]
    public partial string SubscriptionPrice { get; set; } = "3.99";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowFeaturedMusic))]
    public partial ObservableCollection<SongDto> FeaturedSongs { get; set; } = new();

    public bool ShowSubscriptionContent => !HasActiveSubscription;
    public bool ShowLoginRegister => !IsAuthenticated;
    public bool ShowValidateEmail => IsAuthenticated && !IsEmailVerified;
    public bool ShowSubscribeNow => IsAuthenticated && IsEmailVerified && !HasActiveSubscription;
    public bool ShowBrowseMusic => true;
    public bool ShowFeaturedMusic => FeaturedSongs.Count > 0;
    public bool ShowArtistUploadHero => !(IsAuthenticated && IsCreator);

    public string SubscribeButtonText => $"Subscribe Now — ${SubscriptionPrice}/mo";
    public string ManageSubscriptionText => ShouldUseAppleSubscriptionManagement
        ? "Manage subscription with Apple ›"
        : "Manage subscription in Google Play ›";
    public string SubscriptionAutoRenewalText => ShouldUseAppleSubscriptionManagement
        ? "Subscription automatically renews monthly. Cancel anytime from your Apple Account subscription settings."
        : "Subscription automatically renews monthly. Cancel anytime from your Google Play subscription settings.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPlaylists))]
    [NotifyPropertyChangedFor(nameof(ShowRecommended))]
    public partial PlaylistDto? RecommendedPlaylist { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPlaylists))]
    [NotifyPropertyChangedFor(nameof(ShowLikedSongs))]
    public partial PlaylistDto? LikedSongsPlaylist { get; set; }

    public bool ShowPlaylists => IsAuthenticated && IsEmailVerified
        && (RecommendedPlaylist != null || LikedSongsPlaylist != null);

    public bool ShowRecommended => RecommendedPlaylist != null;
    public bool ShowLikedSongs => LikedSongsPlaylist != null;

    public HomeViewModel(
        IAuthService authService,
        IAppSettingsService appSettingsService,
        INavigationService navigationService,
        IAlertService alertService,
        IAppConfig appConfig,
        IBillingService billingService,
        IMusicService musicService,
        ISignalRService signalRService,
        IPlaybackService playbackService,
        IMediaPlaybackOnboardingService mediaPlaybackOnboardingService,
        IBrowserService browserService,
        IConfiguration configuration,
        IPlaylistService playlistService)
    {
        _authService = authService;
        _appSettingsService = appSettingsService;
        _navigationService = navigationService;
        _alertService = alertService;
        _appConfig = appConfig;
        _billingService = billingService;
        _musicService = musicService;
        _signalRService = signalRService;
        _playbackService = playbackService;
        _mediaPlaybackOnboardingService = mediaPlaybackOnboardingService;
        _browserService = browserService;
        _configuration = configuration;
        _playlistService = playlistService;

        _authService.AuthStateChanged += OnAuthStateChanged;
        AttachSignalRSubscriptions();
    }

    public void Activate()
    {
        AttachSignalRSubscriptions();
    }

    public Task StartSignalRAsync() => _signalRService.StartAsync();

    public void Cleanup()
    {
        if (!_signalRSubscriptionsAttached)
        {
            return;
        }

        _musicService.OnStreamCountRecorded -= HandleStreamCountUpdated;
        _signalRService.OnStreamCountUpdated -= HandleStreamCountUpdated;
        _signalRService.OnLikeCountUpdated -= HandleLikeCountUpdated;
        _signalRSubscriptionsAttached = false;
    }

    private void AttachSignalRSubscriptions()
    {
        if (_signalRSubscriptionsAttached)
        {
            return;
        }

        _musicService.OnStreamCountRecorded += HandleStreamCountUpdated;
        _signalRService.OnStreamCountUpdated += HandleStreamCountUpdated;
        _signalRService.OnLikeCountUpdated += HandleLikeCountUpdated;
        _signalRSubscriptionsAttached = true;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            SubscriptionPrice = await _appSettingsService.GetSubscriptionPriceAsync();
            OnPropertyChanged(nameof(SubscribeButtonText));
            RefreshAuthState();
            await LoadStreamQualifyingSecondsAsync();
            await LoadHomePlaylistsAsync();
            await LoadFeaturedSongsAsync();
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadHomePlaylistsAsync()
    {
        if (!_authService.IsLoggedIn || !_authService.EmailConfirmed)
        {
            RecommendedPlaylist = null;
            LikedSongsPlaylist = null;
            return;
        }

        var home = await _playlistService.GetHomePlaylistsAsync();
        RecommendedPlaylist = home?.Recommended;
        LikedSongsPlaylist = home?.LikedSongs;
    }

    private async Task LoadFeaturedSongsAsync()
    {
        var songs = await _musicService.GetSongsAsync();
        foreach (var song in songs)
        {
            song.ShareUrl = SongDto.BuildShareUrl(song.Id, _appConfig.WebBaseUrl);
        }

        var featuredSongs = songs
            .Where(song => song.DisplayOnHomePage)
            .ToList();

        featuredSongs = SongDisplayOrderSorter.OrderForLibrary(featuredSongs);

        await Task.WhenAll(
            LoadLikeCountsAsync(featuredSongs),
            LoadUserLikeStatusAsync(featuredSongs));

        FeaturedSongs = new ObservableCollection<SongDto>(featuredSongs);
    }

    private async Task LoadStreamQualifyingSecondsAsync()
    {
        var seconds = await _musicService.GetStreamQualifyingSecondsAsync();
        _playbackService.SetStreamQualifyingSeconds(seconds);
    }

    private async Task LoadLikeCountsAsync(List<SongDto> songs)
    {
        if (songs.Count == 0) return;

        var counts = await _musicService.GetBulkLikeCountsAsync(songs.Select(song => song.Id));
        foreach (var count in counts)
        {
            var song = songs.FirstOrDefault(item => item.Id == count.SongMetadataId);
            if (song == null) continue;

            song.LikeCount = count.LikeCount;
            song.DislikeCount = count.DislikeCount;
        }
    }

    private async Task LoadUserLikeStatusAsync(List<SongDto> songs)
    {
        if (!_authService.IsLoggedIn || songs.Count == 0) return;

        var statuses = await _musicService.GetBulkUserLikeStatusAsync(songs.Select(song => song.Id));
        foreach (var (songId, status) in statuses)
        {
            var song = songs.FirstOrDefault(item => item.Id == songId);
            if (song != null)
            {
                song.UserLikeStatus = status;
            }
        }
    }

    private void HandleStreamCountUpdated(int songMetadataId, int newCount)
    {
        var song = FeaturedSongs.FirstOrDefault(item => item.Id == songMetadataId);
        if (song != null)
        {
            song.StreamCount = newCount;
        }
    }

    private void HandleLikeCountUpdated(int songMetadataId, int likeCount, int dislikeCount)
    {
        var song = FeaturedSongs.FirstOrDefault(item => item.Id == songMetadataId);
        if (song != null)
        {
            song.LikeCount = likeCount;
            song.DislikeCount = dislikeCount;
        }
    }

    [RelayCommand]
    private async Task PlaySongAsync(SongDto? song)
    {
        if (song == null)
        {
            return;
        }

        if (!_playbackService.PreviewLimitReached
            && PlaybackIndicatorStateResolver.ShouldToggleCurrentSong(song.Id, _playbackService.CurrentSong))
        {
            _playbackService.TogglePlayPause();
            return;
        }

        await PlayFeaturedQueueAsync(song);
    }

    public Task<bool> PlayFeaturedQueueFromStartAsync() =>
        PlayFeaturedQueueAsync();

    private Task<bool> PlayFeaturedQueueAsync(SongDto? startSong = null) =>
        PlaybackQueueBootstrapper.StartQueueAsync(
            FeaturedSongs,
            _mediaPlaybackOnboardingService,
            _playbackService,
            startSong);

    [RelayCommand]
    private Task OpenSongAsync(SongDto? song)
    {
        if (song == null) return Task.CompletedTask;

        return _navigationService.GoToAsync("song-player", new Dictionary<string, object>
        {
            ["Song"] = song
        });
    }

    [RelayCommand]
    private Task NavigateToGenreAsync(string? genre)
    {
        if (string.IsNullOrWhiteSpace(genre)) return Task.CompletedTask;

        return _navigationService.GoToAsync("playlist-player", new Dictionary<string, object>
        {
            ["GenreName"] = genre
        });
    }

    [RelayCommand]
    private Task NavigateToArtistAsync(string? artist)
    {
        if (string.IsNullOrWhiteSpace(artist)) return Task.CompletedTask;

        return _navigationService.GoToAsync("playlist-player", new Dictionary<string, object>
        {
            ["ArtistName"] = artist
        });
    }

    [RelayCommand]
    private async Task LikeSongAsync(SongDto? song)
    {
        if (song == null) return;

        if (!await RequireAuthenticatedUserAsync("like songs"))
            return;

        var result = await _musicService.ToggleLikeAsync(song.Id);
        if (result != null)
        {
            song.UserLikeStatus = result.IsLiked ? true : null;
            song.LikeCount = result.LikeCount;
            song.DislikeCount = result.DislikeCount;
        }
    }

    [RelayCommand]
    private async Task DislikeSongAsync(SongDto? song)
    {
        if (song == null) return;

        if (!await RequireAuthenticatedUserAsync("dislike songs"))
            return;

        var result = await _musicService.ToggleDislikeAsync(song.Id);
        if (result != null)
        {
            song.UserLikeStatus = result.IsDisliked ? false : null;
            song.LikeCount = result.LikeCount;
            song.DislikeCount = result.DislikeCount;
        }
    }

    [RelayCommand]
    private async Task ReportSongAsync(SongDto? song)
    {
        if (song == null) return;

        if (!await RequireValidatedUserAsync("report songs"))
            return;

        var reason = await _alertService.ShowActionSheetAsync(
            "Report Song", "Cancel", null,
            "Copyright Violation", "Terms of Use Violation");

        if (string.IsNullOrEmpty(reason) || reason == "Cancel")
            return;

        try
        {
            var success = await _musicService.ReportSongAsync(song.Id, reason);
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
            var goToLogin = await _alertService.ShowConfirmAsync(
                "Login Required",
                $"Please log in to {action}.",
                "Login",
                "Cancel");

            if (goToLogin)
            {
                await _navigationService.GoToAsync("login");
            }

            return false;
        }

        if (!_authService.EmailConfirmed)
        {
            await _alertService.DisplayAlertAsync(
                "Email Not Verified",
                "Please verify your email before you can interact with songs.",
                "OK");
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
            await _alertService.DisplayAlertAsync(
                "Not Authorized",
                "Your account must be fully verified to " + action + ".",
                "OK");
            return false;
        }

        return true;
    }

    [RelayCommand]
    private Task OpenRecommendedAsync()
    {
        var userId = _authService.UserId;
        if (userId == null) return Task.CompletedTask;
        return _navigationService.GoToAsync("playlist-player", new Dictionary<string, object>
        {
            ["RecommendedUserId"] = userId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
        });
    }

    [RelayCommand]
    private Task OpenPlaylistAsync(PlaylistDto? playlist)
    {
        if (playlist == null) return Task.CompletedTask;
        return _navigationService.GoToAsync("playlist-player", new Dictionary<string, object>
        {
            ["PlaylistId"] = playlist.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)
        });
    }

    [RelayCommand]
    private Task NavigateToMyPlaylistsAsync() => _navigationService.GoToAsync("my-playlists");

    [RelayCommand]
    private Task NavigateToLoginAsync() => _navigationService.GoToAsync("login");

    [RelayCommand]
    private Task NavigateToRegisterAsync() => _navigationService.GoToAsync("register");

    [RelayCommand]
    private Task NavigateToValidateEmailAsync()
    {
        return _navigationService.GoToAsync("verify-email", new Dictionary<string, object>
        {
            ["UserId"] = _authService.UserId ?? 0,
            ["Email"] = _authService.Email ?? string.Empty,
            ["Password"] = string.Empty
        });
    }

    [RelayCommand]
    private async Task SubscribeAsync()
    {
        try
        {
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
                RefreshAuthState();
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
        catch (Exception ex)
        {
            await _alertService.DisplayAlertAsync("Error", $"Subscription failed: {ex.Message}", "OK");
        }
    }

    [RelayCommand]
    private Task NavigateToMusicLibraryAsync() => _navigationService.GoToAsync("//MusicLibrary");

    [RelayCommand]
    private Task OpenSubscriptionManagementAsync()
        => _browserService.OpenExternalAsync(GetSubscriptionManagementUrl());

    [RelayCommand]
    private Task OpenArtistUploadAsync()
        => _browserService.OpenExternalAsync(_appConfig.ApiBaseUrl);

    private void RefreshAuthState()
    {
        IsAuthenticated = _authService.IsLoggedIn;
        HasActiveSubscription = _authService.HasActiveSubscription;
        IsEmailVerified = _authService.EmailConfirmed;
        IsCreator = _authService.IsCreator;
        OnPropertyChanged(nameof(ShowPlaylists));
        OnPropertyChanged(nameof(ManageSubscriptionText));
        OnPropertyChanged(nameof(SubscriptionAutoRenewalText));
    }

    private bool ShouldUseAppleSubscriptionManagement
        => string.Equals(_authService.BillingSource, BillingProviders.Apple, StringComparison.Ordinal) ||
           DeviceInfo.Platform == DevicePlatform.iOS;

    private string GetSubscriptionManagementUrl()
        => ShouldUseAppleSubscriptionManagement
            ? _configuration["AppleAppStore:SubscriptionManagementUrl"] ?? DefaultAppleSubscriptionManagementUrl
            : "https://play.google.com/store/account/subscriptions";

    private void OnAuthStateChanged()
    {
        RefreshAuthState();
        _ = LoadHomePlaylistsAsync();
        _ = LoadFeaturedSongsAsync();
    }
}
