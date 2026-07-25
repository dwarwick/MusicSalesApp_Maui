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
        try
        {
            await _viewModel.RefreshCacheUsageAsync();
        }
        catch (Exception ex)
        {
            // Never let a cache-usage read failure crash the async void handler; the page
            // stays usable and the label falls back via IsCacheUsageLoading.
            System.Diagnostics.Debug.WriteLine($"Failed to refresh cache usage: {ex}");
        }
    }
}
