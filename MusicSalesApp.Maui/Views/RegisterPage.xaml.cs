using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Views;

public partial class RegisterPage : ContentPage, IQueryAttributable
{
    private readonly RegisterViewModel _viewModel;

    public RegisterPage(RegisterViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    private void OnTogglePasswordVisibility(object? sender, EventArgs e)
        => PasswordEntry.IsPassword = !PasswordEntry.IsPassword;

    private void OnToggleConfirmPasswordVisibility(object? sender, EventArgs e)
        => ConfirmPasswordEntry.IsPassword = !ConfirmPasswordEntry.IsPassword;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
        => _viewModel.ApplyQueryAttributes(query);
}
