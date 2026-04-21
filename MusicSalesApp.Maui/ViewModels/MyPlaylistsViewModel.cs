using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.ViewModels;

public partial class MyPlaylistsViewModel : ObservableObject
{
    private readonly IPlaylistService _playlistService;
    private readonly IAuthService _authService;
    private readonly IAlertService _alertService;
    private readonly INavigationService _navigationService;

    public MyPlaylistsViewModel(
        IPlaylistService playlistService,
        IAuthService authService,
        IAlertService alertService,
        INavigationService navigationService)
    {
        _playlistService = playlistService;
        _authService = authService;
        _alertService = alertService;
        _navigationService = navigationService;
    }

    public ObservableCollection<PlaylistDto> Playlists { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    [NotifyPropertyChangedFor(nameof(ShowPlaylists))]
    public partial bool IsLoading { get; set; } = true;

    [ObservableProperty]
    public partial bool HasActiveSubscription { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    /// <summary>
    /// True when the user has no playlists to show (and loading is complete).
    /// </summary>
    public bool ShowEmptyState => !IsLoading && Playlists.Count == 0;

    public bool ShowPlaylists => !IsLoading && Playlists.Count > 0;

    /// <summary>
    /// Banner copy shown above the list when the user lacks an active subscription.
    /// </summary>
    public string SubscriptionBannerText =>
        "To create playlists, you need an active subscription. Tap a system playlist above to listen.";

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            HasActiveSubscription = _authService.HasActiveSubscription;
            var items = await _playlistService.GetMyPlaylistsAsync();

            Playlists.Clear();
            foreach (var p in items)
                Playlists.Add(p);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load playlists: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(ShowEmptyState));
            OnPropertyChanged(nameof(ShowPlaylists));
        }
    }

    [RelayCommand]
    public Task OpenPlaylistAsync(PlaylistDto? playlist)
    {
        if (playlist == null) return Task.CompletedTask;
        return _navigationService.GoToAsync("playlist-player", new Dictionary<string, object>
        {
            // Shell.ApplyQueryAttributes does a direct cast for non-string values; the
            // target PlaylistIdParam property is string?, so pass the int as a string.
            ["PlaylistId"] = playlist.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)
        });
    }

    [RelayCommand]
    public async Task CreatePlaylistAsync()
    {
        if (!HasActiveSubscription)
        {
            await _alertService.DisplayAlertAsync(
                "Subscription required",
                "An active subscription is required to create playlists.",
                "OK");
            return;
        }

        var name = await _alertService.ShowPromptAsync(
            "New playlist",
            "Enter a name for your playlist:",
            "Create",
            "Cancel",
            placeholder: "Playlist name",
            maxLength: 200);
        if (string.IsNullOrWhiteSpace(name)) return;

        var result = await _playlistService.CreatePlaylistAsync(name.Trim());
        if (result.RequiresSubscription)
        {
            HasActiveSubscription = false;
            await _alertService.DisplayAlertAsync(
                "Subscription required",
                "An active subscription is required to create playlists.",
                "OK");
            return;
        }
        if (!result.Success || result.Value == null)
        {
            await _alertService.DisplayAlertAsync("Error", result.ErrorMessage ?? "Failed to create playlist.", "OK");
            return;
        }

        await LoadAsync();
    }

    [RelayCommand]
    public async Task RenamePlaylistAsync(PlaylistDto? playlist)
    {
        if (playlist == null || playlist.IsSystemGenerated) return;

        var name = await _alertService.ShowPromptAsync(
            "Rename playlist",
            "Enter a new name:",
            "Save",
            "Cancel",
            initialValue: playlist.Name,
            maxLength: 200);
        if (string.IsNullOrWhiteSpace(name) || name.Trim() == playlist.Name) return;

        var result = await _playlistService.RenamePlaylistAsync(playlist.Id, name.Trim());
        if (result.RequiresSubscription)
        {
            await _alertService.DisplayAlertAsync(
                "Subscription required",
                "An active subscription is required to rename playlists.",
                "OK");
            return;
        }
        if (!result.Success)
        {
            await _alertService.DisplayAlertAsync("Error", result.ErrorMessage ?? "Failed to rename playlist.", "OK");
            return;
        }

        await LoadAsync();
    }

    [RelayCommand]
    public async Task DeletePlaylistAsync(PlaylistDto? playlist)
    {
        if (playlist == null || playlist.IsSystemGenerated) return;

        var confirm = await _alertService.ShowConfirmAsync(
            "Delete playlist",
            $"Delete \"{playlist.Name}\"? This cannot be undone.",
            "Delete",
            "Cancel");
        if (!confirm) return;

        var result = await _playlistService.DeletePlaylistAsync(playlist.Id);
        if (!result.Success)
        {
            await _alertService.DisplayAlertAsync("Error", result.ErrorMessage ?? "Failed to delete playlist.", "OK");
            return;
        }

        await LoadAsync();
    }
}
