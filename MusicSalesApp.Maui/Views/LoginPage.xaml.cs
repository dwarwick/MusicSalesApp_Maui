using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Views;

public partial class LoginPage : ContentPage, IQueryAttributable
{
    private readonly LoginViewModel _viewModel;
    private CancellationTokenSource? _appearanceCancellation;

    public LoginPage(LoginViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _appearanceCancellation?.Cancel();
        _appearanceCancellation?.Dispose();
        _appearanceCancellation = new CancellationTokenSource();
        _ = InitializeViewModelAsync(_appearanceCancellation.Token);
    }

    protected override void OnDisappearing()
    {
        _appearanceCancellation?.Cancel();
        _appearanceCancellation?.Dispose();
        _appearanceCancellation = null;
        base.OnDisappearing();
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

    private async Task InitializeViewModelAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _viewModel.InitializeAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }
}
