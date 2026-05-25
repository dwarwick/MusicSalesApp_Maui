using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Views;

public partial class ContactUsPage : ContentPage
{
    public ContactUsPage(ContactUsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}