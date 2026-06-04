using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Views;

public partial class LoginPage : ContentPage, IQueryAttributable
{
    private readonly LoginViewModel _viewModel;

    public LoginPage(LoginViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    private void OnTogglePasswordVisibility(object? sender, EventArgs e)
    {
        PasswordEntry.IsPassword = !PasswordEntry.IsPassword;
        PasswordEntry.Focus();
    }

    private void OnPasswordEntryUnfocused(object? sender, FocusEventArgs e)
        => PasswordEntry.IsPassword = true;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
        => _viewModel.ApplyQueryAttributes(query);
}
