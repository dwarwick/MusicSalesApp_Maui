using System.Windows.Input;

namespace MusicSalesApp.Maui.Views;

public partial class SongCardView : ContentView
{
    public static readonly BindableProperty ShowFacebookShareButtonProperty =
        BindableProperty.Create(nameof(ShowFacebookShareButton), typeof(bool), typeof(SongCardView), true);

    public static readonly BindableProperty ShowShareButtonProperty =
        BindableProperty.Create(nameof(ShowShareButton), typeof(bool), typeof(SongCardView), true);

    public static readonly BindableProperty ShowAddToPlaylistButtonProperty =
        BindableProperty.Create(
            nameof(ShowAddToPlaylistButton), typeof(bool), typeof(SongCardView), true,
            propertyChanged: (bindable, _, _) =>
                ((SongCardView)bindable).OnPropertyChanged(nameof(HideAddToPlaylistButton)));

    public static readonly BindableProperty OpenSongCommandProperty =
        BindableProperty.Create(nameof(OpenSongCommand), typeof(ICommand), typeof(SongCardView));

    public static readonly BindableProperty NavigateToArtistCommandProperty =
        BindableProperty.Create(nameof(NavigateToArtistCommand), typeof(ICommand), typeof(SongCardView));

    public static readonly BindableProperty NavigateToGenreCommandProperty =
        BindableProperty.Create(nameof(NavigateToGenreCommand), typeof(ICommand), typeof(SongCardView));

    public static readonly BindableProperty LikeSongCommandProperty =
        BindableProperty.Create(nameof(LikeSongCommand), typeof(ICommand), typeof(SongCardView));

    public static readonly BindableProperty DislikeSongCommandProperty =
        BindableProperty.Create(nameof(DislikeSongCommand), typeof(ICommand), typeof(SongCardView));

    public static readonly BindableProperty ReportSongCommandProperty =
        BindableProperty.Create(nameof(ReportSongCommand), typeof(ICommand), typeof(SongCardView));

    /// <summary>
    /// Set false to hide the report flag. Pages bind this to their ViewModel's CanUseServerActions so
    /// the control disappears offline instead of failing on tap.
    /// </summary>
    public static readonly BindableProperty ShowReportButtonProperty =
        BindableProperty.Create(nameof(ShowReportButton), typeof(bool), typeof(SongCardView), true);

    public bool ShowReportButton
    {
        get => (bool)GetValue(ShowReportButtonProperty);
        set => SetValue(ShowReportButtonProperty, value);
    }

    /// <summary>
    /// Inverse of <see cref="ShowAddToPlaylistButton"/>, for AddToPlaylistButton.Suppressed - that
    /// control owns its own IsVisible so the host and the offline gate cannot fight over it.
    /// </summary>
    public bool HideAddToPlaylistButton => !ShowAddToPlaylistButton;

    public static readonly BindableProperty PlaySongCommandProperty =
        BindableProperty.Create(nameof(PlaySongCommand), typeof(ICommand), typeof(SongCardView));

    public ICommand? OpenSongCommand
    {
        get => (ICommand?)GetValue(OpenSongCommandProperty);
        set => SetValue(OpenSongCommandProperty, value);
    }

    public bool ShowFacebookShareButton
    {
        get => (bool)GetValue(ShowFacebookShareButtonProperty);
        set => SetValue(ShowFacebookShareButtonProperty, value);
    }

    public bool ShowShareButton
    {
        get => (bool)GetValue(ShowShareButtonProperty);
        set => SetValue(ShowShareButtonProperty, value);
    }

    public bool ShowAddToPlaylistButton
    {
        get => (bool)GetValue(ShowAddToPlaylistButtonProperty);
        set => SetValue(ShowAddToPlaylistButtonProperty, value);
    }

    public ICommand? NavigateToArtistCommand
    {
        get => (ICommand?)GetValue(NavigateToArtistCommandProperty);
        set => SetValue(NavigateToArtistCommandProperty, value);
    }

    public ICommand? NavigateToGenreCommand
    {
        get => (ICommand?)GetValue(NavigateToGenreCommandProperty);
        set => SetValue(NavigateToGenreCommandProperty, value);
    }

    public ICommand? LikeSongCommand
    {
        get => (ICommand?)GetValue(LikeSongCommandProperty);
        set => SetValue(LikeSongCommandProperty, value);
    }

    public ICommand? DislikeSongCommand
    {
        get => (ICommand?)GetValue(DislikeSongCommandProperty);
        set => SetValue(DislikeSongCommandProperty, value);
    }

    public ICommand? ReportSongCommand
    {
        get => (ICommand?)GetValue(ReportSongCommandProperty);
        set => SetValue(ReportSongCommandProperty, value);
    }

    public ICommand? PlaySongCommand
    {
        get => (ICommand?)GetValue(PlaySongCommandProperty);
        set => SetValue(PlaySongCommandProperty, value);
    }

    public SongCardView()
    {
        InitializeComponent();
    }
}