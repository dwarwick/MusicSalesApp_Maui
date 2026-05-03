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
        NowPlayingBar.Initialize(
            playbackService,
            authService,
            _viewModel.PlayFeaturedQueueFromStartAsync,
            "Press Play to queue the featured songs shown on Home.");
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadCommand.ExecuteAsync(null);
    }

    protected override bool OnBackButtonPressed()
    {
        if (NowPlayingBar.CollapseDrawerIfExpanded())
        {
            return true;
        }

        return base.OnBackButtonPressed();
    }
}
