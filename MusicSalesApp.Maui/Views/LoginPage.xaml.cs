using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Views;

public partial class LoginPage : ContentPage
{
    public LoginPage(LoginViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    private void OnTogglePasswordVisibility(object? sender, EventArgs e)
        => PasswordEntry.IsPassword = !PasswordEntry.IsPassword;

    private void OnPasswordEntryUnfocused(object? sender, FocusEventArgs e)
        => PasswordEntry.IsPassword = true;
}
