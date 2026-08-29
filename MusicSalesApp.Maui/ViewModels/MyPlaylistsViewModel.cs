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

    /// <summary>
    /// The personal "Recommended For You" list, or null when the server omitted it.
    /// </summary>
    /// <remarks>
    /// Kept out of <see cref="Playlists"/> for the same reason as <see cref="TopStreamedPlaylists"/>:
    /// it is generated per request, has no <c>Playlists</c> row, and must not offer rename or delete.
    /// The server omits it entirely when it has no playable songs, so null is the normal empty case
    /// rather than a failure.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowRecommended))]
    [NotifyPropertyChangedFor(nameof(ShowGeneratedPlaylists))]
    public partial PlaylistDto? RecommendedPlaylist { get; set; }

    public bool ShowRecommended => RecommendedPlaylist != null;

    /// <summary>
    /// The five global "most streamed" playlists, shown above the user's own.
    /// </summary>
    /// <remarks>
    /// Kept out of <see cref="Playlists"/> on purpose. Those rows are addressed by id and carry
    /// rename/delete affordances; these have no id and must never offer either.
    /// </remarks>
    public ObservableCollection<PlaylistDto> TopStreamedPlaylists { get; } = [];

    public bool ShowTopStreamed => TopStreamedPlaylists.Count > 0;

    /// <summary>
    /// Whether anything server-generated precedes the user's own list. Gates the "My Playlists"
    /// heading, which only earns its place when there is a preceding section to distinguish it from.
    /// </summary>
    public bool ShowGeneratedPlaylists => ShowRecommended || ShowTopStreamed;

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
    /// <summary>
    /// True when the user has none of their OWN playlists. The most-streamed ones are always there
    /// and deliberately do not count, or the "create one to get started" prompt would never appear.
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
        "To create playlists, you need an active subscription. Tap a playlist above to listen.";

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

            await LoadGeneratedPlaylistsAsync();
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
            OnPropertyChanged(nameof(ShowTopStreamed));
            OnPropertyChanged(nameof(ShowGeneratedPlaylists));
        }
    }

    /// <summary>
    /// Loads the two server-generated sections: Recommended, and the most-streamed tiles.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Mirrors <c>HomeViewModel.LoadHomePlaylistsAsync</c>. Recommended is personal and is synthesised
    /// only inside the <c>/home</c> payload - <c>GET api/mobile/playlists</c> returns real rows, so it
    /// can never appear there. The most-streamed tiles ride along in that same response, which is why
    /// signed in this is one request rather than two.
    /// </para>
    /// <para>
    /// Signed out or unverified, <c>/home</c> answers 401 and there is no Recommended list to show;
    /// the most-streamed tiles are fetched from the anonymous endpoint instead so that section still
    /// renders. The flyout hides this page unless signed in, so that branch covers an expired session
    /// rather than a genuinely anonymous visitor.
    /// </para>
    /// </remarks>
    private async Task LoadGeneratedPlaylistsAsync()
    {
        if (_authService.IsLoggedIn && _authService.EmailConfirmed)
        {
            var home = await _playlistService.GetHomePlaylistsAsync();
            RecommendedPlaylist = home?.Recommended;
            // Server order is the display order - Day, Week, Month, Year, All Time.
            ReplaceTopStreamed(home?.TopStreamedOrEmpty);
            return;
        }

        RecommendedPlaylist = null;
        ReplaceTopStreamed(await _playlistService.GetTopStreamedPlaylistsAsync());
    }

    private void ReplaceTopStreamed(IEnumerable<PlaylistDto>? playlists)
    {
        TopStreamedPlaylists.Clear();
        foreach (var playlist in playlists ?? [])
            TopStreamedPlaylists.Add(playlist);
    }

    /// <summary>
    /// Opens any playlist row, the user's own or a most-streamed one.
    /// </summary>
    /// <remarks>
    /// Routed through <see cref="PlaylistNavigationTarget"/> rather than navigating on
    /// <c>playlist.Id</c>: the most-streamed playlists have no row and all report id 0, so opening
    /// them by id would send every one of them to the same wrong page.
    /// </remarks>
    [RelayCommand]
    public Task OpenPlaylistAsync(PlaylistDto? playlist)
    {
        var target = PlaylistNavigationTarget.For(playlist, _authService.UserId);
        return target is null
            ? Task.CompletedTask
            : _navigationService.GoToAsync(target.Value.Route, target.Value.Query);
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
