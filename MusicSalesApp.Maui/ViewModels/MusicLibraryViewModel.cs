using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.ViewModels;

public partial class MusicLibraryViewModel : ObservableObject
{
    private const string AiFilterAll = "All";
    private const string AiFilterAiOnly = "AiOnly";
    private const string AiFilterNonAiOnly = "NonAiOnly";

    private readonly IMusicService _musicService;
    private readonly IAlertService _alertService;
    private readonly ISignalRService _signalRService;
    private readonly IAuthService _authService;
    private readonly INavigationService _navigationService;
    private readonly IPlaybackService _playbackService;
    private readonly IMediaPlaybackOnboardingService _mediaPlaybackOnboardingService;
    private readonly IAppConfig _appConfig;
    private readonly IBillingService _billingService;
    private readonly Dictionary<int, (int likes, int dislikes)> _likeCounts = new();
    private bool _subscriptionsAttached;

    // All songs (unfiltered source of truth)
    private readonly List<SongDto> _allSongs = [];

    public MusicLibraryViewModel(
        IMusicService musicService,
        IAlertService alertService,
        ISignalRService signalRService,
        IAuthService authService,
        INavigationService navigationService,
        IPlaybackService playbackService,
        IMediaPlaybackOnboardingService mediaPlaybackOnboardingService,
        IAppConfig appConfig,
        IBillingService billingService)
    {
        _musicService = musicService;
        _alertService = alertService;
        _signalRService = signalRService;
        _authService = authService;
        _navigationService = navigationService;
        _playbackService = playbackService;
        _mediaPlaybackOnboardingService = mediaPlaybackOnboardingService;
        _appConfig = appConfig;
        _billingService = billingService;

        AttachSubscriptions();
    }

    public void Activate()
    {
        AttachSubscriptions();
        SynchronizeVisibleQueue();
    }

    public void Cleanup()
    {
        if (!_subscriptionsAttached)
            return;

        _musicService.OnStreamCountRecorded -= HandleStreamCountUpdated;
        _signalRService.OnStreamCountUpdated -= HandleStreamCountUpdated;
        _signalRService.OnLikeCountUpdated -= HandleLikeCountUpdated;
        _playbackService.ShowSubscribeCtaRequested -= OnShowSubscribeCta;
        _subscriptionsAttached = false;
    }

    private void AttachSubscriptions()
    {
        if (_subscriptionsAttached)
            return;

        _musicService.OnStreamCountRecorded += HandleStreamCountUpdated;
        _signalRService.OnStreamCountUpdated += HandleStreamCountUpdated;
        _signalRService.OnLikeCountUpdated += HandleLikeCountUpdated;
        _playbackService.ShowSubscribeCtaRequested += OnShowSubscribeCta;
        _subscriptionsAttached = true;
    }

    /// <summary>Expose the shared playback service so the page can bind NowPlayingView.</summary>
    public IPlaybackService PlaybackService => _playbackService;

    /// <summary>Web base URL for share links.</summary>
    public string WebBaseUrl => _appConfig.WebBaseUrl;

    public ObservableCollection<SongDto> Songs { get; } = [];

    // --- Filter state ---

    public ObservableCollection<string> AvailableGenres { get; } = [];
    public ObservableCollection<string> AvailableArtists { get; } = [];

    public HashSet<string> SelectedGenres { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> SelectedArtists { get; } = new(StringComparer.OrdinalIgnoreCase);
    private string _selectedAiFilter = AiFilterAll;

    public ObservableCollection<FilterItem> GenreFilterItems { get; } = [];
    public ObservableCollection<FilterItem> ArtistFilterItems { get; } = [];

    [ObservableProperty]
    public partial bool IsAiPanelOpen { get; set; }

    [ObservableProperty]
    public partial bool IsGenrePanelOpen { get; set; }

    [ObservableProperty]
    public partial bool IsArtistPanelOpen { get; set; }

    [ObservableProperty]
    public partial string? GenreSearchText { get; set; }

    [ObservableProperty]
    public partial string? ArtistSearchText { get; set; }

    [ObservableProperty]
    public partial string GenrePillText { get; set; } = "Genre";

    [ObservableProperty]
    public partial string ArtistPillText { get; set; } = "Artist";

    [ObservableProperty]
    public partial string AiPillText { get; set; } = "Music Type";

    [ObservableProperty]
    public partial bool HasActiveAiFilter { get; set; }

    [ObservableProperty]
    public partial bool HasActiveGenreFilters { get; set; }

    [ObservableProperty]
    public partial bool HasActiveArtistFilters { get; set; }

    public bool HasAnyActiveFilters => HasActiveAiFilter || HasActiveGenreFilters || HasActiveArtistFilters;

    public bool IsAllAiFilterSelected => string.Equals(_selectedAiFilter, AiFilterAll, StringComparison.Ordinal);
    public bool IsAiOnlyFilterSelected => string.Equals(_selectedAiFilter, AiFilterAiOnly, StringComparison.Ordinal);
    public bool IsNonAiOnlyFilterSelected => string.Equals(_selectedAiFilter, AiFilterNonAiOnly, StringComparison.Ordinal);

    partial void OnGenreSearchTextChanged(string? value) => RefreshGenreFilterItems();
    partial void OnArtistSearchTextChanged(string? value) => RefreshArtistFilterItems();

    [RelayCommand]
    private void ToggleAiPanel()
    {
        IsAiPanelOpen = !IsAiPanelOpen;
        if (IsAiPanelOpen)
        {
            IsGenrePanelOpen = false;
            IsArtistPanelOpen = false;
        }
    }

    [RelayCommand]
    private void ToggleGenrePanel()
    {
        IsGenrePanelOpen = !IsGenrePanelOpen;
        if (IsGenrePanelOpen)
        {
            IsAiPanelOpen = false;
            IsArtistPanelOpen = false;
            GenreSearchText = null;
            RefreshGenreFilterItems();
        }
    }

    [RelayCommand]
    private void ToggleArtistPanel()
    {
        IsArtistPanelOpen = !IsArtistPanelOpen;
        if (IsArtistPanelOpen)
        {
            IsAiPanelOpen = false;
            IsGenrePanelOpen = false;
            ArtistSearchText = null;
            RefreshArtistFilterItems();
        }
    }

    [RelayCommand]
    internal void ToggleGenreFilter(string genre)
    {
        if (!SelectedGenres.Add(genre))
            SelectedGenres.Remove(genre);

        UpdateGenrePillText();
        RefreshAvailableArtists();
        RefreshArtistFilterItems();
        RefreshGenreFilterItemSelections();
        ApplyFilters();
    }

    [RelayCommand]
    internal void ToggleArtistFilter(string artist)
    {
        if (!SelectedArtists.Add(artist))
            SelectedArtists.Remove(artist);

        UpdateArtistPillText();
        RefreshAvailableGenres();
        RefreshGenreFilterItems();
        RefreshArtistFilterItemSelections();
        ApplyFilters();
    }

    [RelayCommand]
    private void SelectAiFilter(string? filter)
    {
        _selectedAiFilter = filter switch
        {
            AiFilterAiOnly => AiFilterAiOnly,
            AiFilterNonAiOnly => AiFilterNonAiOnly,
            _ => AiFilterAll
        };

        IsAiPanelOpen = false;

        OnPropertyChanged(nameof(IsAllAiFilterSelected));
        OnPropertyChanged(nameof(IsAiOnlyFilterSelected));
        OnPropertyChanged(nameof(IsNonAiOnlyFilterSelected));
        UpdateAiPillText();

        RefreshAvailableGenres();
        RefreshAvailableArtists();
        RefreshGenreFilterItems();
        RefreshArtistFilterItems();
        ApplyFilters();
    }

    private void UpdateGenrePillText()
    {
        HasActiveGenreFilters = SelectedGenres.Count > 0;
        GenrePillText = SelectedGenres.Count > 0
            ? $"Genre ({SelectedGenres.Count})"
            : "Genre";
        OnPropertyChanged(nameof(HasAnyActiveFilters));
    }

    private void UpdateArtistPillText()
    {
        HasActiveArtistFilters = SelectedArtists.Count > 0;
        ArtistPillText = SelectedArtists.Count > 0
            ? $"Artist ({SelectedArtists.Count})"
            : "Artist";
        OnPropertyChanged(nameof(HasAnyActiveFilters));
    }

    private void UpdateAiPillText()
    {
        HasActiveAiFilter = _selectedAiFilter != AiFilterAll;
        AiPillText = _selectedAiFilter switch
        {
            AiFilterAiOnly => "AI Music",
            AiFilterNonAiOnly => "Non-AI Music",
            _ => "Music Type"
        };
        OnPropertyChanged(nameof(HasAnyActiveFilters));
    }

    private void RefreshGenreFilterItems()
    {
        var search = GenreSearchText?.Trim();
        var songs = CrossFilterSongsByArtist();

        var items = songs
            .Where(s => !string.IsNullOrWhiteSpace(s.Genre))
            .GroupBy(s => s.Genre, StringComparer.OrdinalIgnoreCase)
            .Select(g => new FilterItem
            {
                Name = g.Key,
                Count = g.Count(),
                IsSelected = SelectedGenres.Contains(g.Key)
            })
            .Where(f => string.IsNullOrEmpty(search) ||
                        f.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        GenreFilterItems.Clear();
        foreach (var item in items)
            GenreFilterItems.Add(item);
    }

    private void RefreshArtistFilterItems()
    {
        var search = ArtistSearchText?.Trim();
        var songs = CrossFilterSongsByGenre();

        var items = songs
            .Where(s => !string.IsNullOrWhiteSpace(s.ArtistName))
            .GroupBy(s => s.ArtistName, StringComparer.OrdinalIgnoreCase)
            .Select(g => new FilterItem
            {
                Name = g.Key,
                Count = g.Count(),
                IsSelected = SelectedArtists.Contains(g.Key)
            })
            .Where(f => string.IsNullOrEmpty(search) ||
                        f.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        ArtistFilterItems.Clear();
        foreach (var item in items)
            ArtistFilterItems.Add(item);
    }

    private void RefreshGenreFilterItemSelections()
    {
        foreach (var item in GenreFilterItems)
            item.IsSelected = SelectedGenres.Contains(item.Name);
    }

    private void RefreshArtistFilterItemSelections()
    {
        foreach (var item in ArtistFilterItems)
            item.IsSelected = SelectedArtists.Contains(item.Name);
    }

    private IEnumerable<SongDto> CrossFilterSongsByArtist()
    {
        IEnumerable<SongDto> songs = FilterSongsByAiSelection(_allSongs);
        if (SelectedArtists.Count > 0)
            songs = songs.Where(s => SelectedArtists.Contains(s.ArtistName));
        return songs;
    }

    private IEnumerable<SongDto> CrossFilterSongsByGenre()
    {
        IEnumerable<SongDto> songs = FilterSongsByAiSelection(_allSongs);
        if (SelectedGenres.Count > 0)
            songs = songs.Where(s => SelectedGenres.Contains(s.Genre));
        return songs;
    }

    [RelayCommand]
    private void ClearFilters()
    {
        SelectedGenres.Clear();
        SelectedArtists.Clear();
        _selectedAiFilter = AiFilterAll;
        OnPropertyChanged(nameof(IsAllAiFilterSelected));
        OnPropertyChanged(nameof(IsAiOnlyFilterSelected));
        OnPropertyChanged(nameof(IsNonAiOnlyFilterSelected));
        UpdateGenrePillText();
        UpdateArtistPillText();
        UpdateAiPillText();
        GenreSearchText = null;
        ArtistSearchText = null;
        IsAiPanelOpen = false;
        IsGenrePanelOpen = false;
        IsArtistPanelOpen = false;
        RefreshAvailableGenres();
        RefreshAvailableArtists();
        RefreshGenreFilterItems();
        RefreshArtistFilterItems();
        ApplyFilters();
    }

    /// <summary>
    /// Rebuilds the Songs collection from _allSongs using current filter selections.
    /// </summary>
    internal void ApplyFilters()
    {
        IEnumerable<SongDto> filtered = FilterSongsByAiSelection(_allSongs);

        if (SelectedGenres.Count > 0)
        {
            filtered = filtered.Where(s =>
                SelectedGenres.Contains(s.Genre));
        }

        if (SelectedArtists.Count > 0)
        {
            filtered = filtered.Where(s =>
                SelectedArtists.Contains(s.ArtistName));
        }

        Songs.Clear();
        foreach (var song in filtered)
        {
            Songs.Add(song);
        }
    }

    private void SynchronizeVisibleQueue()
    {
        if (!_playbackService.HasPlaylist || !_playbackService.IsPlaying || Songs.Count == 0)
        {
            return;
        }

        var visibleSongs = Songs.ToList();
        if (PlaybackQueueSelection.HasEquivalentActiveQueue(_playbackService, visibleSongs))
        {
            return;
        }

        var startIndex = PlaybackQueueSelection.ResolveCurrentSongIndex(_playbackService, visibleSongs);
        _playbackService.SetPlaylist(visibleSongs, startIndex);
    }

    private IEnumerable<SongDto> FilterSongsByAiSelection(IEnumerable<SongDto> songs)
    {
        return _selectedAiFilter switch
        {
            AiFilterAiOnly => songs.Where(s => s.IsAiGenerated),
            AiFilterNonAiOnly => songs.Where(s => !s.IsAiGenerated),
            _ => songs
        };
    }

    /// <summary>
    /// Refreshes AvailableGenres, cross-filtered by the currently selected artists.
    /// </summary>
    internal void RefreshAvailableGenres()
    {
        var songs = CrossFilterSongsByArtist();

        var genres = songs
            .Where(s => !string.IsNullOrWhiteSpace(s.Genre))
            .Select(s => s.Genre)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g, StringComparer.OrdinalIgnoreCase)
            .ToList();

        AvailableGenres.Clear();
        foreach (var g in genres)
        {
            AvailableGenres.Add(g);
        }
    }

    /// <summary>
    /// Refreshes AvailableArtists, cross-filtered by the currently selected genres.
    /// </summary>
    internal void RefreshAvailableArtists()
    {
        var songs = CrossFilterSongsByGenre();

        var artists = songs
            .Where(s => !string.IsNullOrWhiteSpace(s.ArtistName))
            .Select(s => s.ArtistName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(a => a, StringComparer.OrdinalIgnoreCase)
            .ToList();

        AvailableArtists.Clear();
        foreach (var a in artists)
        {
            AvailableArtists.Add(a);
        }
    }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    /// <summary>
    /// Fetches the stream qualifying seconds from the server. Call once at startup.
    /// </summary>
    public async Task LoadStreamQualifyingSecondsAsync()
    {
        var seconds = await _musicService.GetStreamQualifyingSecondsAsync();
        _playbackService.SetStreamQualifyingSeconds(seconds);
    }

    // --- Like/dislike ---

    public int GetLikeCount(int songId)
    {
        return _likeCounts.TryGetValue(songId, out var counts) ? counts.likes : 0;
    }

    public int GetDislikeCount(int songId)
    {
        return _likeCounts.TryGetValue(songId, out var counts) ? counts.dislikes : 0;
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

    /// <summary>
    /// Returns true if the user is logged in with a confirmed email (User role).
    /// Shows appropriate alerts and navigation if not.
    /// </summary>
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

    /// <summary>
    /// Returns true if the user has the "User" role (confirmed email, not NonValidatedUser).
    /// </summary>
    internal async Task<bool> RequireValidatedUserAsync(string action)
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

    // --- Navigation ---

    [RelayCommand]
    private async Task OpenSongAsync(SongDto? song)
    {
        if (song == null) return;
        await _navigationService.GoToAsync("song-player", new Dictionary<string, object>
        {
            ["Song"] = song
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

    // --- Songs loading ---

    [RelayCommand]
    private async Task LoadSongsAsync()
    {
        if (IsLoading) return;

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var songs = await _musicService.GetSongsAsync();
            if (songs.Count == 0 && !string.IsNullOrWhiteSpace(_musicService.LastSongsError))
            {
                ErrorMessage = _musicService.LastSongsError;
            }

            System.Diagnostics.Debug.WriteLine($"[MusicLibrary] WebBaseUrl = '{_appConfig.WebBaseUrl}'");
            Console.WriteLine($"[MusicLibrary] WebBaseUrl = '{_appConfig.WebBaseUrl}'");
            foreach (var song in songs)
            {
                song.ShareUrl = SongDto.BuildShareUrl(song.Id, _appConfig.WebBaseUrl);
                System.Diagnostics.Debug.WriteLine($"[MusicLibrary] Song '{song.SongTitle}' → ShareUrl = '{song.ShareUrl}'");
                Console.WriteLine($"[MusicLibrary] Song '{song.SongTitle}' → ShareUrl = '{song.ShareUrl}'");
            }

            var orderedSongs = SongDisplayOrderSorter.OrderForLibrary(songs);

            _allSongs.Clear();
            _allSongs.AddRange(orderedSongs);

            // Reset filters when reloading
            SelectedGenres.Clear();
            SelectedArtists.Clear();
            _selectedAiFilter = AiFilterAll;
            OnPropertyChanged(nameof(IsAllAiFilterSelected));
            OnPropertyChanged(nameof(IsAiOnlyFilterSelected));
            OnPropertyChanged(nameof(IsNonAiOnlyFilterSelected));
            UpdateGenrePillText();
            UpdateArtistPillText();
            IsGenrePanelOpen = false;
            IsArtistPanelOpen = false;
            RefreshAvailableGenres();
            RefreshAvailableArtists();
            ApplyFilters();
            SynchronizeVisibleQueue();

            // Load like counts and user like status in parallel
            await Task.WhenAll(
                LoadLikeCountsAsync(orderedSongs),
                LoadUserLikeStatusAsync(orderedSongs));
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

    private async Task LoadLikeCountsAsync(List<SongDto> songs)
    {
        try
        {
            var ids = songs.Select(s => s.Id).ToList();
            if (ids.Count == 0) return;

            var likeCounts = await _musicService.GetBulkLikeCountsAsync(ids);
            _likeCounts.Clear();
            foreach (var lc in likeCounts)
            {
                _likeCounts[lc.SongMetadataId] = (lc.LikeCount, lc.DislikeCount);

                var song = songs.FirstOrDefault(s => s.Id == lc.SongMetadataId);
                if (song != null)
                {
                    song.LikeCount = lc.LikeCount;
                    song.DislikeCount = lc.DislikeCount;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load like counts: {ex.Message}");
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
                {
                    song.UserLikeStatus = status;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load user like status: {ex.Message}");
        }
    }

    // --- SignalR real-time updates ---

    public async Task StartSignalRAsync()
    {
        await _signalRService.StartAsync();
    }

    private void HandleStreamCountUpdated(int songMetadataId, int newCount)
    {
        var song = Songs.FirstOrDefault(s => s.Id == songMetadataId);
        if (song != null)
        {
            song.StreamCount = newCount;
        }
    }

    private void HandleLikeCountUpdated(int songMetadataId, int likeCount, int dislikeCount)
    {
        _likeCounts[songMetadataId] = (likeCount, dislikeCount);

        var song = Songs.FirstOrDefault(s => s.Id == songMetadataId);
        if (song != null)
        {
            song.LikeCount = likeCount;
            song.DislikeCount = dislikeCount;
        }
    }

    // --- Playback delegation ---

    [RelayCommand]
    private async Task PlaySongAsync(SongDto? song)
    {
        if (song == null)
            return;

        await PlayVisibleQueueAsync(song);
    }

    public Task<bool> PlayVisibleQueueFromStartAsync() =>
        PlayVisibleQueueAsync();

    private Task<bool> PlayVisibleQueueAsync(SongDto? startSong = null) =>
        PlaybackQueueBootstrapper.StartQueueAsync(
            Songs,
            _mediaPlaybackOnboardingService,
            _playbackService,
            startSong);

    [RelayCommand]
    private void TogglePlayPause() => _playbackService.TogglePlayPause();

    [RelayCommand]
    private void Stop() => _playbackService.Stop();

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

            var verificationResult = await _musicService.VerifyGooglePlayPurchaseAsync(result.PurchaseToken!, result.OrderId);

            if (verificationResult.Success)
            {
                await _authService.RefreshUserStatusAsync();
                _playbackService.HandleSubscriptionActivated();
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
}
