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
    private readonly IPlaybackService _playbackService;
    private readonly IAppConfig _appConfig;
    private readonly Dictionary<int, (int likes, int dislikes)> _likeCounts = new();

    // All songs (unfiltered source of truth)
    private readonly List<SongDto> _allSongs = [];

    public MusicLibraryViewModel(
        IMusicService musicService,
        IAlertService alertService,
        ISignalRService signalRService,
        IAuthService authService,
        INavigationService navigationService,
        IPlaybackService playbackService,
        IAppConfig appConfig)
    {
        _musicService = musicService;
        _alertService = alertService;
        _signalRService = signalRService;
        _authService = authService;
        _navigationService = navigationService;
        _playbackService = playbackService;
        _appConfig = appConfig;

        // Subscribe to real-time updates
        _signalRService.OnStreamCountUpdated += HandleStreamCountUpdated;
        _signalRService.OnLikeCountUpdated += HandleLikeCountUpdated;

        // Wire subscribe CTA from playback service
        _playbackService.ShowSubscribeCtaRequested += OnShowSubscribeCta;
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

            System.Diagnostics.Debug.WriteLine($"[MusicLibrary] WebBaseUrl = '{_appConfig.WebBaseUrl}'");
            Console.WriteLine($"[MusicLibrary] WebBaseUrl = '{_appConfig.WebBaseUrl}'");
            foreach (var song in songs)
            {
                song.ShareUrl = SongDto.BuildShareUrl(song.Id, _appConfig.WebBaseUrl);
                System.Diagnostics.Debug.WriteLine($"[MusicLibrary] Song '{song.SongTitle}' → ShareUrl = '{song.ShareUrl}'");
                Console.WriteLine($"[MusicLibrary] Song '{song.SongTitle}' → ShareUrl = '{song.ShareUrl}'");
            }

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

            // Load like counts and user like status in parallel
            await Task.WhenAll(
                LoadLikeCountsAsync(songs),
                LoadUserLikeStatusAsync(songs));
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
    private void PlaySong(SongDto song) => _playbackService.PlaySong(song);

    [RelayCommand]
    private void TogglePlayPause() => _playbackService.TogglePlayPause();

    [RelayCommand]
    private void Stop() => _playbackService.Stop();

    private async Task OnShowSubscribeCta()
    {
        await _alertService.DisplayAlertAsync("Preview Limit",
            "Subscribe at streamtunes.net for unlimited listening!", "OK");
    }
}
