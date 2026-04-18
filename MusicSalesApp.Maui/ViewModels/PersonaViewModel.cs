using CommunityToolkit.Mvvm.ComponentModel;

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
    public partial string? PersonaImageUrl { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBio))]
    public partial string PersonaBio { get; set; }

    public bool HasImage => !string.IsNullOrEmpty(PersonaImageUrl);

    public bool HasBio => !string.IsNullOrEmpty(PersonaBio);
}
