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
    private Mock<IMediaPlaybackOnboardingService> _mockMediaPlaybackOnboardingService;
    private Mock<ISignalRService> _mockSignalRService;
    private Mock<IAppConfig> _mockAppConfig;
    private Mock<IBillingService> _mockBillingService;
    private SongPlayerViewModel _viewModel;

    [SetUp]
    public void Setup()
    {
        _mockMusicService = new Mock<IMusicService>();
        _mockAlertService = new Mock<IAlertService>();
        _mockAuthService = new Mock<IAuthService>();
        _mockNavigationService = new Mock<INavigationService>();
        _mockPlaybackService = new Mock<IPlaybackService>();
        _mockMediaPlaybackOnboardingService = new Mock<IMediaPlaybackOnboardingService>();
        _mockSignalRService = new Mock<ISignalRService>();
        _mockAppConfig = new Mock<IAppConfig>();
        _mockBillingService = new Mock<IBillingService>();
        _mockAppConfig.Setup(c => c.WebBaseUrl).Returns("https://streamtunes.net");
        _mockAppConfig.Setup(c => c.ApiBaseUrl).Returns("https://streamtunes.net");
        _mockMediaPlaybackOnboardingService.Setup(s => s.EnsureBackgroundPlaybackExplainedAsync()).Returns(Task.CompletedTask);

        _viewModel = new SongPlayerViewModel(
            _mockMusicService.Object, _mockAlertService.Object,
            _mockAuthService.Object, _mockNavigationService.Object,
            _mockPlaybackService.Object, _mockMediaPlaybackOnboardingService.Object, _mockSignalRService.Object,
            _mockAppConfig.Object, _mockBillingService.Object);
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
    public void Song_WhenSetToCurrentlyPlayingSong_DoesNotRestartOrPausePlayback()
    {
        var song = new SongDto { Id = 1, SongTitle = "Test Song" };
        _mockPlaybackService.SetupGet(p => p.CurrentSong).Returns(song);
        _mockPlaybackService.SetupGet(p => p.IsPlaying).Returns(true);

        _viewModel.Song = song;

        _mockPlaybackService.Verify(p => p.PlaySong(It.IsAny<SongDto>()), Times.Never);
        _mockMediaPlaybackOnboardingService.Verify(s => s.EnsureBackgroundPlaybackExplainedAsync(), Times.Never);
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
    public async Task PlaySongCommand_SetsSingleSongPlaylist()
    {
        var song = new SongDto { Id = 1, SongTitle = "Test" };
        _viewModel.Song = song;

        await _viewModel.PlaySongCommand.ExecuteAsync(null);

        _mockPlaybackService.Verify(p => p.SetPlaylist(
            It.Is<List<SongDto>>(songs => songs.Count == 1 && songs[0].Id == 1),
            0), Times.Once);
        _mockMediaPlaybackOnboardingService.Verify(s => s.EnsureBackgroundPlaybackExplainedAsync(), Times.Exactly(2));
    }

    [Test]
    public async Task PlaySongCommand_WhenDisplayedSongMatchesCurrentPlayback_TogglesPlayPause()
    {
        var song = new SongDto { Id = 1, SongTitle = "Test" };
        _viewModel.Song = song;
        _mockPlaybackService.Invocations.Clear();
        _mockMediaPlaybackOnboardingService.Invocations.Clear();
        _mockPlaybackService.SetupGet(p => p.CurrentSong).Returns(song);
        _mockPlaybackService.SetupGet(p => p.PreviewLimitReached).Returns(false);

        await _viewModel.PlaySongCommand.ExecuteAsync(null);

        _mockPlaybackService.Verify(p => p.TogglePlayPause(), Times.Once);
        _mockPlaybackService.Verify(p => p.SetPlaylist(It.IsAny<List<SongDto>>(), It.IsAny<int>()), Times.Never);
        _mockMediaPlaybackOnboardingService.Verify(s => s.EnsureBackgroundPlaybackExplainedAsync(), Times.Never);
    }

    [Test]
    public async Task PlayDisplayedSongQueueAsync_QueuesDisplayedSong()
    {
        var song = new SongDto { Id = 9, SongTitle = "Queued Song" };
        _viewModel.Song = song;

        var started = await _viewModel.PlayDisplayedSongQueueAsync();

        Assert.That(started, Is.True);
        _mockPlaybackService.Verify(p => p.SetPlaylist(
            It.Is<List<SongDto>>(songs => songs.Count == 1 && songs[0].Id == 9),
            0), Times.Once);
    }

    [Test]
    public void PlaySong_NullSong_DoesNotCallService()
    {
        _viewModel.PlaySongCommand.Execute(null);

        _mockPlaybackService.Verify(p => p.PlaySong(It.IsAny<SongDto>()), Times.Never);
        _mockPlaybackService.Verify(p => p.SetPlaylist(It.IsAny<List<SongDto>>(), It.IsAny<int>()), Times.Never);
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

    [Test]
    public void SignalR_StreamCountUpdate_UpdatesSongDto()
    {
        var song = new SongDto { Id = 42, SongTitle = "Test", StreamCount = 5 };
        _viewModel.Song = song;

        _mockSignalRService.Raise(s => s.OnStreamCountUpdated += null, 42, 12);

        Assert.That(song.StreamCount, Is.EqualTo(12));
    }

    [Test]
    public void MusicService_StreamCountRecorded_UpdatesSongDto()
    {
        var song = new SongDto { Id = 42, SongTitle = "Test", StreamCount = 5 };
        _viewModel.Song = song;

        _mockMusicService.Raise(s => s.OnStreamCountRecorded += null, 42, 12);

        Assert.That(song.StreamCount, Is.EqualTo(12));
    }

    [Test]
    public async Task StartSignalRAsync_StartsService()
    {
        await _viewModel.StartSignalRAsync();

        _mockSignalRService.Verify(s => s.StartAsync(), Times.Once);
    }

    [Test]
    public void Activate_ReattachesSignalR_AfterCleanup()
    {
        var song = new SongDto { Id = 42, SongTitle = "Test", LikeCount = 5, DislikeCount = 2 };
        _viewModel.Song = song;

        _viewModel.Cleanup();
        _viewModel.Activate();

        _mockSignalRService.Raise(s => s.OnLikeCountUpdated += null, 42, 8, 4);

        Assert.That(song.LikeCount, Is.EqualTo(8));
        Assert.That(song.DislikeCount, Is.EqualTo(4));
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

    [Test]
    public async Task ShowSubscribeCtaRequested_WhenRaised_ShowsPreviewPromptAndHandlesVerificationFailure()
    {
        _mockAlertService.Setup(a => a.ShowConfirmAsync("Preview Limit", It.IsAny<string>(), "Subscribe Now", "Not Now"))
            .ReturnsAsync(true);
        _mockBillingService.Setup(b => b.PurchaseSubscriptionAsync())
            .ReturnsAsync(BillingPurchaseResult.Succeeded("test-token", "order-123"));
        _mockMusicService.Setup(s => s.VerifySubscriptionPurchaseAsync(It.Is<BillingPurchaseVerificationRequest>(r =>
                r.Provider == BillingProviders.GooglePlay &&
                r.PurchaseToken == "test-token" &&
                r.OrderId == "order-123")))
            .ReturnsAsync((false, "Configured Google Play service account key file was not found on the server."));

        _mockPlaybackService.Raise(p => p.ShowSubscribeCtaRequested += null);
        await Task.Delay(50);

        _mockAlertService.Verify(a => a.DisplayAlertAsync("Subscribe",
            It.Is<string>(s => s.Contains("Configured Google Play service account key file was not found on the server.")),
            "OK"), Times.Once);
    }

    [Test]
    public void Cleanup_UnsubscribesFromPlaybackSubscribeCta()
    {
        _viewModel.Cleanup();

        _mockPlaybackService.Raise(p => p.ShowSubscribeCtaRequested += null);

        _mockAlertService.Verify(a => a.ShowConfirmAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    // --- ViewBio command ---

    [Test]
    public async Task ViewBio_WithArtistName_NavigatesToPersonaPage()
    {
        _viewModel.Song = new SongDto
        {
            Id = 1, SongTitle = "Test", ArtistName = "Artist",
            PersonaImageUrl = "https://img.test/pic.jpg", PersonaBio = "A great artist."
        };

        await _viewModel.ViewBioCommand.ExecuteAsync(null);

        _mockNavigationService.Verify(n => n.GoToAsync("persona",
            It.Is<IDictionary<string, object>>(d =>
                d["PersonaName"].ToString() == "Artist" &&
                d["PersonaImageUrl"].ToString() == "https://img.test/pic.jpg" &&
                d["PersonaBio"].ToString() == "A great artist.")), Times.Once);
    }

    [Test]
    public async Task ViewBio_NullSong_DoesNotNavigate()
    {
        await _viewModel.ViewBioCommand.ExecuteAsync(null);

        _mockNavigationService.Verify(n => n.GoToAsync("persona",
            It.IsAny<IDictionary<string, object>>()), Times.Never);
    }

    [Test]
    public async Task ViewBio_EmptyArtistName_DoesNotNavigate()
    {
        _viewModel.Song = new SongDto
        {
            Id = 1, SongTitle = "Test", ArtistName = "", PersonaBio = "Some bio"
        };

        await _viewModel.ViewBioCommand.ExecuteAsync(null);

        _mockNavigationService.Verify(n => n.GoToAsync("persona",
            It.IsAny<IDictionary<string, object>>()), Times.Never);
    }

    [Test]
    public async Task ViewBio_NullPersonaImageUrl_PassesEmptyString()
    {
        _viewModel.Song = new SongDto
        {
            Id = 1, SongTitle = "Test", ArtistName = "Artist",
            PersonaImageUrl = null, PersonaBio = "Bio text"
        };

        await _viewModel.ViewBioCommand.ExecuteAsync(null);

        _mockNavigationService.Verify(n => n.GoToAsync("persona",
            It.Is<IDictionary<string, object>>(d =>
                d["PersonaImageUrl"].ToString() == string.Empty)), Times.Once);
    }

    [Test]
    public async Task ViewBio_NullBio_PassesEmptyString()
    {
        _viewModel.Song = new SongDto
        {
            Id = 1, SongTitle = "Test", ArtistName = "Artist",
            PersonaBio = null
        };

        await _viewModel.ViewBioCommand.ExecuteAsync(null);

        _mockNavigationService.Verify(n => n.GoToAsync("persona",
            It.Is<IDictionary<string, object>>(d =>
                d["PersonaBio"].ToString() == string.Empty)), Times.Once);
    }

    // --- Report Song Tests ---

    [Test]
    public async Task ReportSong_WhenNotLoggedIn_ShowsLoginPrompt()
    {
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(false);
        _viewModel.Song = new SongDto { Id = 1, SongTitle = "Test" };

        await _viewModel.ReportSongCommand.ExecuteAsync(null);

        _mockAlertService.Verify(a => a.ShowConfirmAsync(
            "Login Required", It.IsAny<string>(), "Login", "Cancel"), Times.Once);
        _mockMusicService.Verify(s => s.ReportSongAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task ReportSong_WhenNotValidatedUser_ShowsNotAuthorized()
    {
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(true);
        _mockAuthService.Setup(a => a.EmailConfirmed).Returns(true);
        _mockAuthService.Setup(a => a.Roles).Returns(new List<string> { "NonValidatedUser" });
        _viewModel.Song = new SongDto { Id = 1, SongTitle = "Test" };

        await _viewModel.ReportSongCommand.ExecuteAsync(null);

        _mockAlertService.Verify(a => a.DisplayAlertAsync(
            "Not Authorized", It.IsAny<string>(), "OK"), Times.Once);
        _mockMusicService.Verify(s => s.ReportSongAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task ReportSong_WhenValidatedUser_CallsService()
    {
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(true);
        _mockAuthService.Setup(a => a.EmailConfirmed).Returns(true);
        _mockAuthService.Setup(a => a.Roles).Returns(new List<string> { "User" });
        _mockAlertService.Setup(a => a.ShowActionSheetAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string[]>()))
            .ReturnsAsync("Terms of Use Violation");
        _mockMusicService.Setup(s => s.ReportSongAsync(42, "Terms of Use Violation")).ReturnsAsync(true);
        _viewModel.Song = new SongDto { Id = 42, SongTitle = "Test" };

        await _viewModel.ReportSongCommand.ExecuteAsync(null);

        _mockMusicService.Verify(s => s.ReportSongAsync(42, "Terms of Use Violation"), Times.Once);
        _mockAlertService.Verify(a => a.DisplayAlertAsync("Report Submitted", It.IsAny<string>(), "OK"), Times.Once);
    }

    [Test]
    public async Task ReportSong_NullSong_DoesNothing()
    {
        _viewModel.Song = null;

        await _viewModel.ReportSongCommand.ExecuteAsync(null);

        _mockMusicService.Verify(s => s.ReportSongAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task ReportSong_WhenAlreadyReported_ShowsAlreadyReportedAlert()
    {
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(true);
        _mockAuthService.Setup(a => a.EmailConfirmed).Returns(true);
        _mockAuthService.Setup(a => a.Roles).Returns(new List<string> { "User" });
        _mockAlertService.Setup(a => a.ShowActionSheetAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string[]>()))
            .ReturnsAsync("Copyright Violation");
        _mockMusicService.Setup(s => s.ReportSongAsync(42, "Copyright Violation"))
            .ThrowsAsync(new InvalidOperationException("You have already reported this song."));
        _viewModel.Song = new SongDto { Id = 42, SongTitle = "Test" };

        await _viewModel.ReportSongCommand.ExecuteAsync(null);

        _mockAlertService.Verify(a => a.DisplayAlertAsync("Already Reported", It.IsAny<string>(), "OK"), Times.Once);
    }

    // --- Navigate to Genre/Artist ---

    [Test]
    public async Task NavigateToGenre_NavigatesToPlaylistPlayer()
    {
        await _viewModel.NavigateToGenreCommand.ExecuteAsync("Rock");

        _mockNavigationService.Verify(n =>
            n.GoToAsync("playlist-player", It.Is<IDictionary<string, object>>(d =>
                d.ContainsKey("GenreName") && (string)d["GenreName"] == "Rock")),
            Times.Once);
        _mockNavigationService.Verify(n =>
            n.GoToReplacingCurrentAsync(It.IsAny<string>(), It.IsAny<IDictionary<string, object>>()),
            Times.Never);
    }

    [Test]
    public async Task NavigateToArtist_NavigatesToPlaylistPlayer()
    {
        await _viewModel.NavigateToArtistCommand.ExecuteAsync("Band A");

        _mockNavigationService.Verify(n =>
            n.GoToAsync("playlist-player", It.Is<IDictionary<string, object>>(d =>
                d.ContainsKey("ArtistName") && (string)d["ArtistName"] == "Band A")),
            Times.Once);
        _mockNavigationService.Verify(n =>
            n.GoToReplacingCurrentAsync(It.IsAny<string>(), It.IsAny<IDictionary<string, object>>()),
            Times.Never);
    }

    [Test]
    public async Task NavigateToGenre_NullGenre_DoesNothing()
    {
        await _viewModel.NavigateToGenreCommand.ExecuteAsync(null);

        _mockNavigationService.Verify(n =>
            n.GoToAsync(It.IsAny<string>(), It.IsAny<IDictionary<string, object>>()),
            Times.Never);
    }

    [Test]
    public async Task NavigateToArtist_EmptyString_DoesNothing()
    {
        await _viewModel.NavigateToArtistCommand.ExecuteAsync(string.Empty);

        _mockNavigationService.Verify(n =>
            n.GoToAsync(It.IsAny<string>(), It.IsAny<IDictionary<string, object>>()),
            Times.Never);
    }

    // --- Refresh ---

    [Test]
    public async Task Refresh_WhenNoSong_SetsIsRefreshingFalse()
    {
        await _viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.That(_viewModel.IsRefreshing, Is.False);
    }

    [Test]
    public async Task Refresh_UpdatesSubscriptionStatus()
    {
        _viewModel.Song = new SongDto { Id = 1, SongTitle = "Test" };
        _mockAuthService.Setup(a => a.HasActiveSubscription).Returns(true);
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(false);

        await _viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.That(_viewModel.HasActiveSubscription, Is.True);
        Assert.That(_viewModel.IsRefreshing, Is.False);
    }

    [Test]
    public async Task Refresh_WhenLoggedIn_ReloadsLikeData()
    {
        var song = new SongDto { Id = 5, SongTitle = "Test" };
        _viewModel.Song = song;
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(true);
        _mockMusicService.Setup(s => s.GetBulkUserLikeStatusAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new Dictionary<int, bool?> { { 5, true } });
        _mockMusicService.Setup(s => s.GetBulkLikeCountsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync([new LikeCountDto { SongMetadataId = 5, LikeCount = 10, DislikeCount = 2 }]);

        await _viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.That(song.UserLikeStatus, Is.True);
        Assert.That(song.LikeCount, Is.EqualTo(10));
        Assert.That(song.DislikeCount, Is.EqualTo(2));
        Assert.That(_viewModel.IsRefreshing, Is.False);
    }
}
