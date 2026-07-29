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
    private readonly INetworkStatusService? _networkStatusService;
    private bool _networkSubscriptionAttached;

    public MyPlaylistsViewModel(
        IPlaylistService playlistService,
        IAuthService authService,
        IAlertService alertService,
        INavigationService navigationService,
        INetworkStatusService? networkStatusService = null)
    {
        _playlistService = playlistService;
        _authService = authService;
        _alertService = alertService;
        _navigationService = navigationService;
        _networkStatusService = networkStatusService;

        Activate();
    }

    /// <summary>
    /// Subscribes to connectivity changes. Paired with <see cref="Cleanup"/> and guarded, because the
    /// page can be navigated away from and back to on the same ViewModel instance.
    /// </summary>
    public void Activate()
    {
        if (_networkSubscriptionAttached || _networkStatusService == null)
            return;

        _networkStatusService.PropertyChanged += HandleNetworkStatusChanged;
        _networkSubscriptionAttached = true;
    }

    private void HandleNetworkStatusChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!NetworkStatusChange.AffectsConnectivity(e.PropertyName))
            return;

        OnPropertyChanged(nameof(CanUseServerActions));
        OnPropertyChanged(nameof(EmptyStateTitle));
        OnPropertyChanged(nameof(EmptyStateDetail));

        if (!IsLoading)
            _ = LoadCommand.ExecuteAsync(null);
    }

    public void Cleanup()
    {
        if (!_networkSubscriptionAttached || _networkStatusService == null)
            return;

        _networkStatusService.PropertyChanged -= HandleNetworkStatusChanged;
        _networkSubscriptionAttached = false;
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
    /// False when the device has no network at all: creating and renaming playlists both need the
    /// server. Gated on <see cref="INetworkStatusService.HasNoNetworkAccess"/> rather than the
    /// pessimistic <see cref="INetworkStatusService.IsOffline"/>, so a constrained connection - where
    /// the server is still reachable - keeps the controls available.
    /// </summary>
    public bool CanUseServerActions => _networkStatusService?.HasNoNetworkAccess != true;

    /// <summary>
    /// Offline, an empty list means "we couldn't reach the server", not "you have no playlists" -
    /// showing the latter reads as if the user's playlists were deleted.
    /// </summary>
    public string EmptyStateTitle => CanUseServerActions
        ? "No playlists yet"
        : "You're offline";

    public string EmptyStateDetail => CanUseServerActions
        ? "Create a playlist to get started."
        : "Your playlists will be here when you reconnect.";

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
