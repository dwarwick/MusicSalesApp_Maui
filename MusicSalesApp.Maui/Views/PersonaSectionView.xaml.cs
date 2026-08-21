using System.Windows.Input;
using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.Views;

/// <summary>
/// The artist block shown inline on the song and playlist players.
/// </summary>
/// <remarks>
/// Takes its values as bindable properties rather than binding to a song, because the two
/// players hold the current song under different names - <c>Song</c> on one, <c>CurrentSong</c>
/// on the other - and a control that knew about either would only be reusable on one of them.
/// </remarks>
public partial class PersonaSectionView : ContentView
{
    public static readonly BindableProperty PersonaNameProperty =
        BindableProperty.Create(
            nameof(PersonaName), typeof(string), typeof(PersonaSectionView), default(string),
            propertyChanged: OnContentChanged);

    public static readonly BindableProperty BioProperty =
        BindableProperty.Create(
            nameof(Bio), typeof(string), typeof(PersonaSectionView), default(string),
            propertyChanged: OnContentChanged);

    public static readonly BindableProperty ImageSourceProperty =
        BindableProperty.Create(
            nameof(ImageSource), typeof(ImageSource), typeof(PersonaSectionView), default(ImageSource));

    public static readonly BindableProperty WebsiteUrlProperty =
        BindableProperty.Create(
            nameof(WebsiteUrl), typeof(string), typeof(PersonaSectionView), default(string));

    /// <summary>Where tapping the artist's name goes. The players supply their artist route.</summary>
    public static readonly BindableProperty NavigateCommandProperty =
        BindableProperty.Create(
            nameof(NavigateCommand), typeof(ICommand), typeof(PersonaSectionView), default(ICommand));

    public PersonaSectionView()
    {
        InitializeComponent();
    }

    public string? PersonaName
    {
        get => (string?)GetValue(PersonaNameProperty);
        set => SetValue(PersonaNameProperty, value);
    }

    public string? Bio
    {
        get => (string?)GetValue(BioProperty);
        set => SetValue(BioProperty, value);
    }

    public ImageSource? ImageSource
    {
        get => (ImageSource?)GetValue(ImageSourceProperty);
        set => SetValue(ImageSourceProperty, value);
    }

    public ICommand? NavigateCommand
    {
        get => (ICommand?)GetValue(NavigateCommandProperty);
        set => SetValue(NavigateCommandProperty, value);
    }

    /// <summary>
    /// The artist's own site, exactly as they typed it.
    /// </summary>
    /// <remarks>
    /// Not necessarily a URL. The server stores this with nothing but a Trim - no scheme is
    /// added and nothing is validated - so it may well arrive as "example.com", and it is not
    /// safe to hand to the launcher unexamined. See <see cref="WebsiteUri"/>.
    /// </remarks>
    public string? WebsiteUrl
    {
        get => (string?)GetValue(WebsiteUrlProperty);
        set => SetValue(WebsiteUrlProperty, value);
    }

    private async void OnWebsiteTapped(object? sender, TappedEventArgs e)
    {
        if (!WebsiteUri.TryParse(WebsiteUrl, out var uri) || uri is null)
        {
            return;
        }

        try
        {
            await Launcher.Default.OpenAsync(uri);
        }
        catch
        {
            // No browser, or the launcher refused. Nothing useful to say - the rest of the page
            // is unaffected.
        }
    }

    /// <summary>
    /// Whether there is an artist to show at all.
    /// </summary>
    /// <remarks>
    /// Keyed on the NAME rather than on the bio: a song with an artist but no bio still shows the
    /// block, because the name is a link to everything else they have made. A song with no artist
    /// has nothing to say and the block collapses entirely rather than leaving a gap.
    /// </remarks>
    public bool HasContent => !string.IsNullOrWhiteSpace(PersonaName);

    private static void OnContentChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is PersonaSectionView view)
        {
            view.OnPropertyChanged(nameof(HasContent));
        }
    }
}
