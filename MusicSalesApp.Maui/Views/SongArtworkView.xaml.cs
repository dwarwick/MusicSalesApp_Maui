using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.Views;

public partial class SongArtworkView : ContentView
{
    public static readonly BindableProperty AlbumArtUrlProperty =
        BindableProperty.Create(
            nameof(AlbumArtUrl),
            typeof(string),
            typeof(SongArtworkView),
            default(string),
            propertyChanged: OnAlbumArtUrlChanged);

    public static readonly BindableProperty HasAlbumArtProperty =
        BindableProperty.Create(nameof(HasAlbumArt), typeof(bool), typeof(SongArtworkView), false);

    public static readonly BindableProperty HasFallbackArtworkProperty =
        BindableProperty.Create(nameof(HasFallbackArtwork), typeof(bool), typeof(SongArtworkView), true);

    /// <summary>
    /// Artwork to display: either a remote URL or a local file path. Call sites bind
    /// <c>SongDto.AlbumArtDisplaySource</c>, which prefers the locally cached copy when one exists.
    /// Null or empty shows the built-in placeholder.
    /// </summary>
    public string? AlbumArtUrl
    {
        get => (string?)GetValue(AlbumArtUrlProperty);
        set => SetValue(AlbumArtUrlProperty, value);
    }

    public bool HasAlbumArt
    {
        get => (bool)GetValue(HasAlbumArtProperty);
        private set => SetValue(HasAlbumArtProperty, value);
    }

    public bool HasFallbackArtwork
    {
        get => (bool)GetValue(HasFallbackArtworkProperty);
        private set => SetValue(HasFallbackArtworkProperty, value);
    }

    public SongArtworkView()
    {
        InitializeComponent();
        UpdateArtworkState();
    }

    private static void OnAlbumArtUrlChanged(BindableObject bindable, object oldValue, object newValue)
    {
        ((SongArtworkView)bindable).UpdateArtworkState();
    }

    private void UpdateArtworkState()
    {
        HasAlbumArt = SongArtworkFallbackState.HasAlbumArt(AlbumArtUrl);
        HasFallbackArtwork = !HasAlbumArt;
    }
}
