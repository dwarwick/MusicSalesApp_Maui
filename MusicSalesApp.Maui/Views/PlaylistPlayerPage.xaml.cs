using System.Globalization;
using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Views;

public partial class PlaylistPlayerPage : ContentPage
{
    private readonly PlaylistPlayerViewModel _viewModel;

    public PlaylistPlayerPage(PlaylistPlayerViewModel viewModel, IPlaybackService playbackService, IAuthService authService)
    {
        _viewModel = viewModel;
        BindingContext = viewModel;

        // Reuse converters defined in SongPlayerPage.xaml.cs (same namespace)
        Resources.Add("DurationConverter", new DurationConverter());
        Resources.Add("SubBadgeBgConverter", new SubBadgeBgConverter());
        Resources.Add("SubBadgeTextConverter", new SubBadgeTextConverter());
        Resources.Add("LikeGlyphConverter", new LikeGlyphConverter());
        Resources.Add("DislikeGlyphConverter", new DislikeGlyphConverter());
        Resources.Add("LikeFillConverter", new LikeFillConverter());
        Resources.Add("DislikeFillConverter", new DislikeFillConverter());

        InitializeComponent();

        NowPlayingBar.Initialize(
            playbackService,
            authService,
            _viewModel.PlayVisibleQueueFromStartAsync,
            "Press Play to queue the tracks in this playlist.");
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.Cleanup();
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
