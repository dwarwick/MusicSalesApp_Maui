using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Views;

public partial class PolicyPage : ContentPage
{
    public PolicyPage(PolicyViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;

        PolicyWebView.Navigated += OnWebViewNavigated;
    }

    private void OnWebViewNavigated(object? sender, WebNavigatedEventArgs e)
    {
        LoadingIndicator.IsRunning = false;
        LoadingIndicator.IsVisible = false;
    }
}
