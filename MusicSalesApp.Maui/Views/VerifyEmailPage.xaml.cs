using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Views;

public partial class VerifyEmailPage : ContentPage
{
    public VerifyEmailPage(VerifyEmailViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is VerifyEmailViewModel vm)
            await vm.OnAppearingAsync();
    }
}
