using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.Views;

/// <summary>
/// Reusable "Add to playlist" icon button. Exposes <see cref="SongId"/> and
/// <see cref="SongTitle"/> bindable properties. On tap, resolves
/// <see cref="IAddToPlaylistHandler"/> from the MAUI service provider and
/// shows the shared add-to-playlist UX.
/// </summary>
public partial class AddToPlaylistButton : ContentView
{
    public static readonly BindableProperty SongIdProperty =
        BindableProperty.Create(nameof(SongId), typeof(int), typeof(AddToPlaylistButton), 0);

    public static readonly BindableProperty SongTitleProperty =
        BindableProperty.Create(nameof(SongTitle), typeof(string), typeof(AddToPlaylistButton), string.Empty);

    /// <summary>
    /// Host-supplied suppression, combined with the offline gate below. Callers set this instead of
    /// binding <see cref="VisualElement.IsVisible"/> directly, so exactly one place decides visibility.
    /// </summary>
    public static readonly BindableProperty SuppressedProperty =
        BindableProperty.Create(
            nameof(Suppressed), typeof(bool), typeof(AddToPlaylistButton), false,
            propertyChanged: (bindable, _, _) => ((AddToPlaylistButton)bindable).UpdateVisibility());

    public bool Suppressed
    {
        get => (bool)GetValue(SuppressedProperty);
        set => SetValue(SuppressedProperty, value);
    }

    public int SongId
    {
        get => (int)GetValue(SongIdProperty);
        set => SetValue(SongIdProperty, value);
    }

    public string SongTitle
    {
        get => (string)GetValue(SongTitleProperty);
        set => SetValue(SongTitleProperty, value);
    }

    /// <summary>
    /// Optional override so tests and callers can supply a handler directly
    /// instead of resolving one from the MAUI service provider.
    /// </summary>
    public IAddToPlaylistHandler? AddHandler { get; set; }

    /// <summary>
    /// Optional override so tests can supply network state directly instead of resolving it from the
    /// MAUI service provider.
    /// </summary>
    public INetworkStatusService? NetworkStatusService { get; set; }

    private bool _subscribedToNetworkChanges;

    public AddToPlaylistButton()
    {
        InitializeComponent();
        var tap = new TapGestureRecognizer();
        tap.Tapped += OnTapped;
        AddBtn.GestureRecognizers.Add(tap);

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        SubscribeToNetworkChanges();
        UpdateVisibility();
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        UnsubscribeFromNetworkChanges();
    }

    private void SubscribeToNetworkChanges()
    {
        var networkStatusService = NetworkStatusService ?? ResolveNetworkStatusService();
        if (networkStatusService == null || _subscribedToNetworkChanges)
            return;

        NetworkStatusService = networkStatusService;
        networkStatusService.PropertyChanged += OnNetworkStatusChanged;
        _subscribedToNetworkChanges = true;
    }

    private void UnsubscribeFromNetworkChanges()
    {
        if (!_subscribedToNetworkChanges || NetworkStatusService == null)
            return;

        NetworkStatusService.PropertyChanged -= OnNetworkStatusChanged;
        _subscribedToNetworkChanges = false;
    }

    private void OnNetworkStatusChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (NetworkStatusChange.AffectsConnectivity(e.PropertyName))
            MainThread.BeginInvokeOnMainThread(UpdateVisibility);
    }

    /// <summary>
    /// Adding to a playlist writes to the server, so the button hides while there is no network rather
    /// than failing on tap.
    /// </summary>
    internal void UpdateVisibility()
    {
        var networkStatusService = NetworkStatusService ?? ResolveNetworkStatusService();
        IsVisible = !Suppressed && networkStatusService?.HasNoNetworkAccess != true;
    }

    private async void OnTapped(object? sender, TappedEventArgs e)
    {
        await InvokeHandlerAsync();
    }

    internal async Task InvokeHandlerAsync()
    {
        var handler = AddHandler ?? ResolveHandler();
        if (handler == null) return;
        await handler.ShowAsync(SongId, SongTitle);
    }

    private static IAddToPlaylistHandler? ResolveHandler()
    {
        var services = IPlatformApplication.Current?.Services;
        return services?.GetService(typeof(IAddToPlaylistHandler)) as IAddToPlaylistHandler;
    }

    private static INetworkStatusService? ResolveNetworkStatusService()
    {
        var services = IPlatformApplication.Current?.Services;
        return services?.GetService(typeof(INetworkStatusService)) as INetworkStatusService;
    }
}
