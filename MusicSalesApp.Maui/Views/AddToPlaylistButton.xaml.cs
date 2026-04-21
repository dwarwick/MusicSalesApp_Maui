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

    public AddToPlaylistButton()
    {
        InitializeComponent();
        var tap = new TapGestureRecognizer();
        tap.Tapped += OnTapped;
        AddBtn.GestureRecognizers.Add(tap);
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
}
