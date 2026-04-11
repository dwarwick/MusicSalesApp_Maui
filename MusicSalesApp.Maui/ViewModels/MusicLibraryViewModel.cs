using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.ViewModels;

public partial class MusicLibraryViewModel : ObservableObject
{
    private readonly IMusicService _musicService;
    private readonly IAlertService _alertService;
    private readonly ISignalRService _signalRService;
    private readonly IAuthService _authService;
    private readonly INavigationService _navigationService;
    private readonly Dictionary<int, (int likes, int dislikes)> _likeCounts = new();

    // All songs (unfiltered source of truth)
    private readonly List<SongDto> _allSongs = [];

    // Stream tracking state
    private int _streamQualifyingSeconds = 30; // default, overwritten from server
    private int _streamTrackingSongId;
    private double _continuousPlaybackSeconds;
    private bool _streamRecordedForCurrentSong;

    // Playback restriction state (subscribe CTA)
    private const double PreviewLimitSeconds = 60.0;
    private const int MinPreviewInterval = 2;
    private const int MaxPreviewIntervalExclusive = 5;
    private int _previewEndCount;
    private int _nextCtaThreshold;
    private readonly Random _random = new();

    public MusicLibraryViewModel(IMusicService musicService, IAlertService alertService, ISignalRService signalRService, IAuthService authService, INavigationService navigationService)
    {
        _musicService = musicService;
        _alertService = alertService;
        _signalRService = signalRService;
        _authService = authService;
        _navigationService = navigationService;
        _nextCtaThreshold = 0; // show on first preview end

        // Subscribe to real-time updates
        _signalRService.OnStreamCountUpdated += HandleStreamCountUpdated;
        _signalRService.OnLikeCountUpdated += HandleLikeCountUpdated;
    }

    public ObservableCollection<SongDto> Songs { get; } = [];

    // --- Filter state ---

    public ObservableCollection<string> AvailableGenres { get; } = [];
    public ObservableCollection<string> AvailableArtists { get; } = [];

    public HashSet<string> SelectedGenres { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> SelectedArtists { get; } = new(StringComparer.OrdinalIgnoreCase);

    public ObservableCollection<FilterItem> GenreFilterItems { get; } = [];
    public ObservableCollection<FilterItem> ArtistFilterItems { get; } = [];

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
    public partial bool HasActiveGenreFilters { get; set; }

    [ObservableProperty]
    public partial bool HasActiveArtistFilters { get; set; }

    partial void OnGenreSearchTextChanged(string? value) => RefreshGenreFilterItems();
    partial void OnArtistSearchTextChanged(string? value) => RefreshArtistFilterItems();

    [RelayCommand]
    private void ToggleGenrePanel()
    {
        IsGenrePanelOpen = !IsGenrePanelOpen;
        if (IsGenrePanelOpen)
        {
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

    private void UpdateGenrePillText()
    {
        HasActiveGenreFilters = SelectedGenres.Count > 0;
        GenrePillText = SelectedGenres.Count > 0
            ? $"Genre ({SelectedGenres.Count})"
            : "Genre";
    }

    private void UpdateArtistPillText()
    {
        HasActiveArtistFilters = SelectedArtists.Count > 0;
        ArtistPillText = SelectedArtists.Count > 0
            ? $"Artist ({SelectedArtists.Count})"
            : "Artist";
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
        IEnumerable<SongDto> songs = _allSongs;
        if (SelectedArtists.Count > 0)
            songs = songs.Where(s => SelectedArtists.Contains(s.ArtistName));
        return songs;
    }

    private IEnumerable<SongDto> CrossFilterSongsByGenre()
    {
        IEnumerable<SongDto> songs = _allSongs;
        if (SelectedGenres.Count > 0)
            songs = songs.Where(s => SelectedGenres.Contains(s.Genre));
        return songs;
    }

    [RelayCommand]
    private void ClearFilters()
    {
        SelectedGenres.Clear();
        SelectedArtists.Clear();
        UpdateGenrePillText();
        UpdateArtistPillText();
        GenreSearchText = null;
        ArtistSearchText = null;
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
        IEnumerable<SongDto> filtered = _allSongs;

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

    [ObservableProperty]
    public partial SongDto? CurrentlyPlayingSong { get; set; }

    [ObservableProperty]
    public partial bool IsPlaying { get; set; }

    // --- Playback progress ---

    [ObservableProperty]
    public partial double PlaybackProgress { get; set; }

    [ObservableProperty]
    public partial string FormattedPosition { get; set; } = "0:00";

    [ObservableProperty]
    public partial string FormattedDuration { get; set; } = "0:00";

    private TimeSpan _playbackPosition;
    private TimeSpan _playbackDuration;

    /// <summary>
    /// Called by the code-behind timer to update playback position/duration.
    /// Also tracks continuous playback time for stream count qualification
    /// and enforces preview limits for non-subscribers.
    /// </summary>
    public void UpdatePlaybackPosition(TimeSpan position, TimeSpan duration)
    {
        var previousPosition = _playbackPosition;
        _playbackPosition = position;
        _playbackDuration = duration;

        PlaybackProgress = duration.TotalSeconds > 0
            ? position.TotalSeconds / duration.TotalSeconds
            : 0;

        FormattedPosition = FormatDuration(position.TotalSeconds);
        FormattedDuration = FormatDuration(duration.TotalSeconds);

        // Track continuous playback for stream count
        TrackStreamPlayback(position, previousPosition);

        // Enforce preview limit for non-subscribers
        CheckPreviewLimit(position);
    }

    /// <summary>
    /// Checks if the current playback should be limited to 60 seconds.
    /// Returns true if playback should be paused due to preview limit.
    /// </summary>
    private void CheckPreviewLimit(TimeSpan position)
    {
        if (CurrentlyPlayingSong == null || !IsPlaying)
            return;

        // Don't limit subscribers
        if (_authService.HasActiveSubscription)
            return;

        // Don't limit creators playing their own songs
        if (_authService.IsCreator && CurrentlyPlayingSong.CreatorUserId == _authService.UserId)
            return;

        // Enforce 60s limit
        if (position.TotalSeconds >= PreviewLimitSeconds)
        {
            IsPlaying = false;
            PreviewLimitReached = true;
            _previewEndCount++;
            ShowSubscribeCtaIfNeeded();
        }
    }

    [ObservableProperty]
    public partial bool PreviewLimitReached { get; set; }

    private async void ShowSubscribeCtaIfNeeded()
    {
        if (_previewEndCount >= _nextCtaThreshold)
        {
            _nextCtaThreshold = _previewEndCount + _random.Next(MinPreviewInterval, MaxPreviewIntervalExclusive);

            await _alertService.DisplayAlertAsync("Preview Limit",
                "Subscribe at streamtunes.net for unlimited listening!", "OK");
        }
    }

    private void TrackStreamPlayback(TimeSpan position, TimeSpan previousPosition)
    {
        if (CurrentlyPlayingSong == null || !IsPlaying || _streamRecordedForCurrentSong)
            return;

        // Don't count streams for creators listening to their own songs
        if (_authService.IsCreator && CurrentlyPlayingSong.CreatorUserId == _authService.UserId)
        {
            _streamRecordedForCurrentSong = true; // prevent further checks
            return;
        }

        // If the song changed, reset tracking
        if (CurrentlyPlayingSong.Id != _streamTrackingSongId)
        {
            _streamTrackingSongId = CurrentlyPlayingSong.Id;
            _continuousPlaybackSeconds = 0;
            _streamRecordedForCurrentSong = false;
        }

        // Calculate elapsed since last tick (timer fires every 500ms)
        var elapsed = position.TotalSeconds - previousPosition.TotalSeconds;

        // Only count forward progress within reasonable bounds (not seeks)
        if (elapsed > 0 && elapsed < 2.0)
        {
            _continuousPlaybackSeconds += elapsed;
        }

        if (_continuousPlaybackSeconds >= _streamQualifyingSeconds)
        {
            _streamRecordedForCurrentSong = true;
            _ = RecordStreamInBackgroundAsync(CurrentlyPlayingSong.Id);
        }
    }

    private async Task RecordStreamInBackgroundAsync(int songMetadataId)
    {
        await _musicService.RecordStreamAsync(songMetadataId);
    }

    /// <summary>
    /// Fetches the stream qualifying seconds from the server. Call once at startup.
    /// </summary>
    public async Task LoadStreamQualifyingSecondsAsync()
    {
        _streamQualifyingSeconds = await _musicService.GetStreamQualifyingSecondsAsync();
    }

    /// <summary>
    /// Returns the TimeSpan for a given progress value (0-1), used for seeking.
    /// </summary>
    public TimeSpan GetSeekPosition(double progress)
    {
        return TimeSpan.FromSeconds(progress * _playbackDuration.TotalSeconds);
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

        await _musicService.ToggleLikeAsync(song.Id);
    }

    [RelayCommand]
    private async Task DislikeSongAsync(SongDto? song)
    {
        if (song == null) return;

        if (!await RequireAuthenticatedUserAsync("dislike songs"))
            return;

        await _musicService.ToggleDislikeAsync(song.Id);
    }

    /// <summary>
    /// Returns true if the user is logged in with a confirmed email (User role).
    /// Shows appropriate alerts and navigation if not.
    /// </summary>
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

            _allSongs.Clear();
            _allSongs.AddRange(songs);

            // Reset filters when reloading
            SelectedGenres.Clear();
            SelectedArtists.Clear();
            UpdateGenrePillText();
            UpdateArtistPillText();
            IsGenrePanelOpen = false;
            IsArtistPanelOpen = false;
            RefreshAvailableGenres();
            RefreshAvailableArtists();
            ApplyFilters();

            // Load like counts in parallel
            await LoadLikeCountsAsync(songs);
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

                // Also set on the SongDto for data binding in the card template
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
            // Non-fatal — cards still display without like counts
            System.Diagnostics.Debug.WriteLine($"Failed to load like counts: {ex.Message}");
        }
    }

    // --- SignalR real-time updates ---

    /// <summary>
    /// Starts the SignalR hub connections. Call from the page code-behind on appearing.
    /// </summary>
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

    // --- Playback commands ---

    [RelayCommand]
    private void PlaySong(SongDto song)
    {
        if (CurrentlyPlayingSong?.Id == song.Id && IsPlaying)
        {
            // Tapping the same song that's playing — pause it
            IsPlaying = false;
            return;
        }

        // Reset stream tracking for the new song
        _continuousPlaybackSeconds = 0;
        _streamRecordedForCurrentSong = false;
        _streamTrackingSongId = song.Id;
        PreviewLimitReached = false;

        CurrentlyPlayingSong = song;
        IsPlaying = true;
    }

    [RelayCommand]
    private void TogglePlayPause()
    {
        if (CurrentlyPlayingSong == null) return;
        IsPlaying = !IsPlaying;
    }

    [RelayCommand]
    private void Stop()
    {
        IsPlaying = false;
        CurrentlyPlayingSong = null;
        ResetPlaybackState();
    }

    private void ResetPlaybackState()
    {
        PlaybackProgress = 0;
        FormattedPosition = "0:00";
        FormattedDuration = "0:00";
        _playbackPosition = TimeSpan.Zero;
        _playbackDuration = TimeSpan.Zero;
        _continuousPlaybackSeconds = 0;
        _streamRecordedForCurrentSong = false;
        PreviewLimitReached = false;
    }

    public string FormatDuration(double? seconds)
    {
        if (seconds == null || double.IsNaN(seconds.Value) || double.IsInfinity(seconds.Value))
            return "0:00";
        var ts = TimeSpan.FromSeconds(seconds.Value);
        return ts.TotalHours >= 1
            ? ts.ToString(@"h\:mm\:ss")
            : ts.ToString(@"m\:ss");
    }
}
