using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Views;

public partial class ConfigPage : ContentPage
{
    private readonly ConfigViewModel _viewModel;

    public ConfigPage(ConfigViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.Refresh();
        await _viewModel.RefreshCacheUsageAsync();
    }
}
