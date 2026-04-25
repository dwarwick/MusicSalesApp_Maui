using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Views;

public partial class HomePage : ContentPage
{
    private readonly HomeViewModel _viewModel;

    public HomePage(HomeViewModel viewModel, IPlaybackService playbackService, IAuthService authService)
    {
        _viewModel = viewModel;
        InitializeComponent();
        BindingContext = viewModel;
        NowPlayingBar.Initialize(playbackService, authService);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadCommand.ExecuteAsync(null);
    }
}
