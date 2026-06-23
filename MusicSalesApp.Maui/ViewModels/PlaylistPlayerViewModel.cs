using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.ViewModels;

[QueryProperty(nameof(GenreName), "GenreName")]
[QueryProperty(nameof(ArtistName), "ArtistName")]
[QueryProperty(nameof(PlaylistIdParam), "PlaylistId")]
[QueryProperty(nameof(RecommendedUserIdParam), "RecommendedUserId")]
public partial class PlaylistPlayerViewModel : ObservableObject
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
    private readonly IPlaylistService _playlistService;

    /// <summary>Id of the currently loaded custom playlist, if any.</summary>
    private int? _loadedPlaylistId;
    /// <summary>Maps a song's SongMetadataId to its UserPlaylist row id for reorder/remove.</summary>
    private readonly Dictionary<int, int> _userPlaylistIdBySongId = [];
    private bool _subscriptionsAttached;

    public PlaylistPlayerViewModel(
        IMusicService musicService,
        IAlertService alertService,
        IAuthService authService,
        INavigationService navigationService,
        IPlaybackService playbackService,
        IMediaPlaybackOnboardingService mediaPlaybackOnboardingService,
        ISignalRService signalRService,
        IAppConfig appConfig,
        IBillingService billingService,
        IPlaylistService playlistService)
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
        _playlistService = playlistService;

        AttachSubscriptions();

        Songs.CollectionChanged += (_, _) =>
        {
            HasSongs = Songs.Count > 0;
        };
    }

    public IPlaybackService PlaybackService => _playbackService;

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
        _playbackService.StateChanged += OnPlaybackStateChanged;
        _playbackService.ShowSubscribeCtaRequested += OnShowSubscribeCta;
        _subscriptionsAttached = true;
    }

    public ObservableCollection<SongDto> Songs { get; } = [];

    [ObservableProperty]
    public partial string PlaylistTitle { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowTracksHeader))]
    [NotifyPropertyChangedFor(nameof(IsCurrentTrackPreviewLimited))]
    public partial SongDto? CurrentSong { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyPlaylistPrompt))]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCurrentTrackPreviewLimited))]
    public partial bool HasActiveSubscription { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial string? GenreName { get; set; }

    [ObservableProperty]
    public partial string? ArtistName { get; set; }

    /// <summary>Raw query param for PlaylistId (Shell passes int or string).</summary>
    [ObservableProperty]
    public partial string? PlaylistIdParam { get; set; }

    /// <summary>Raw query param for RecommendedUserId.</summary>
    [ObservableProperty]
    public partial string? RecommendedUserIdParam { get; set; }

    /// <summary>True when the loaded playlist is a user-owned custom playlist (reorder/remove allowed).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReorderEnabled))]
    [NotifyPropertyChangedFor(nameof(ShowTracksHeader))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyPlaylistPrompt))]
    public partial bool IsUserPlaylist { get; set; }

    /// <summary>True when the user can drag-reorder tracks (custom playlist + active subscription).</summary>
    public bool IsReorderEnabled => IsUserPlaylist && HasActiveSubscription;

    /// <summary>Tracks Songs.Count changes so computed properties refresh.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyPlaylistPrompt))]
    [NotifyPropertyChangedFor(nameof(ShowTracksHeader))]
    public partial bool HasSongs { get; set; }

    /// <summary>Show the "Tracks" header + Add Songs button whenever a playlist is loaded for the user, or there's a current song.</summary>
    public bool ShowTracksHeader => IsUserPlaylist || HasSongs || CurrentSong is not null;

    /// <summary>Show the empty-custom-playlist call-to-action when we've finished loading a user playlist that has no songs.</summary>
    public bool ShowEmptyPlaylistPrompt => IsUserPlaylist && !HasSongs && !IsLoading;

    public bool IsCurrentTrackPreviewLimited =>
        CurrentSong != null &&
        !HasActiveSubscription &&
        !CurrentSong.DisplayOnHomePage &&
        !(_authService.IsCreator && CurrentSong.CreatorUserId == _authService.UserId);

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

    partial void OnPlaylistIdParamChanged(string? value)
    {
        if (!string.IsNullOrEmpty(value) && int.TryParse(Uri.UnescapeDataString(value), out _))
            _ = LoadPlaylistAsync();
    }

    partial void OnRecommendedUserIdParamChanged(string? value)
    {
        if (!string.IsNullOrEmpty(value))
            _ = LoadPlaylistAsync();
    }

    partial void OnHasActiveSubscriptionChanged(bool value)
    {
        OnPropertyChanged(nameof(IsReorderEnabled));
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

        // Branch 1: Custom playlist or system playlist (Liked Songs) by id
        if (TryParseQueryInt(PlaylistIdParam, out var playlistId))
        {
            await LoadFromPlaylistServiceAsync(playlistId, loadRecommended: false);
            return;
        }

        // Branch 2: Recommended playlist (server scopes to current user from JWT)
        if (!string.IsNullOrEmpty(RecommendedUserIdParam))
        {
            await LoadFromPlaylistServiceAsync(null, loadRecommended: true);
            return;
        }

        // Legacy: filter by Genre / Artist using full song list
        IsUserPlaylist = false;
        _loadedPlaylistId = null;
        _userPlaylistIdBySongId.Clear();

        var allSongs = await _musicService.GetSongsAsync();
        if (allSongs.Count == 0 && !string.IsNullOrWhiteSpace(_musicService.LastSongsError))
        {
            ErrorMessage = _musicService.LastSongsError;
            return;
        }

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

        filtered = SongDisplayOrderSorter.OrderById(filtered);

        if (filtered.Count == 0)
        {
            ErrorMessage = !string.IsNullOrEmpty(GenreName)
                ? $"No songs found for genre \"{Uri.UnescapeDataString(GenreName)}\"."
                : $"No songs found for artist \"{Uri.UnescapeDataString(ArtistName!)}\".";
            return;
        }

        await FinalizeLoadedSongsAsync(filtered);
    }

    private async Task LoadFromPlaylistServiceAsync(int? playlistId, bool loadRecommended)
    {
        _userPlaylistIdBySongId.Clear();
        _loadedPlaylistId = null;

        PlaylistSongsDto? result;
        if (playlistId.HasValue)
        {
            result = await _playlistService.GetPlaylistSongsAsync(playlistId.Value);
            if (result == null)
            {
                ErrorMessage = "Failed to load playlist.";
                IsUserPlaylist = false;
                return;
            }
            _loadedPlaylistId = result.PlaylistId;
            // Only non-system ("custom") playlists support reorder.
            IsUserPlaylist = !result.IsSystemGenerated;
        }
        else if (loadRecommended)
        {
            result = await _playlistService.GetRecommendedSongsAsync();
            if (result == null)
            {
                ErrorMessage = "Failed to load recommended playlist.";
                IsUserPlaylist = false;
                return;
            }
            IsUserPlaylist = false;
        }
        else
        {
            return;
        }

        PlaylistTitle = result.PlaylistName;

        if (result.Songs.Count == 0)
        {
            ErrorMessage = "This playlist has no songs yet.";
            Songs.Clear();
            return;
        }

        var mapped = result.Songs.Select(MapToSongDto).ToList();
        foreach (var ps in result.Songs.Where(ps => ps.UserPlaylistId.HasValue))
            _userPlaylistIdBySongId[ps.SongMetadataId] = ps.UserPlaylistId!.Value;

        await FinalizeLoadedSongsAsync(mapped);
    }

    private async Task FinalizeLoadedSongsAsync(List<SongDto> list)
    {
        foreach (var song in list)
            song.ShareUrl = SongDto.BuildShareUrl(song.Id, _appConfig.WebBaseUrl);

        await Task.WhenAll(
            LoadLikeCountsAsync(list),
            LoadUserLikeStatusAsync(list));

        Songs.Clear();
        foreach (var song in list)
            Songs.Add(song);

        HasActiveSubscription = _authService.HasActiveSubscription;

        var currentSongOutsideVisibleQueue = PlaybackQueueSelection.HasCurrentSongOutsideQueue(_playbackService, list);
        if (PlaybackQueueSelection.HasEquivalentActiveQueue(_playbackService, list) && !currentSongOutsideVisibleQueue)
        {
            CurrentSong = PlaybackQueueSelection.ResolveCurrentSong(_playbackService, list);
            OnPropertyChanged(nameof(ShareUrl));
            return;
        }

        await _mediaPlaybackOnboardingService.EnsureBackgroundPlaybackExplainedAsync();
        _playbackService.SetPlaylist(
            list,
            0,
            currentSongOutsideVisibleQueue
                ? PlaybackQueueStartBehavior.RestartAtRequestedIndex
                : PlaybackQueueStartBehavior.PreserveCurrentSongIfPresent,
            BuildVisibleQueueDescription());
        CurrentSong = PlaybackQueueSelection.ResolveCurrentSong(_playbackService, list);
        OnPropertyChanged(nameof(ShareUrl));
    }

    private string BuildVisibleQueueDescription()
    {
        if (!string.IsNullOrWhiteSpace(GenreName))
        {
            return PlaybackQueueDescriptions.Genre(Uri.UnescapeDataString(GenreName));
        }

        if (!string.IsNullOrWhiteSpace(ArtistName))
        {
            return PlaybackQueueDescriptions.Artist(Uri.UnescapeDataString(ArtistName));
        }

        return string.IsNullOrWhiteSpace(PlaylistTitle)
            ? "Playlist player"
            : PlaybackQueueDescriptions.Playlist(PlaylistTitle);
    }

    private static SongDto MapToSongDto(PlaylistSongDto ps) => new()
    {
        Id = ps.SongMetadataId != 0 ? ps.SongMetadataId : ps.Id,
        SongTitle = ps.SongTitle,
        ArtistName = ps.ArtistName,
        Genre = ps.Genre,
        AlbumArtUrl = ps.AlbumArtUrl,
        PersonaImageUrl = ps.PersonaImageUrl,
        PersonaBio = ps.PersonaBio,
        StreamUrl = ps.StreamUrl,
        StreamQualifyingSeconds = ps.StreamQualifyingSeconds,
        TrackLengthSeconds = ps.TrackLengthSeconds,
        StreamCount = ps.StreamCount,
        IsAiGenerated = ps.IsAiGenerated,
        IsAiVocals = ps.IsAiVocals,
        IsAiLyrics = ps.IsAiLyrics,
        DisplayOnHomePage = ps.DisplayOnHomePage,
        DisplayOrder = ps.DisplayOrder,
        CreatorId = ps.CreatorId,
        CreatorUserId = ps.CreatorUserId,
    };

    private static bool TryParseQueryInt(string? raw, out int value)
    {
        value = 0;
        if (string.IsNullOrEmpty(raw)) return false;
        return int.TryParse(Uri.UnescapeDataString(raw), out value);
    }

    /// <summary>
    /// Persist a new track order to the server. Returns true on success.
    /// </summary>
    public async Task<bool> PersistReorderAsync()
    {
        if (!IsReorderEnabled || _loadedPlaylistId is null) return false;

        var ids = new List<int>(Songs.Count);
        foreach (var s in Songs)
        {
            if (_userPlaylistIdBySongId.TryGetValue(s.Id, out var upId))
                ids.Add(upId);
        }
        if (ids.Count != Songs.Count)
            return false;

        var result = await _playlistService.ReorderAsync(_loadedPlaylistId.Value, ids);
        if (!result.Success)
        {
            ErrorMessage = result.ErrorMessage ?? "Failed to save new order.";
            await LoadPlaylistAsync();
            return false;
        }
        return true;
    }

    [RelayCommand]
    private async Task MoveTrackUpAsync(SongDto? song)
    {
        if (song == null || !IsReorderEnabled) return;
        var idx = Songs.IndexOf(song);
        if (idx <= 0) return;
        Songs.Move(idx, idx - 1);
        await PersistReorderAsync();
    }

    [RelayCommand]
    private async Task MoveTrackDownAsync(SongDto? song)
    {
        if (song == null || !IsReorderEnabled) return;
        var idx = Songs.IndexOf(song);
        if (idx < 0 || idx >= Songs.Count - 1) return;
        Songs.Move(idx, idx + 1);
        await PersistReorderAsync();
    }

    // --- Remove song (custom playlists only) ---

    [RelayCommand]
    private async Task RemoveSongFromPlaylistAsync(SongDto? song)
    {
        if (song == null || !IsUserPlaylist || _loadedPlaylistId is null) return;
        if (!_userPlaylistIdBySongId.TryGetValue(song.Id, out var userPlaylistId)) return;

        var confirmed = await _alertService.ShowConfirmAsync(
            "Remove song",
            $"Remove \"{song.SongTitle}\" from this playlist?",
            "Remove",
            "Cancel");
        if (!confirmed) return;

        var result = await _playlistService.RemoveSongAsync(_loadedPlaylistId.Value, userPlaylistId);
        if (!result.Success)
        {
            ErrorMessage = result.ErrorMessage ?? "Failed to remove song from playlist.";
            return;
        }

        await LoadPlaylistAsync();
    }

    // --- Playback commands ---

    [RelayCommand]
    private async Task PlayTrackAsync(SongDto? song)
    {
        if (song == null) return;

        if (PlaybackIndicatorStateResolver.ShouldToggleCurrentSong(song.Id, _playbackService.CurrentSong))
        {
            _playbackService.TogglePlayPause();
            return;
        }

        await PlayVisibleQueueAsync(song);
    }

    public Task<bool> PlayVisibleQueueFromStartAsync() =>
        PlayVisibleQueueAsync();

    private Task<bool> PlayVisibleQueueAsync(SongDto? startSong = null) =>
        PlaybackQueueBootstrapper.StartQueueAsync(
            Songs,
            _mediaPlaybackOnboardingService,
            _playbackService,
            startSong,
            BuildVisibleQueueDescription());

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
        await _navigationService.GoToReplacingCurrentAsync("playlist-player", new Dictionary<string, object>
        {
            ["GenreName"] = genre
        });
    }

    [RelayCommand]
    private async Task NavigateToArtistAsync(string? artist)
    {
        if (string.IsNullOrEmpty(artist)) return;
        await _navigationService.GoToReplacingCurrentAsync("playlist-player", new Dictionary<string, object>
        {
            ["ArtistName"] = artist
        });
    }

    [RelayCommand]
    private async Task GoToMusicLibraryAsync()
    {
        await _navigationService.GoToAsync(NavigationRoutes.MusicLibraryRoot);
    }

    // --- Auth helpers ---

    internal async Task<bool> RequireAuthenticatedUserAsync(string action)
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
            CurrentSong = Songs.Count == 0
                ? _playbackService.CurrentSong
                : PlaybackQueueSelection.ResolveCurrentSong(_playbackService, Songs);
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
        _playbackService.StateChanged -= OnPlaybackStateChanged;
        _playbackService.ShowSubscribeCtaRequested -= OnShowSubscribeCta;
        _subscriptionsAttached = false;
    }
}
