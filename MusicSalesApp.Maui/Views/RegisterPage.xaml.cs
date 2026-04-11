using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Views;

public partial class RegisterPage : ContentPage
{
    public RegisterPage(RegisterViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    private void OnTogglePasswordVisibility(object? sender, EventArgs e)
        => PasswordEntry.IsPassword = !PasswordEntry.IsPassword;

    private void OnToggleConfirmPasswordVisibility(object? sender, EventArgs e)
        => ConfirmPasswordEntry.IsPassword = !ConfirmPasswordEntry.IsPassword;
}
