using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Views;

public partial class MyPlaylistsPage : ContentPage
{
    private readonly MyPlaylistsViewModel _viewModel;

    public MyPlaylistsPage(MyPlaylistsViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.Activate();
        await _viewModel.LoadCommand.ExecuteAsync(null);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // The ViewModel is transient but subscribes to the singleton network-status service, so it
        // has to unsubscribe or every navigation here leaks one.
        _viewModel.Cleanup();
    }
}
