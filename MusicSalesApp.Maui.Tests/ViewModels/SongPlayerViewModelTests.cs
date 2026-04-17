using Moq;
using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Tests.ViewModels;

[TestFixture]
public class SongPlayerViewModelTests
{
    private Mock<IMusicService> _mockMusicService;
    private Mock<IAlertService> _mockAlertService;
    private Mock<IAuthService> _mockAuthService;
    private Mock<INavigationService> _mockNavigationService;
    private Mock<IPlaybackService> _mockPlaybackService;
    private Mock<ISignalRService> _mockSignalRService;
    private Mock<IAppConfig> _mockAppConfig;
    private SongPlayerViewModel _viewModel;

    [SetUp]
    public void Setup()
    {
        _mockMusicService = new Mock<IMusicService>();
        _mockAlertService = new Mock<IAlertService>();
        _mockAuthService = new Mock<IAuthService>();
        _mockNavigationService = new Mock<INavigationService>();
        _mockPlaybackService = new Mock<IPlaybackService>();
        _mockSignalRService = new Mock<ISignalRService>();
        _mockAppConfig = new Mock<IAppConfig>();
        _mockAppConfig.Setup(c => c.WebBaseUrl).Returns("https://streamtunes.net");
        _mockAppConfig.Setup(c => c.ApiBaseUrl).Returns("https://streamtunes.net");

        _viewModel = new SongPlayerViewModel(
            _mockMusicService.Object, _mockAlertService.Object,
            _mockAuthService.Object, _mockNavigationService.Object,
            _mockPlaybackService.Object, _mockSignalRService.Object,
            _mockAppConfig.Object);
    }

    // --- Song property ---

    [Test]
    public void Song_WhenSet_StartsPlayback()
    {
        var song = new SongDto { Id = 1, SongTitle = "Test Song" };

        _viewModel.Song = song;

        // PlaySong is called from LoadSongDetailsAsync
        _mockPlaybackService.Verify(p => p.PlaySong(song), Times.Once);
    }

    [Test]
    public void Song_WhenSet_UpdatesShareUrl()
    {
        var song = new SongDto { Id = 42, SongTitle = "My Song" };

        _viewModel.Song = song;

        Assert.That(_viewModel.ShareUrl, Is.EqualTo("https://streamtunes.net/share/42"));
    }

    // --- PlaySong command ---

    [Test]
    public void PlaySong_DelegatesToPlaybackService()
    {
        var song = new SongDto { Id = 1, SongTitle = "Test" };
        _viewModel.Song = song;

        _viewModel.PlaySongCommand.Execute(null);

        // Called twice: once from Song setter, once from command
        _mockPlaybackService.Verify(p => p.PlaySong(song), Times.Exactly(2));
    }

    [Test]
    public void PlaySong_NullSong_DoesNotCallService()
    {
        _viewModel.PlaySongCommand.Execute(null);

        _mockPlaybackService.Verify(p => p.PlaySong(It.IsAny<SongDto>()), Times.Never);
    }

    // --- Like/Dislike ---

    [Test]
    public async Task LikeSong_WhenNotLoggedIn_ShowsLoginPrompt()
    {
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(false);
        _viewModel.Song = new SongDto { Id = 1, SongTitle = "Test" };

        await _viewModel.LikeSongCommand.ExecuteAsync(null);

        _mockAlertService.Verify(a => a.ShowConfirmAsync(
            "Login Required", It.IsAny<string>(), "Login", "Cancel"), Times.Once);
        _mockMusicService.Verify(s => s.ToggleLikeAsync(It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task LikeSong_WhenEmailNotConfirmed_ShowsVerifyAlert()
    {
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(true);
        _mockAuthService.Setup(a => a.EmailConfirmed).Returns(false);
        _viewModel.Song = new SongDto { Id = 1, SongTitle = "Test" };

        await _viewModel.LikeSongCommand.ExecuteAsync(null);

        _mockAlertService.Verify(a => a.DisplayAlertAsync(
            "Email Not Verified", It.IsAny<string>(), "OK"), Times.Once);
    }

    [Test]
    public async Task LikeSong_WhenAuthenticated_CallsToggleLike()
    {
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(true);
        _mockAuthService.Setup(a => a.EmailConfirmed).Returns(true);
        _mockMusicService.Setup(s => s.ToggleLikeAsync(42)).ReturnsAsync(new LikeToggleResult
        {
            IsLiked = true,
            LikeCount = 5,
            DislikeCount = 1
        });
        _viewModel.Song = new SongDto { Id = 42, SongTitle = "Test" };

        await _viewModel.LikeSongCommand.ExecuteAsync(null);

        _mockMusicService.Verify(s => s.ToggleLikeAsync(42), Times.Once);
        Assert.That(_viewModel.Song.UserLikeStatus, Is.True);
        Assert.That(_viewModel.Song.LikeCount, Is.EqualTo(5));
    }

    [Test]
    public async Task DislikeSong_WhenAuthenticated_CallsToggleDislike()
    {
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(true);
        _mockAuthService.Setup(a => a.EmailConfirmed).Returns(true);
        _mockMusicService.Setup(s => s.ToggleDislikeAsync(42)).ReturnsAsync(new LikeToggleResult
        {
            IsDisliked = true,
            LikeCount = 3,
            DislikeCount = 7
        });
        _viewModel.Song = new SongDto { Id = 42, SongTitle = "Test" };

        await _viewModel.DislikeSongCommand.ExecuteAsync(null);

        _mockMusicService.Verify(s => s.ToggleDislikeAsync(42), Times.Once);
        Assert.That(_viewModel.Song.UserLikeStatus, Is.False);
        Assert.That(_viewModel.Song.DislikeCount, Is.EqualTo(7));
    }

    [Test]
    public async Task LikeSong_NullSong_DoesNothing()
    {
        await _viewModel.LikeSongCommand.ExecuteAsync(null);

        _mockMusicService.Verify(s => s.ToggleLikeAsync(It.IsAny<int>()), Times.Never);
    }

    // --- Deep linking ---

    [Test]
    public async Task SongTitle_WhenSetWithNoSong_LoadsSongByTitle()
    {
        var song = new SongDto { Id = 5, SongTitle = "Deep Link Song" };
        _mockMusicService.Setup(s => s.GetSongByTitleAsync("Deep Link Song"))
            .ReturnsAsync(song);

        _viewModel.SongTitle = "Deep Link Song";

        // Give async operation time to complete
        await Task.Delay(100);

        _mockMusicService.Verify(s => s.GetSongByTitleAsync("Deep Link Song"), Times.Once);
    }

    [Test]
    public void SongTitle_WhenSongAlreadySet_DoesNotLoadByTitle()
    {
        _viewModel.Song = new SongDto { Id = 1, SongTitle = "Existing" };

        _viewModel.SongTitle = "Other Song";

        _mockMusicService.Verify(s => s.GetSongByTitleAsync(It.IsAny<string>()), Times.Never);
    }

    // --- SignalR like count updates ---

    [Test]
    public void SignalR_LikeCountUpdate_UpdatesSongDto()
    {
        var song = new SongDto { Id = 42, SongTitle = "Test", LikeCount = 5, DislikeCount = 2 };
        _viewModel.Song = song;

        _mockSignalRService.Raise(s => s.OnLikeCountUpdated += null, 42, 10, 3);

        Assert.That(song.LikeCount, Is.EqualTo(10));
        Assert.That(song.DislikeCount, Is.EqualTo(3));
    }

    [Test]
    public void SignalR_LikeCountUpdate_IgnoresDifferentSong()
    {
        var song = new SongDto { Id = 42, SongTitle = "Test", LikeCount = 5, DislikeCount = 2 };
        _viewModel.Song = song;

        _mockSignalRService.Raise(s => s.OnLikeCountUpdated += null, 999, 20, 10);

        Assert.That(song.LikeCount, Is.EqualTo(5));
        Assert.That(song.DislikeCount, Is.EqualTo(2));
    }

    // --- Subscription status ---

    [Test]
    public void Song_WhenSet_LoadsSubscriptionStatus()
    {
        _mockAuthService.Setup(a => a.HasActiveSubscription).Returns(true);
        var song = new SongDto { Id = 1, SongTitle = "Test" };

        _viewModel.Song = song;

        Assert.That(_viewModel.HasActiveSubscription, Is.True);
    }

    [Test]
    public void PlaybackService_IsExposed()
    {
        Assert.That(_viewModel.PlaybackService, Is.SameAs(_mockPlaybackService.Object));
    }
}
