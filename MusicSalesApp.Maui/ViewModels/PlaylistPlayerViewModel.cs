using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.ViewModels;

[QueryProperty(nameof(GenreName), "GenreName")]
[QueryProperty(nameof(ArtistName), "ArtistName")]
[QueryProperty(nameof(PlaylistIdParam), "PlaylistId")]
[QueryProperty(nameof(RecommendedUserIdParam), "RecommendedUserId")]
[QueryProperty(nameof(TopStreamedWindow), "TopStreamedWindow")]
public partial class PlaylistPlayerViewModel : ObservableObject
{
    private readonly IMusicService _musicService;
    private readonly IAlertService _alertService;
    private readonly IUserStreamedSongStore? _userStreamedSongStore;
    private readonly IAuthService _authService;
    private readonly INavigationService _navigationService;
    private readonly IPlaybackService _playbackService;
    private readonly IMediaPlaybackOnboardingService _mediaPlaybackOnboardingService;
    private readonly ISignalRService _signalRService;
    private readonly IAppConfig _appConfig;
    private readonly IBillingService _billingService;
    private readonly IPlaylistService _playlistService;
    private readonly INetworkStatusService? _networkStatusService;
    private readonly ISongArtworkHydrator? _songArtworkHydrator;

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
        IPlaylistService playlistService,
        INetworkStatusService? networkStatusService = null,
        ISongArtworkHydrator? songArtworkHydrator = null,
        IUserStreamedSongStore? userStreamedSongStore = null)
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
        _networkStatusService = networkStatusService;
        _songArtworkHydrator = songArtworkHydrator;
        _userStreamedSongStore = userStreamedSongStore;

        AttachSubscriptions();

        Songs.CollectionChanged += (_, _) =>
        {
            HasSongs = Songs.Count > 0;
            OnPropertyChanged(nameof(TrackCountLabel));
            OnPropertyChanged(nameof(TotalStreamsLabel));
        };
    }

    public IPlaybackService PlaybackService => _playbackService;

    public void Activate()
    {
        AttachSubscriptions();

        // Re-read what is playing, do not merely start listening for the NEXT change. Cleanup()
        // detaches on OnDisappearing, so every track that advances while this page is off screen -
        // most commonly while the app is backgrounded - raises its change to nobody. Without this
        // the header stays on whatever was playing when the page left, while the now-playing bar,
        // which does resync on Activate, shows the truth. That is the exact split this fixes.
        SyncCurrentSongFromPlayback();
    }

    /// <summary>
    /// Point <see cref="CurrentSong"/> at whatever the playback service is actually on.
    /// </summary>
    /// <remarks>
    /// Resolved against the visible list rather than taken straight off the service, so the header
    /// shows the same SongDto instance the track list holds - the one carrying this page's like
    /// counts and cached artwork. Falls back to the service's own copy only when the list has not
    /// loaded yet.
    /// </remarks>
    private void SyncCurrentSongFromPlayback()
    {
        CurrentSong = Songs.Count == 0
            ? _playbackService.CurrentSong
            : PlaybackQueueSelection.ResolveCurrentSong(_playbackService, Songs);
        OnPropertyChanged(nameof(ShareUrl));
        OnPropertyChanged(nameof(ShowAboutPanel));
        OnPropertyChanged(nameof(ShowArtistPanel));
        OnPropertyChanged(nameof(HasUnlimitedAccess));
        OnPropertyChanged(nameof(PersonaWebsiteUrl));
        MarkNowPlayingRow();
    }

    /// <summary>
    /// Flag the track list row the player is on, so the list shows WHICH song is playing.
    /// </summary>
    /// <remarks>
    /// Walks the list rather than tracking the previous row, because the queue can be rebuilt
    /// underneath this - a filter change or a jump to another playlist replaces Songs wholesale,
    /// and a remembered reference would then clear a flag on a row that is no longer displayed
    /// while leaving the real one lit.
    /// </remarks>
    private void MarkNowPlayingRow()
    {
        var playingId = CurrentSong?.Id;
        for (var index = 0; index < Songs.Count; index++)
        {
            var song = Songs[index];
            song.IsNowPlaying = song.Id == playingId;
            song.TrackNumber = index + 1;
        }
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
        if (_networkStatusService != null)
            _networkStatusService.PropertyChanged += HandleNetworkStatusChanged;
        _subscriptionsAttached = true;
    }

    /// <summary>
    /// Refreshes the offline-dependent properties, then reloads so the playlist narrows to downloaded
    /// songs going offline and restores the full list coming back online.
    /// </summary>
    private void HandleNetworkStatusChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!NetworkStatusChange.AffectsConnectivity(e.PropertyName))
            return;

        OnPropertyChanged(nameof(CanUseServerActions));
        OnPropertyChanged(nameof(IsReorderEnabled));
        OnPropertyChanged(nameof(CanEditPlaylist));
        OnPropertyChanged(nameof(ShowOfflineEditingNotice));

        if (IsLoading)
            return;

        _ = LoadPlaylistAsync();
    }

    public ObservableRangeCollection<SongDto> Songs { get; } = [];

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

    /// <summary>
    /// Window key of a "most streamed" playlist ("Day", "Week", ...), when that is what was opened.
    /// </summary>
    [ObservableProperty]
    public partial string? TopStreamedWindow { get; set; }

    /// <summary>
    /// Heading for the period stream count column - "Today", "This Week" and so on - or null when the
    /// list has no period of its own.
    /// </summary>
    /// <remarks>
    /// Set only by the four rolling most-streamed playlists. The list is ranked on the period count
    /// while <c>SongDto.StreamCount</c> is the lifetime total, so without the extra column a correctly
    /// ordered "Top 10 Today" reads as mis-sorted.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPeriodStreamCount))]
    public partial string? PeriodStreamLabel { get; set; }

    public bool ShowPeriodStreamCount => !string.IsNullOrEmpty(PeriodStreamLabel);

    /// <summary>True when the loaded playlist is a user-owned custom playlist (reorder/remove allowed).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReorderEnabled))]
    [NotifyPropertyChangedFor(nameof(ShowTracksHeader))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyPlaylistPrompt))]
    [NotifyPropertyChangedFor(nameof(ShowOfflineEditingNotice))]
    [NotifyPropertyChangedFor(nameof(CanEditPlaylist))]
    public partial bool IsUserPlaylist { get; set; }

    /// <summary>True when the user can drag-reorder tracks (custom playlist + active subscription + online).</summary>
    public bool IsReorderEnabled => IsUserPlaylist && HasActiveSubscription && CanUseServerActions;

    /// <summary>
    /// False when the device has no network at all. Playlist editing, tipping, reporting and
    /// add-to-playlist all need the server, so their controls are hidden rather than left to fail on
    /// tap. Gated on <see cref="INetworkStatusService.HasNoNetworkAccess"/> rather than the
    /// pessimistic <see cref="INetworkStatusService.IsOffline"/>, so a constrained or unknown
    /// connection - where the server is still reachable - keeps them available.
    /// </summary>
    public bool CanUseServerActions => _networkStatusService?.HasNoNetworkAccess != true;

    /// <summary>
    /// True when the loaded playlist's edit controls (remove track) should be shown. Same rule as
    /// <see cref="IsUserPlaylist"/> as before, now also requiring a connection.
    /// </summary>
    public bool CanEditPlaylist => IsUserPlaylist && CanUseServerActions;

    /// <summary>True when a playlist is loaded that could be edited if the device were online.</summary>
    public bool ShowOfflineEditingNotice => IsUserPlaylist && !CanUseServerActions;

    /// <summary>Tracks Songs.Count changes so computed properties refresh.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyPlaylistPrompt))]
    [NotifyPropertyChangedFor(nameof(ShowTracksHeader))]
    public partial bool HasSongs { get; set; }

    /// <summary>Show the "Tracks" header + Add Songs button whenever a playlist is loaded for the user, or there's a current song.</summary>
    public bool ShowTracksHeader => IsUserPlaylist || HasSongs || CurrentSong is not null;

    /// <summary>Show the empty-custom-playlist call-to-action when we've finished loading a user playlist that has no songs.</summary>
    public bool ShowEmptyPlaylistPrompt => IsUserPlaylist && !HasSongs && !IsLoading;

    public bool IsCurrentTrackPreviewLimited => PreviewAccessPolicy.ShouldLimitPreview(_authService, CurrentSong);

    partial void OnGenreNameChanged(string? value)
    {
        if (!string.IsNullOrEmpty(value))
            _ = LoadPlaylistAsync();
    }

    partial void OnArtistNameChanged(string? value)
    {
        OnPropertyChanged(nameof(IsArtistTreatment));
        OnPropertyChanged(nameof(ModeLabel));
        OnPropertyChanged(nameof(ShowAboutPanel));
        OnPropertyChanged(nameof(ShowArtistPanel));
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

    partial void OnTopStreamedWindowChanged(string? value)
    {
        if (!string.IsNullOrEmpty(value))
            _ = LoadPlaylistAsync();
    }

    partial void OnHasActiveSubscriptionChanged(bool value)
    {
        OnPropertyChanged(nameof(IsReorderEnabled));
    }

    public string ShareUrl => CurrentSong?.ShareUrl ?? string.Empty;

    /// <summary>
    /// Whether this page is an artist's own page rather than a playlist that happens to have one.
    /// </summary>
    /// <remarks>
    /// Drives which trailing panel shows, matching the web. On an artist's page the bio is the
    /// page's own subject and gets an "About" panel with the full text; anywhere else it is
    /// context about someone else and gets "The artist", with their picture and a link out.
    /// Showing both would print the bio twice.
    /// </remarks>
    public bool IsArtistTreatment => !string.IsNullOrWhiteSpace(ArtistName);

    /// <summary>
    /// The violet chip above the title - what KIND of page this is.
    /// </summary>
    /// <remarks>
    /// Mirrors the web's GetModeLabel(). Uppercased here rather than in the markup so the label and
    /// its accessibility text are the same string.
    /// </remarks>
    public string ModeLabel =>
        IsArtistTreatment ? "ARTIST"
        : !string.IsNullOrWhiteSpace(GenreName) ? "GENRE"
        : "PLAYLIST";

    /// <summary>"1 track" / "12 tracks", pluralised.</summary>
    public string TrackCountLabel => Songs.Count == 1 ? "1 track" : $"{Songs.Count} tracks";

    /// <summary>
    /// Plays across every track on the page, which is what the web's artist header counts.
    /// </summary>
    /// <remarks>
    /// The sum of the visible list, not of the artist's whole catalogue: going offline narrows the
    /// list to downloaded songs, and a total that disagreed with the rows under it would read as a
    /// bug rather than as a filter.
    /// </remarks>
    public string TotalStreamsLabel => $"{Songs.Sum(song => song.StreamCount):N0} streams";

    /// <summary>Whether to say "Unlimited Access" rather than the preview warning.</summary>
    public bool HasUnlimitedAccess => !IsCurrentTrackPreviewLimited;

    /// <summary>The artist's own picture, for the round hero image.</summary>
    public string? PersonaWebsiteUrl => CurrentSong?.PersonaWebsiteUrl;

    /// <summary>The full-bio panel, on an artist's own page and only when there is a bio.</summary>
    public bool ShowAboutPanel =>
        IsArtistTreatment && !string.IsNullOrWhiteSpace(CurrentSong?.PersonaBio);

    /// <summary>The artist panel, everywhere else, and only when there is an artist to name.</summary>
    public bool ShowArtistPanel =>
        !IsArtistTreatment && !string.IsNullOrWhiteSpace(CurrentSong?.ArtistName);

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

        // Branch 3: one of the five global "most streamed" playlists, addressed by window key
        // because none of them has an id.
        if (!string.IsNullOrEmpty(TopStreamedWindow))
        {
            await LoadTopStreamedAsync(Uri.UnescapeDataString(TopStreamedWindow));
            return;
        }

        // Legacy: filter by Genre / Artist using full song list
        IsUserPlaylist = false;
        _loadedPlaylistId = null;
        _userPlaylistIdBySongId.Clear();

        var allSongs = await _musicService.GetSongsAsync();
        // This load's own error, not the service's shared last-load state - the library and home
        // reload on the same connectivity change and would otherwise overwrite it out from under us.
        var catalogError = SongCatalogOutcome.For(allSongs, _musicService).Error;
        if (allSongs.Count == 0 && !string.IsNullOrWhiteSpace(catalogError))
        {
            ErrorMessage = catalogError;
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

    /// <summary>
    /// Offline, "failed to load" is misleading - nothing is broken, the songs just aren't downloaded.
    /// </summary>
    private string ResolveLoadFailureMessage(string onlineMessage)
        => _networkStatusService?.HasNoNetworkAccess == true
            ? "You're offline. None of this playlist's songs are downloaded for offline playback."
            : onlineMessage;

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
                ErrorMessage = ResolveLoadFailureMessage("Failed to load playlist.");
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
                ErrorMessage = ResolveLoadFailureMessage("Failed to load recommended playlist.");
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
            Songs.ReplaceAll([]);
            return;
        }

        var mapped = result.Songs.Select(MapToSongDto).ToList();
        foreach (var ps in result.Songs.Where(ps => ps.UserPlaylistId.HasValue))
            _userPlaylistIdBySongId[ps.SongMetadataId] = ps.UserPlaylistId!.Value;

        await FinalizeLoadedSongsAsync(mapped);
    }

    /// <summary>
    /// Loads one "most streamed" playlist.
    /// </summary>
    /// <remarks>
    /// The server returns the songs in rank order - most streamed first - and this deliberately does
    /// not sort, so that order reaches the list intact.
    /// </remarks>
    private async Task LoadTopStreamedAsync(string window)
    {
        _userPlaylistIdBySongId.Clear();
        _loadedPlaylistId = null;
        IsUserPlaylist = false;

        var result = await _playlistService.GetTopStreamedSongsAsync(window);
        if (result == null)
        {
            ErrorMessage = ResolveLoadFailureMessage("Failed to load playlist.");
            return;
        }

        PlaylistTitle = result.PlaylistName;
        PeriodStreamLabel = result.PeriodLabel;

        if (result.Songs.Count == 0)
        {
            ErrorMessage = "This playlist has no songs yet.";
            Songs.ReplaceAll([]);
            return;
        }

        var mapped = result.Songs.Select(MapToSongDto).ToList();

        // The period count travels on the playlist DTO, not the song DTO the rest of the app shares,
        // so it is copied across here by song id.
        var periodCounts = result.Songs
            .Where(song => song.PeriodStreamCount.HasValue)
            .GroupBy(song => song.SongMetadataId)
            .ToDictionary(group => group.Key, group => group.First().PeriodStreamCount!.Value);

        foreach (var song in mapped)
        {
            song.PeriodStreamCount = periodCounts.TryGetValue(song.Id, out var count) ? count : null;
        }

        await FinalizeLoadedSongsAsync(mapped);
    }

    private async Task FinalizeLoadedSongsAsync(List<SongDto> list)
    {
        foreach (var song in list)
            song.ShareUrl = SongDto.BuildShareUrl(song.Id, _appConfig.WebBaseUrl);

        // Offline these two requests can only stall for the client timeout; the songs already carry
        // their last-known counts from whichever cache they were restored from.
        if (_networkStatusService?.HasNoNetworkAccess != true)
        {
            await Task.WhenAll(
                LoadLikeCountsAsync(list),
                LoadUserLikeStatusAsync(list));
        }

        // Unconditional: offline the status call above is skipped, so this is the only thing that knows
        // which of these songs the user has already listened to.
        UserSongRatingStateApplier.SeedFromLocalStore(list, _userStreamedSongStore);

        await HydrateArtworkAsync(list);

        Songs.ReplaceAll(list);
        MarkNowPlayingRow();

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

    /// <summary>
    /// Points each song's artwork at its locally cached copy. Best-effort - artwork is decorative.
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
        AlbumArtThumbUrl = ps.AlbumArtThumbUrl,
        AlbumArtHeroUrl = ps.AlbumArtHeroUrl,
        AlbumArtVersion = ps.AlbumArtVersion,
        PersonaImageUrl = ps.PersonaImageUrl,
        PersonaImageThumbUrl = ps.PersonaImageThumbUrl,
        PersonaImageHeroUrl = ps.PersonaImageHeroUrl,
        PersonaImageVersion = ps.PersonaImageVersion,
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

        var outcome = await OptimisticLikeStateUpdater.ApplyAsync(_musicService, CurrentSong, LikeAction.ThumbsUp);
        await RatingRequiresStreamNotice.ReportAsync(outcome, _alertService);
    }

    [RelayCommand]
    private async Task DislikeSongAsync()
    {
        if (CurrentSong == null) return;
        if (!await RequireAuthenticatedUserAsync("dislike songs")) return;

        var outcome = await OptimisticLikeStateUpdater.ApplyAsync(_musicService, CurrentSong, LikeAction.ThumbsDown);
        await RatingRequiresStreamNotice.ReportAsync(outcome, _alertService);
    }

    // --- Navigation ---

    // ViewBioAsync used to push a dedicated persona page. The bio is now rendered inline by
    // PersonaSectionView, which is where the web app puts it.

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
            UserSongRatingStateApplier.ApplyServerStatuses(statuses, songs, _userStreamedSongStore);
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
            SyncCurrentSongFromPlayback();
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
        if (_networkStatusService != null)
            _networkStatusService.PropertyChanged -= HandleNetworkStatusChanged;
        _subscriptionsAttached = false;
    }
}
