using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.Views;

public partial class TipButton : ContentView
{
    public static readonly BindableProperty SongIdProperty =
        BindableProperty.Create(nameof(SongId), typeof(int), typeof(TipButton), 0, propertyChanged: OnVisibilityInputsChanged);

    public static readonly BindableProperty SongTitleProperty =
        BindableProperty.Create(nameof(SongTitle), typeof(string), typeof(TipButton), string.Empty);

    public static readonly BindableProperty CreatorIdProperty =
        BindableProperty.Create(nameof(CreatorId), typeof(int?), typeof(TipButton), default(int?), propertyChanged: OnVisibilityInputsChanged);

    public static readonly BindableProperty CreatorUserIdProperty =
        BindableProperty.Create(nameof(CreatorUserId), typeof(int?), typeof(TipButton), default(int?), propertyChanged: OnVisibilityInputsChanged);

    private bool _subscribedToAuthChanges;
    private bool _subscribedToNetworkChanges;

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

    public int? CreatorId
    {
        get => (int?)GetValue(CreatorIdProperty);
        set => SetValue(CreatorIdProperty, value);
    }

    public int? CreatorUserId
    {
        get => (int?)GetValue(CreatorUserIdProperty);
        set => SetValue(CreatorUserIdProperty, value);
    }

    public ITipFlowHandler? TipHandler { get; set; }
    public IAuthService? AuthService { get; set; }
    public INetworkStatusService? NetworkStatusService { get; set; }

    public TipButton()
    {
        InitializeComponent();

        var tap = new TapGestureRecognizer();
        tap.Tapped += OnTapped;
        TipBtn.GestureRecognizers.Add(tap);

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        BindingContextChanged += (_, _) => UpdateVisibility();
    }

    private async void OnTapped(object? sender, TappedEventArgs e)
    {
        var handler = TipHandler ?? ResolveHandler();
        if (handler == null)
            return;

        await handler.ShowAsync(SongId, SongTitle, CreatorId, CreatorUserId);
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        SubscribeToAuthChanges();
        SubscribeToNetworkChanges();
        UpdateVisibility();
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        UnsubscribeFromAuthChanges();
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

    /// <summary>
    /// Tipping needs the server, so the button hides while offline and reappears on reconnect.
    /// </summary>
    private void OnNetworkStatusChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (NetworkStatusChange.AffectsConnectivity(e.PropertyName))
            MainThread.BeginInvokeOnMainThread(UpdateVisibility);
    }

    private void SubscribeToAuthChanges()
    {
        var authService = AuthService ?? ResolveAuthService();
        if (authService == null || _subscribedToAuthChanges)
            return;

        AuthService = authService;
        authService.AuthStateChanged += OnAuthStateChanged;
        _subscribedToAuthChanges = true;
    }

    private void UnsubscribeFromAuthChanges()
    {
        if (!_subscribedToAuthChanges || AuthService == null)
            return;

        AuthService.AuthStateChanged -= OnAuthStateChanged;
        _subscribedToAuthChanges = false;
    }

    private void OnAuthStateChanged()
    {
        MainThread.BeginInvokeOnMainThread(UpdateVisibility);
    }

    private void UpdateVisibility()
    {
        var handler = TipHandler ?? ResolveHandler();
        IsVisible = handler?.CanShowTipButton(CreatorId, CreatorUserId) == true;
    }

    private static void OnVisibilityInputsChanged(BindableObject bindable, object oldValue, object newValue)
    {
        ((TipButton)bindable).UpdateVisibility();
    }

    private static ITipFlowHandler? ResolveHandler()
    {
        var services = IPlatformApplication.Current?.Services;
        return services?.GetService(typeof(ITipFlowHandler)) as ITipFlowHandler;
    }

    private static IAuthService? ResolveAuthService()
    {
        var services = IPlatformApplication.Current?.Services;
        return services?.GetService(typeof(IAuthService)) as IAuthService;
    }

    private static INetworkStatusService? ResolveNetworkStatusService()
    {
        var services = IPlatformApplication.Current?.Services;
        return services?.GetService(typeof(INetworkStatusService)) as INetworkStatusService;
    }
}