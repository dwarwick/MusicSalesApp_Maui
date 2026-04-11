using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Views;

public partial class ForgotPasswordPage : ContentPage
{
    public ForgotPasswordPage(ForgotPasswordViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
