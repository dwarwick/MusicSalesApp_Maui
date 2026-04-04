using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.ViewModels;

public partial class MusicLibraryViewModel : ObservableObject
{
    private readonly IMusicService _musicService;

    public MusicLibraryViewModel(IMusicService musicService)
    {
        _musicService = musicService;
    }

    public ObservableCollection<SongDto> Songs { get; } = [];

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial SongDto? CurrentlyPlayingSong { get; set; }

    [ObservableProperty]
    public partial bool IsPlaying { get; set; }

    [RelayCommand]
    private async Task LoadSongsAsync()
    {
        if (IsLoading) return;

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var songs = await _musicService.GetSongsAsync();
            Songs.Clear();
            foreach (var song in songs)
            {
                Songs.Add(song);
            }
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

    [RelayCommand]
    private void PlaySong(SongDto song)
    {
        if (CurrentlyPlayingSong?.Id == song.Id && IsPlaying)
        {
            // Tapping the same song that's playing — pause it
            IsPlaying = false;
            return;
        }

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
    }

    public string FormatDuration(double? seconds)
    {
        if (seconds == null) return "--:--";
        var ts = TimeSpan.FromSeconds(seconds.Value);
        return ts.TotalHours >= 1
            ? ts.ToString(@"h\:mm\:ss")
            : ts.ToString(@"m\:ss");
    }
}
