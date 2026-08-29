using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.ViewModels;

public partial class MusicLibraryViewModel : ObservableObject
{
    private const string AiFilterAll = "All";
    private const string AiFilterAny = "AnyAi";
    private const string AiFilterAiMusic = "AiMusic";
    private const string AiFilterAiVocals = "AiVocals";
    private const string AiFilterAiLyrics = "AiLyrics";
    private const string AiFilterNonAiOnly = "NonAiOnly";

    private readonly IMusicService _musicService;
    private readonly IAlertService _alertService;
    private readonly IUserStreamedSongStore? _userStreamedSongStore;
    private readonly ISignalRService _signalRService;
    private readonly IAuthService _authService;
    private readonly INavigationService _navigationService;
    private readonly IPlaybackService _playbackService;
    private readonly IMediaPlaybackOnboardingService _mediaPlaybackOnboardingService;
    private readonly IAppConfig _appConfig;
    private readonly IBillingService _billingService;
    private readonly IAudioCacheService? _audioCacheService;
    private readonly INetworkStatusService? _networkStatusService;
    private readonly ISongArtworkHydrator? _songArtworkHydrator;
    private readonly Dictionary<int, (int likes, int dislikes)> _likeCounts = new();
    private readonly HashSet<int> _downloadedSongIds = [];
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
        IBillingService billingService,
        IAudioCacheService? audioCacheService = null,
        INetworkStatusService? networkStatusService = null,
        ISongArtworkHydrator? songArtworkHydrator = null,
        IUserStreamedSongStore? userStreamedSongStore = null)
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
        _audioCacheService = audioCacheService;
        _networkStatusService = networkStatusService;
        _songArtworkHydrator = songArtworkHydrator;
        _userStreamedSongStore = userStreamedSongStore;

        UpdateAiPillText();
        UpdateGenrePillText();
        UpdateArtistPillText();

        AttachSubscriptions();
    }

    public void Activate()
    {
        AttachSubscriptions();
        SynchronizeVisibleQueue();
        SyncCurrentSongFromPlayback();
    }

    public void Cleanup()
    {
        if (!_subscriptionsAttached)
            return;

        _musicService.OnStreamCountRecorded -= HandleStreamCountUpdated;
        _signalRService.OnStreamCountUpdated -= HandleStreamCountUpdated;
        _signalRService.OnLikeCountUpdated -= HandleLikeCountUpdated;
        _playbackService.ShowSubscribeCtaRequested -= OnShowSubscribeCta;
        _playbackService.StateChanged -= OnPlaybackStateChanged;
        if (_networkStatusService != null)
            _networkStatusService.PropertyChanged -= HandleNetworkStatusChanged;
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
        _playbackService.StateChanged += OnPlaybackStateChanged;
        if (_networkStatusService != null)
            _networkStatusService.PropertyChanged += HandleNetworkStatusChanged;
        _subscriptionsAttached = true;
    }

    /// <summary>
    /// Reloads when connectivity flips in either direction: going offline narrows the library to
    /// downloaded songs, coming back online restores the full catalog without a manual pull-to-refresh.
    /// NetworkStatusService already marshals this to the main thread.
    /// </summary>
    private void HandleNetworkStatusChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!NetworkStatusChange.AffectsConnectivity(e.PropertyName))
            return;

        OnPropertyChanged(nameof(CanUseServerActions));
        OnPropertyChanged(nameof(EmptyStateTitle));
        OnPropertyChanged(nameof(EmptyStateDetail));

        if (IsLoading)
            return;

        _ = LoadSongsCommand.ExecuteAsync(null);
    }

    /// <summary>
    /// The song the player is on, resolved to this page's own copy.
    /// </summary>
    /// <remarks>
    /// Mirrors <c>PlaylistPlayerViewModel.CurrentSong</c> so both list pages give their code-behind
    /// the same change signal to scroll on. Resolved against the visible list rather than taken off
    /// the service, so it is the SongDto instance this page's cards are bound to.
    /// </remarks>
    [ObservableProperty]
    public partial SongDto? CurrentSong { get; set; }

    private void OnPlaybackStateChanged(string propertyName)
    {
        if (propertyName == nameof(IPlaybackService.CurrentSong))
        {
            SyncCurrentSongFromPlayback();
        }
    }

    private void SyncCurrentSongFromPlayback()
    {
        CurrentSong = Songs.Count == 0
            ? _playbackService.CurrentSong
            : PlaybackQueueSelection.ResolveCurrentSong(_playbackService, Songs);
        MarkNowPlayingRow();
    }

    /// <summary>
    /// Flag the card the player is on, so the library shows WHICH song is playing.
    /// </summary>
    /// <remarks>
    /// Walks the list rather than tracking the previous card, for the reason written up on the
    /// playlist's copy of this: ApplyFilters replaces Songs wholesale, so a remembered reference
    /// would clear the flag on a card no longer displayed and leave the real one lit.
    /// </remarks>
    private void MarkNowPlayingRow()
    {
        var playingId = CurrentSong?.Id;
        foreach (var song in Songs)
        {
            song.IsNowPlaying = song.Id == playingId;
        }
    }

    /// <summary>Expose the shared playback service so the page can bind NowPlayingView.</summary>
    public IPlaybackService PlaybackService => _playbackService;

    /// <summary>Web base URL for share links.</summary>
    public string WebBaseUrl => _appConfig.WebBaseUrl;

    public ObservableRangeCollection<SongDto> Songs { get; } = [];

    // --- Filter state ---

    public ObservableRangeCollection<string> AvailableGenres { get; } = [];
    public ObservableRangeCollection<string> AvailableArtists { get; } = [];

    public HashSet<string> SelectedGenres { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> SelectedArtists { get; } = new(StringComparer.OrdinalIgnoreCase);
    private string _selectedAiFilter = AiFilterAll;

    public ObservableRangeCollection<FilterItem> GenreFilterItems { get; } = [];
    public ObservableRangeCollection<FilterItem> ArtistFilterItems { get; } = [];

    [ObservableProperty]
    public partial bool IsDownloadedFilterActive { get; set; }

    [ObservableProperty]
    public partial bool IsAiPanelOpen { get; set; }

    [ObservableProperty]
    public partial bool IsGenrePanelOpen { get; set; }

    [ObservableProperty]
    public partial bool IsArtistPanelOpen { get; set; }

    [ObservableProperty]
    public partial bool IsTitlePanelOpen { get; set; }

    [ObservableProperty]
    public partial string? GenreSearchText { get; set; }

    [ObservableProperty]
    public partial string? ArtistSearchText { get; set; }

    /// <summary>
    /// The Title filter's query. Unlike GenreSearchText/ArtistSearchText, which only narrow an
    /// option list, this one is itself a filter - every reset path has to clear it.
    /// </summary>
    [ObservableProperty]
    public partial string? TitleSearchText { get; set; }

    [ObservableProperty]
    public partial string GenrePillText { get; set; } = "Genre";

    [ObservableProperty]
    public partial string ArtistPillText { get; set; } = "Artist";

    [ObservableProperty]
    public partial string TitlePillText { get; set; } = "Title";

    [ObservableProperty]
    public partial string AiPillText { get; set; } = "Music Type";

    [ObservableProperty]
    public partial bool HasActiveAiFilter { get; set; }

    [ObservableProperty]
    public partial bool HasActiveGenreFilters { get; set; }

    [ObservableProperty]
    public partial bool HasActiveArtistFilters { get; set; }

    [ObservableProperty]
    public partial bool HasActiveTitleFilter { get; set; }

    [ObservableProperty]
    public partial bool HasAnyActiveFilters { get; set; }

    public bool IsAllAiFilterSelected => string.Equals(_selectedAiFilter, AiFilterAll, StringComparison.Ordinal);
    public bool IsAnyAiFilterSelected => string.Equals(_selectedAiFilter, AiFilterAny, StringComparison.Ordinal);
    public bool IsAiMusicFilterSelected => string.Equals(_selectedAiFilter, AiFilterAiMusic, StringComparison.Ordinal);
    public bool IsAiVocalsFilterSelected => string.Equals(_selectedAiFilter, AiFilterAiVocals, StringComparison.Ordinal);
    public bool IsAiLyricsFilterSelected => string.Equals(_selectedAiFilter, AiFilterAiLyrics, StringComparison.Ordinal);
    public bool IsNonAiOnlyFilterSelected => string.Equals(_selectedAiFilter, AiFilterNonAiOnly, StringComparison.Ordinal);

    [RelayCommand]
    private async Task ToggleDownloadedFilterAsync()
    {
        await RefreshDownloadedSongIdsAsync();
        IsDownloadedFilterActive = !IsDownloadedFilterActive;
        IsAiPanelOpen = false;
        IsGenrePanelOpen = false;
        IsArtistPanelOpen = false;
        IsTitlePanelOpen = false;
        UpdateHasAnyActiveFilters();
        RefreshAvailableGenres();
        RefreshAvailableArtists();
        RefreshGenreFilterItems();
        RefreshArtistFilterItems();
        ApplyFilters();
    }

    partial void OnGenreSearchTextChanged(string? value) => RefreshGenreFilterItems();
    partial void OnArtistSearchTextChanged(string? value) => RefreshArtistFilterItems();

    partial void OnTitleSearchTextChanged(string? value)
    {
        UpdateTitlePillText();
        RefreshAvailableGenres();
        RefreshAvailableArtists();
        RefreshGenreFilterItems();
        RefreshArtistFilterItems();
        ApplyFilters();
    }

    [RelayCommand]
    private void ToggleAiPanel()
    {
        IsAiPanelOpen = !IsAiPanelOpen;
        if (IsAiPanelOpen)
        {
            IsGenrePanelOpen = false;
            IsArtistPanelOpen = false;
            IsTitlePanelOpen = false;
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
            IsTitlePanelOpen = false;
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
            IsTitlePanelOpen = false;
            ArtistSearchText = null;
            RefreshArtistFilterItems();
        }
    }

    [RelayCommand]
    private void ToggleTitlePanel()
    {
        IsTitlePanelOpen = !IsTitlePanelOpen;
        if (IsTitlePanelOpen)
        {
            IsAiPanelOpen = false;
            IsGenrePanelOpen = false;
            IsArtistPanelOpen = false;
            // Deliberately NOT clearing TitleSearchText the way the genre and artist panels clear
            // theirs: those search boxes only narrow an option list, so reopening one wants a clean
            // slate. This one holds the live filter - clearing it here would drop the user's filter
            // every time they reopened the panel to edit it.
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
            AiFilterAny => AiFilterAny,
            AiFilterAiMusic => AiFilterAiMusic,
            AiFilterAiVocals => AiFilterAiVocals,
            AiFilterAiLyrics => AiFilterAiLyrics,
            AiFilterNonAiOnly => AiFilterNonAiOnly,
            _ => AiFilterAll
        };

        IsAiPanelOpen = false;

        NotifyAiFilterSelectionChanged();
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
        UpdateHasAnyActiveFilters();
    }

    private void UpdateArtistPillText()
    {
        HasActiveArtistFilters = SelectedArtists.Count > 0;
        ArtistPillText = SelectedArtists.Count > 0
            ? $"Artist ({SelectedArtists.Count})"
            : "Artist";
        UpdateHasAnyActiveFilters();
    }

    private void UpdateTitlePillText()
    {
        HasActiveTitleFilter = !string.IsNullOrWhiteSpace(TitleSearchText);
        UpdateHasAnyActiveFilters();
    }

    private void UpdateAiPillText()
    {
        HasActiveAiFilter = _selectedAiFilter != AiFilterAll;
        AiPillText = _selectedAiFilter switch
        {
            AiFilterAny => "Any AI",
            AiFilterAiMusic => "AI Music",
            AiFilterAiVocals => "AI Vocals",
            AiFilterAiLyrics => "AI Lyrics",
            AiFilterNonAiOnly => "Non-AI Music",
            _ => "Music Type"
        };
        UpdateHasAnyActiveFilters();
    }

    private void UpdateHasAnyActiveFilters()
    {
        HasAnyActiveFilters = HasActiveAiFilter || HasActiveGenreFilters || HasActiveArtistFilters
            || HasActiveTitleFilter || IsDownloadedFilterActive;
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

        GenreFilterItems.ReplaceAll(items);
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

        ArtistFilterItems.ReplaceAll(items);
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
        IEnumerable<SongDto> songs = ApplyGlobalFilters(_allSongs);
        if (SelectedArtists.Count > 0)
            songs = songs.Where(s => SelectedArtists.Contains(s.ArtistName));
        return songs;
    }

    private IEnumerable<SongDto> CrossFilterSongsByGenre()
    {
        IEnumerable<SongDto> songs = ApplyGlobalFilters(_allSongs);
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
        IsDownloadedFilterActive = false;
        NotifyAiFilterSelectionChanged();
        UpdateGenrePillText();
        UpdateArtistPillText();
        UpdateAiPillText();
        GenreSearchText = null;
        ArtistSearchText = null;
        TitleSearchText = null;
        UpdateTitlePillText();
        IsAiPanelOpen = false;
        IsGenrePanelOpen = false;
        IsArtistPanelOpen = false;
        IsTitlePanelOpen = false;
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
        IEnumerable<SongDto> filtered = ApplyGlobalFilters(_allSongs);

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

        Songs.ReplaceAll(filtered);

        SynchronizeVisibleQueue();

        // ReplaceAll rebuilt the collection, so the flag has to be re-applied to the new rows -
        // and SynchronizeVisibleQueue may itself have moved the queue underneath us.
        SyncCurrentSongFromPlayback();
    }

    private void SynchronizeVisibleQueue()
    {
        if (!_playbackService.HasPlaylist || Songs.Count == 0)
        {
            return;
        }

        var visibleSongs = Songs.ToList();
        var currentSongOutsideVisibleQueue = PlaybackQueueSelection.HasCurrentSongOutsideQueue(_playbackService, visibleSongs);
        if (PlaybackQueueSelection.HasEquivalentActiveQueue(_playbackService, visibleSongs) && !currentSongOutsideVisibleQueue)
        {
            return;
        }

        var startIndex = PlaybackQueueSelection.ResolveCurrentSongIndex(_playbackService, visibleSongs);
        _playbackService.SetPlaylist(
            visibleSongs,
            startIndex,
            currentSongOutsideVisibleQueue
                ? PlaybackQueueStartBehavior.RestartAtRequestedIndex
                : PlaybackQueueStartBehavior.PreserveCurrentSongIfPresent,
            BuildVisibleQueueDescription());
    }

    private string BuildVisibleQueueDescription()
    {
        var filters = new List<string>();

        if (SelectedGenres.Count > 0)
        {
            filters.Add($"Genres: {string.Join(", ", SelectedGenres.OrderBy(genre => genre, StringComparer.OrdinalIgnoreCase))}");
        }

        if (SelectedArtists.Count > 0)
        {
            filters.Add($"Artists: {string.Join(", ", SelectedArtists.OrderBy(artist => artist, StringComparer.OrdinalIgnoreCase))}");
        }

        var title = TitleSearchText?.Trim();
        if (!string.IsNullOrEmpty(title))
        {
            filters.Add($"Title: {title}");
        }

        var musicTypeFilter = _selectedAiFilter switch
        {
            AiFilterAny => "Music Type: Any AI",
            AiFilterAiMusic => "Music Type: AI Music",
            AiFilterAiVocals => "Music Type: AI Vocals",
            AiFilterAiLyrics => "Music Type: AI Lyrics",
            AiFilterNonAiOnly => "Music Type: Non-AI Music",
            _ => null
        };
        if (musicTypeFilter != null)
        {
            filters.Add(musicTypeFilter);
        }

        if (IsDownloadedFilterActive)
        {
            filters.Add("Downloaded");
        }

        return filters.Count == 0
            ? PlaybackQueueDescriptions.UnfilteredMediaLibrary
            : PlaybackQueueDescriptions.FilteredMediaLibrary(filters);
    }

    private IEnumerable<SongDto> FilterSongsByAiSelection(IEnumerable<SongDto> songs)
    {
        return _selectedAiFilter switch
        {
            AiFilterAny => songs.Where(HasAnyAiDisclosure),
            AiFilterAiMusic => songs.Where(s => s.IsAiGenerated),
            AiFilterAiVocals => songs.Where(s => s.IsAiVocals),
            AiFilterAiLyrics => songs.Where(s => s.IsAiLyrics),
            AiFilterNonAiOnly => songs.Where(s => !HasAnyAiDisclosure(s)),
            _ => songs
        };
    }

    /// <summary>
    /// The filters that apply everywhere: downloaded, music type, and title. Sits underneath
    /// ApplyFilters and both cross-filter helpers, so a clause added here narrows the visible songs
    /// and the genre/artist option lists and counts together.
    /// </summary>
    private IEnumerable<SongDto> ApplyGlobalFilters(IEnumerable<SongDto> songs)
    {
        if (IsDownloadedFilterActive)
        {
            songs = songs.Where(song => _downloadedSongIds.Contains(song.Id));
        }

        var title = TitleSearchText?.Trim();
        if (!string.IsNullOrEmpty(title))
        {
            songs = songs.Where(song => song.SongTitle.Contains(title, StringComparison.OrdinalIgnoreCase));
        }

        return FilterSongsByAiSelection(songs);
    }

    private static bool HasAnyAiDisclosure(SongDto song)
    {
        return song.IsAiGenerated || song.IsAiVocals || song.IsAiLyrics;
    }

    private void NotifyAiFilterSelectionChanged()
    {
        OnPropertyChanged(nameof(IsAllAiFilterSelected));
        OnPropertyChanged(nameof(IsAnyAiFilterSelected));
        OnPropertyChanged(nameof(IsAiMusicFilterSelected));
        OnPropertyChanged(nameof(IsAiVocalsFilterSelected));
        OnPropertyChanged(nameof(IsAiLyricsFilterSelected));
        OnPropertyChanged(nameof(IsNonAiOnlyFilterSelected));
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

        AvailableGenres.ReplaceAll(genres);
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

        AvailableArtists.ReplaceAll(artists);
    }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    /// <summary>
    /// True when the visible list came from the offline catalog rather than the API, meaning it holds
    /// only songs whose audio is downloaded. Drives the offline banner and the empty-state copy.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyStateTitle))]
    [NotifyPropertyChangedFor(nameof(EmptyStateDetail))]
    public partial bool IsOfflineCatalog { get; set; }

    public string EmptyStateTitle => IsOfflineCatalog || _networkStatusService?.HasNoNetworkAccess == true
        ? "You're offline"
        : "No songs available";

    public string EmptyStateDetail => IsOfflineCatalog || _networkStatusService?.HasNoNetworkAccess == true
        ? "Only downloaded songs can be played right now. Reconnect to browse the full library."
        : "Pull down to refresh.";

    /// <summary>
    /// False when the device has no network at all. Actions that need the server (reporting a song,
    /// tipping a creator) are hidden rather than left to fail with a generic error on tap. Gated on
    /// <see cref="INetworkStatusService.HasNoNetworkAccess"/> rather than the pessimistic
    /// <see cref="INetworkStatusService.IsOffline"/>, which is also true on a constrained connection
    /// that can still reach the server.
    /// </summary>
    public bool CanUseServerActions => _networkStatusService?.HasNoNetworkAccess != true;

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

        var outcome = await OptimisticLikeStateUpdater.ApplyAsync(_musicService, song, LikeAction.ThumbsUp);
        await RatingRequiresStreamNotice.ReportAsync(outcome, _alertService);
    }

    [RelayCommand]
    private async Task DislikeSongAsync(SongDto? song)
    {
        if (song == null) return;

        if (!await RequireAuthenticatedUserAsync("dislike songs"))
            return;

        var outcome = await OptimisticLikeStateUpdater.ApplyAsync(_musicService, song, LikeAction.ThumbsDown);
        await RatingRequiresStreamNotice.ReportAsync(outcome, _alertService);
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

            // Read the source from this load's own result: home and the playlist player reload on the
            // same connectivity change, and the service's last-load properties are shared between us.
            var catalog = SongCatalogOutcome.For(songs, _musicService);
            var songsSource = catalog.Source;

            // Offline states get a friendly empty state; only a genuine server failure while online
            // still surfaces the raw diagnostic (which includes the API URL).
            IsOfflineCatalog = songsSource == SongCatalogSource.OfflineCache
                || (songsSource == SongCatalogSource.Unavailable && _networkStatusService?.HasNoNetworkAccess == true);
            ErrorMessage = songsSource == SongCatalogSource.Unavailable
                ? catalog.Error
                : null;

            var orderedSongs = await Task.Run(() =>
            {
                foreach (var song in songs)
                {
                    song.ShareUrl = SongDto.BuildShareUrl(song.Id, _appConfig.WebBaseUrl);
                }

                return SongDisplayOrderSorter.OrderForLibrary(songs);
            });

            _allSongs.Clear();
            _allSongs.AddRange(orderedSongs);
            await RefreshDownloadedSongIdsAsync();
            await HydrateArtworkAsync(orderedSongs);

            // Reset filters when reloading
            SelectedGenres.Clear();
            SelectedArtists.Clear();
            _selectedAiFilter = AiFilterAll;
            IsDownloadedFilterActive = false;
            TitleSearchText = null;
            NotifyAiFilterSelectionChanged();
            UpdateGenrePillText();
            UpdateArtistPillText();
            UpdateTitlePillText();
            IsGenrePanelOpen = false;
            IsArtistPanelOpen = false;
            IsTitlePanelOpen = false;
            RefreshAvailableGenres();
            RefreshAvailableArtists();
            ApplyFilters();

            // Skip the like/status calls unless the catalog itself came back live. Offline they are two
            // more requests that each hang for the full HTTP timeout after the songs call already
            // failed, and the cached songs already carry their last-known counts.
            if (songsSource == SongCatalogSource.Live)
            {
                // Load like counts and user like status in parallel
                await Task.WhenAll(
                    LoadLikeCountsAsync(orderedSongs),
                    LoadUserLikeStatusAsync(orderedSongs));
            }
            else
            {
                SeedLikeCountsFromCachedSongs(orderedSongs);
            }

            // Unconditional: offline the status call above is skipped, so this is the only thing that
            // knows which of these songs the user has already listened to.
            UserSongRatingStateApplier.SeedFromLocalStore(orderedSongs, _userStreamedSongStore);
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
            var songsById = songs.ToDictionary(song => song.Id);
            foreach (var lc in likeCounts)
            {
                _likeCounts[lc.SongMetadataId] = (lc.LikeCount, lc.DislikeCount);

                if (songsById.TryGetValue(lc.SongMetadataId, out var song))
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

    /// <summary>
    /// Points each song's artwork at its locally cached copy. Best-effort: artwork is decorative, so a
    /// failure here must never break the song list.
    /// </summary>
    private async Task HydrateArtworkAsync(IReadOnlyList<SongDto> songs)
    {
        if (_songArtworkHydrator == null)
            return;

        try
        {
            await _songArtworkHydrator.HydrateAsync(songs);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to hydrate song artwork: {ex.Message}");
        }
    }

    /// <summary>
    /// Populates the like-count lookup from the songs themselves rather than the API. The offline
    /// catalog persists the counts that were current when it was written, so GetLikeCount/GetDislikeCount
    /// keep working offline instead of reporting zero for everything.
    /// </summary>
    private void SeedLikeCountsFromCachedSongs(List<SongDto> songs)
    {
        _likeCounts.Clear();
        foreach (var song in songs)
        {
            _likeCounts[song.Id] = (song.LikeCount, song.DislikeCount);
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
            UserSongRatingStateApplier.ApplyServerStatuses(statuses, songs, _userStreamedSongStore);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load user like status: {ex.Message}");
        }
    }

    private async Task RefreshDownloadedSongIdsAsync()
    {
        if (_audioCacheService == null)
        {
            return;
        }

        try
        {
            var statuses = await _audioCacheService.GetCacheStatusesAsync(_allSongs.ToList());
            _downloadedSongIds.Clear();
            foreach (var status in statuses.Values)
            {
                if (status.IsLocalReady)
                {
                    _downloadedSongIds.Add(status.SongId);
                }
            }
        }
        catch (Exception ex)
        {
            // A cache-status failure (e.g. a faulted Media3 cache) must not abort the library
            // load or crash the Downloaded-filter command. Degrade to an empty downloaded set
            // (the filter simply shows nothing as downloaded) rather than throwing.
            _downloadedSongIds.Clear();
            System.Diagnostics.Debug.WriteLine($"Failed to refresh downloaded song ids: {ex.Message}");
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

    [RelayCommand]
    private void TogglePlayPause() => _playbackService.TogglePlayPause();

    [RelayCommand]
    private void Stop() => _playbackService.Stop();

    private async Task OnShowSubscribeCta()
    {
        var isSignedIn = _authService.IsLoggedIn;
        var subscribe = await _alertService.ShowConfirmAsync(
            SubscriptionPurchaseGate.PreviewLimitTitle,
            SubscriptionPurchaseGate.PreviewLimitMessage(isSignedIn),
            SubscriptionPurchaseGate.PreviewLimitAccept(isSignedIn),
            SubscriptionPurchaseGate.PreviewLimitDecline);

        if (subscribe)
        {
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
