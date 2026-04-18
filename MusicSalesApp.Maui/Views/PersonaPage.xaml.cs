using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Views;

public partial class PersonaPage : ContentPage
{
    public PersonaPage(PersonaViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
