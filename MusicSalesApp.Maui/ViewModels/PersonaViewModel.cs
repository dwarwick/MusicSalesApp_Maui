using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Maui.Controls;

namespace MusicSalesApp.Maui.ViewModels;

[QueryProperty(nameof(PersonaName), "PersonaName")]
[QueryProperty(nameof(PersonaImageUrl), "PersonaImageUrl")]
[QueryProperty(nameof(PersonaBio), "PersonaBio")]
public partial class PersonaViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImage))]
    public partial string PersonaName { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImage))]
    [NotifyPropertyChangedFor(nameof(PersonaImageSource))]
    public partial string? PersonaImageUrl { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBio))]
    public partial string PersonaBio { get; set; }

    public ImageSource? PersonaImageSource => CreatePersonaImageSource(PersonaImageUrl);

    public bool HasImage => PersonaImageSource is not null;

    public bool HasBio => !string.IsNullOrEmpty(PersonaBio);

    private static ImageSource? CreatePersonaImageSource(string? imageUrl)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            return null;
        }

        var trimmedImageUrl = imageUrl.Trim();

        if (Uri.TryCreate(trimmedImageUrl, UriKind.Absolute, out var uri))
        {
            return new UriImageSource
            {
                Uri = uri,
                CachingEnabled = true,
                CacheValidity = TimeSpan.FromHours(1)
            };
        }

        return ImageSource.FromFile(trimmedImageUrl);
    }
}
