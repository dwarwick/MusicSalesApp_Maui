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
    private readonly Dictionary<int, (int likes, int dislikes)> _likeCounts = new();

    // All songs (unfiltered source of truth)
    private readonly List<SongDto> _allSongs = [];

    // Stream tracking state
    private int _streamQualifyingSeconds = 30; // default, overwritten from server
    private int _streamTrackingSongId;
    private double _continuousPlaybackSeconds;
    private bool _streamRecordedForCurrentSong;

    public MusicLibraryViewModel(IMusicService musicService, IAlertService alertService, ISignalRService signalRService)
    {
        _musicService = musicService;
        _alertService = alertService;
        _signalRService = signalRService;

        // Subscribe to real-time updates
        _signalRService.OnStreamCountUpdated += HandleStreamCountUpdated;
        _signalRService.OnLikeCountUpdated += HandleLikeCountUpdated;
    }

    public ObservableCollection<SongDto> Songs { get; } = [];

    // --- Filter state ---

    public ObservableCollection<string> AvailableGenres { get; } = [];
    public ObservableCollection<string> AvailableArtists { get; } = [];

    [ObservableProperty]
    public partial string? SelectedGenre { get; set; }

    [ObservableProperty]
    public partial string? SelectedArtist { get; set; }

    partial void OnSelectedGenreChanged(string? value)
    {
        RefreshAvailableArtists();
        ApplyFilters();
    }

    partial void OnSelectedArtistChanged(string? value)
    {
        RefreshAvailableGenres();
        ApplyFilters();
    }

    [RelayCommand]
    private void ClearFilters()
    {
        SelectedGenre = null;
        SelectedArtist = null;
        RefreshAvailableGenres();
        RefreshAvailableArtists();
        ApplyFilters();
    }

    /// <summary>
    /// Rebuilds the Songs collection from _allSongs using current filter selections.
    /// </summary>
    internal void ApplyFilters()
    {
        IEnumerable<SongDto> filtered = _allSongs;

        if (!string.IsNullOrEmpty(SelectedGenre))
        {
            filtered = filtered.Where(s =>
                string.Equals(s.Genre, SelectedGenre, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrEmpty(SelectedArtist))
        {
            filtered = filtered.Where(s =>
                string.Equals(s.ArtistName, SelectedArtist, StringComparison.OrdinalIgnoreCase));
        }

        Songs.Clear();
        foreach (var song in filtered)
        {
            Songs.Add(song);
        }
    }

    /// <summary>
    /// Refreshes AvailableGenres, cross-filtered by the currently selected artist.
    /// </summary>
    internal void RefreshAvailableGenres()
    {
        var songs = (IEnumerable<SongDto>)_allSongs;

        if (!string.IsNullOrEmpty(SelectedArtist))
        {
            songs = songs.Where(s =>
                string.Equals(s.ArtistName, SelectedArtist, StringComparison.OrdinalIgnoreCase));
        }

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
    /// Refreshes AvailableArtists, cross-filtered by the currently selected genre.
    /// </summary>
    internal void RefreshAvailableArtists()
    {
        var songs = (IEnumerable<SongDto>)_allSongs;

        if (!string.IsNullOrEmpty(SelectedGenre))
        {
            songs = songs.Where(s =>
                string.Equals(s.Genre, SelectedGenre, StringComparison.OrdinalIgnoreCase));
        }

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
    /// Also tracks continuous playback time for stream count qualification.
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
    }

    private void TrackStreamPlayback(TimeSpan position, TimeSpan previousPosition)
    {
        if (CurrentlyPlayingSong == null || !IsPlaying || _streamRecordedForCurrentSong)
            return;

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

        // Auth stub — no auth implemented yet
        await _alertService.DisplayAlertAsync("Login Required",
            "Please log in to like songs.", "OK");
    }

    [RelayCommand]
    private async Task DislikeSongAsync(SongDto? song)
    {
        if (song == null) return;

        // Auth stub — no auth implemented yet
        await _alertService.DisplayAlertAsync("Login Required",
            "Please log in to dislike songs.", "OK");
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
            SelectedGenre = null;
            SelectedArtist = null;
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
