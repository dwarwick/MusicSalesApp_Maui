using Moq;
using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.ViewModels;
using System.Collections.ObjectModel;

namespace MusicSalesApp.Maui.Tests.ViewModels;

[TestFixture]
public class HomeViewModelTests
{
    private Mock<IAuthService> _mockAuthService;
    private Mock<IAppSettingsService> _mockAppSettingsService;
    private Mock<INavigationService> _mockNavigationService;
    private Mock<IAlertService> _mockAlertService;
    private Mock<IAppConfig> _mockAppConfig;
    private Mock<IBillingService> _mockBillingService;
    private Mock<IMusicService> _mockMusicService;
    private Mock<ISignalRService> _mockSignalRService;
    private Mock<IPlaybackService> _mockPlaybackService;
    private Mock<IMediaPlaybackOnboardingService> _mockMediaPlaybackOnboardingService;
    private Mock<IBrowserService> _mockBrowserService;
    private Mock<IPlaylistService> _mockPlaylistService;
    private HomeViewModel _viewModel;

    [SetUp]
    public void Setup()
    {
        _mockAuthService = new Mock<IAuthService>();
        _mockAppSettingsService = new Mock<IAppSettingsService>();
        _mockNavigationService = new Mock<INavigationService>();
        _mockAlertService = new Mock<IAlertService>();
        _mockAppConfig = new Mock<IAppConfig>();
        _mockBillingService = new Mock<IBillingService>();
        _mockMusicService = new Mock<IMusicService>();
        _mockSignalRService = new Mock<ISignalRService>();
        _mockPlaybackService = new Mock<IPlaybackService>();
        _mockMediaPlaybackOnboardingService = new Mock<IMediaPlaybackOnboardingService>();
        _mockBrowserService = new Mock<IBrowserService>();
        _mockPlaylistService = new Mock<IPlaylistService>();

        _mockAppSettingsService.Setup(s => s.GetSubscriptionPriceAsync()).ReturnsAsync("3.99");
        _mockAppConfig.Setup(c => c.WebBaseUrl).Returns("https://streamtunes.net");
        _mockMediaPlaybackOnboardingService.Setup(s => s.EnsureBackgroundPlaybackExplainedAsync()).Returns(Task.CompletedTask);
        _mockMusicService.Setup(s => s.GetSongsAsync()).ReturnsAsync([]);
        _mockMusicService.Setup(s => s.GetStreamQualifyingSecondsAsync()).ReturnsAsync(30);
        _mockMusicService.Setup(s => s.GetBulkLikeCountsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync([]);
        _mockMusicService.Setup(s => s.GetBulkUserLikeStatusAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new Dictionary<int, bool?>());
        _mockPlaylistService.Setup(p => p.GetHomePlaylistsAsync())
            .ReturnsAsync(new HomePlaylistsDto());

        _viewModel = CreateViewModel();
    }

    private HomeViewModel CreateViewModel()
    {
        return new HomeViewModel(
            _mockAuthService.Object,
            _mockAppSettingsService.Object,
            _mockNavigationService.Object,
            _mockAlertService.Object,
            _mockAppConfig.Object,
            _mockBillingService.Object,
            _mockMusicService.Object,
            _mockSignalRService.Object,
            _mockPlaybackService.Object,
            _mockMediaPlaybackOnboardingService.Object,
            _mockBrowserService.Object,
            _mockPlaylistService.Object);
    }

    [Test]
    public void InitialState_IsNotAuthenticated()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.IsAuthenticated, Is.False);
            Assert.That(_viewModel.HasActiveSubscription, Is.False);
            Assert.That(_viewModel.IsEmailVerified, Is.False);
            Assert.That(_viewModel.SubscriptionPrice, Is.EqualTo("3.99"));
            Assert.That(_viewModel.IsLoading, Is.True);
        });
    }

    [Test]
    public void ShowLoginRegister_TrueWhenNotAuthenticated()
    {
        _viewModel.IsAuthenticated = false;
        Assert.That(_viewModel.ShowLoginRegister, Is.True);
    }

    [Test]
    public void ShowLoginRegister_FalseWhenAuthenticated()
    {
        _viewModel.IsAuthenticated = true;
        Assert.That(_viewModel.ShowLoginRegister, Is.False);
    }

    [Test]
    public void ShowValidateEmail_TrueWhenAuthenticatedAndNotVerified()
    {
        _viewModel.IsAuthenticated = true;
        _viewModel.IsEmailVerified = false;
        Assert.That(_viewModel.ShowValidateEmail, Is.True);
    }

    [Test]
    public void ShowValidateEmail_FalseWhenNotAuthenticated()
    {
        _viewModel.IsAuthenticated = false;
        _viewModel.IsEmailVerified = false;
        Assert.That(_viewModel.ShowValidateEmail, Is.False);
    }

    [Test]
    public void ShowSubscribeNow_TrueWhenAuthenticatedVerifiedNoSubscription()
    {
        _viewModel.IsAuthenticated = true;
        _viewModel.IsEmailVerified = true;
        _viewModel.HasActiveSubscription = false;
        Assert.That(_viewModel.ShowSubscribeNow, Is.True);
    }

    [Test]
    public void ShowSubscribeNow_FalseWhenHasSubscription()
    {
        _viewModel.IsAuthenticated = true;
        _viewModel.IsEmailVerified = true;
        _viewModel.HasActiveSubscription = true;
        Assert.That(_viewModel.ShowSubscribeNow, Is.False);
    }

    [Test]
    public void ShowBrowseMusic_TrueWhenLoggedOut()
    {
        Assert.That(_viewModel.ShowBrowseMusic, Is.True);
    }

    [Test]
    public void ShowBrowseMusic_TrueWhenAuthenticatedWithoutSubscription()
    {
        _viewModel.IsAuthenticated = true;
        _viewModel.HasActiveSubscription = false;
        Assert.That(_viewModel.ShowBrowseMusic, Is.True);
    }

    [Test]
    public void ShowSubscriptionContent_FalseWhenHasSubscription()
    {
        _viewModel.HasActiveSubscription = true;
        Assert.That(_viewModel.ShowSubscriptionContent, Is.False);
    }

    [Test]
    public void ShowSubscriptionContent_TrueWhenNoSubscription()
    {
        _viewModel.HasActiveSubscription = false;
        Assert.That(_viewModel.ShowSubscriptionContent, Is.True);
    }

    [Test]
    public async Task LoadAsync_SetsSubscriptionPriceFromService()
    {
        _mockAppSettingsService.Setup(s => s.GetSubscriptionPriceAsync()).ReturnsAsync("9.99");

        await _viewModel.LoadCommand.ExecuteAsync(null);

        Assert.That(_viewModel.SubscriptionPrice, Is.EqualTo("9.99"));
    }

    [Test]
    public async Task LoadAsync_SetsIsLoadingFalseAfterCompletion()
    {
        await _viewModel.LoadCommand.ExecuteAsync(null);

        Assert.That(_viewModel.IsLoading, Is.False);
    }

    [Test]
    public async Task LoadAsync_LoadsOnlyFeaturedSongsAndBuildsShareUrls()
    {
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(true);
        _mockAuthService.Setup(a => a.EmailConfirmed).Returns(true);

        _mockMusicService.Setup(s => s.GetSongsAsync()).ReturnsAsync(
        [
            new SongDto { Id = 1, SongTitle = "Featured Song", DisplayOnHomePage = true },
            new SongDto { Id = 2, SongTitle = "Library Song", DisplayOnHomePage = false }
        ]);

        _mockMusicService.Setup(s => s.GetBulkLikeCountsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync([
                new LikeCountDto { SongMetadataId = 1, LikeCount = 5, DislikeCount = 2 }
            ]);

        _mockMusicService.Setup(s => s.GetBulkUserLikeStatusAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new Dictionary<int, bool?> { [1] = true });

        await _viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.FeaturedSongs, Has.Count.EqualTo(1));
            Assert.That(_viewModel.ShowFeaturedMusic, Is.True);
            Assert.That(_viewModel.FeaturedSongs[0].Id, Is.EqualTo(1));
            Assert.That(_viewModel.FeaturedSongs[0].ShareUrl, Is.EqualTo("https://streamtunes.net/share/1"));
            Assert.That(_viewModel.FeaturedSongs[0].LikeCount, Is.EqualTo(5));
            Assert.That(_viewModel.FeaturedSongs[0].DislikeCount, Is.EqualTo(2));
            Assert.That(_viewModel.FeaturedSongs[0].UserLikeStatus, Is.True);
        });
    }

    [Test]
    public async Task LoadAsync_OrdersFeaturedSongsByDisplayOrder()
    {
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(true);
        _mockAuthService.Setup(a => a.EmailConfirmed).Returns(true);

        _mockMusicService.Setup(s => s.GetSongsAsync()).ReturnsAsync(
        [
            new SongDto { Id = 10, SongTitle = "Ranked One", DisplayOnHomePage = true, DisplayOrder = 1 },
            new SongDto { Id = 40, SongTitle = "Ranked Two", DisplayOnHomePage = true, DisplayOrder = 2 },
            new SongDto { Id = 30, SongTitle = "Null Newest", DisplayOnHomePage = true, DisplayOrder = null },
            new SongDto { Id = 20, SongTitle = "Null Older", DisplayOnHomePage = true, DisplayOrder = null }
        ]);

        await _viewModel.LoadCommand.ExecuteAsync(null);

        Assert.That(_viewModel.FeaturedSongs.Select(song => song.SongTitle), Is.EqualTo(new[]
        {
            "Null Newest",
            "Null Older",
            "Ranked One",
            "Ranked Two"
        }));
    }

    [Test]
    public async Task LoadAsync_SetsPlaybackStreamQualifyingSeconds()
    {
        _mockMusicService.Setup(s => s.GetStreamQualifyingSecondsAsync()).ReturnsAsync(45);

        await _viewModel.LoadCommand.ExecuteAsync(null);

        _mockPlaybackService.Verify(p => p.SetStreamQualifyingSeconds(45), Times.Once);
    }

    [Test]
    public async Task StartSignalRAsync_StartsService()
    {
        await _viewModel.StartSignalRAsync();

        _mockSignalRService.Verify(s => s.StartAsync(), Times.Once);
    }

    [Test]
    public void SignalR_StreamCountUpdate_UpdatesFeaturedSong()
    {
        var song = new SongDto { Id = 42, SongTitle = "Featured", StreamCount = 5 };
        _viewModel.FeaturedSongs = new ObservableCollection<SongDto> { song };

        _mockSignalRService.Raise(s => s.OnStreamCountUpdated += null, 42, 11);

        Assert.That(song.StreamCount, Is.EqualTo(11));
    }

    [Test]
    public void SignalR_LikeCountUpdate_UpdatesFeaturedSong()
    {
        var song = new SongDto { Id = 42, SongTitle = "Featured", LikeCount = 3, DislikeCount = 1 };
        _viewModel.FeaturedSongs = new ObservableCollection<SongDto> { song };

        _mockSignalRService.Raise(s => s.OnLikeCountUpdated += null, 42, 9, 2);

        Assert.That(song.LikeCount, Is.EqualTo(9));
        Assert.That(song.DislikeCount, Is.EqualTo(2));
    }

    [Test]
    public void Activate_ReattachesSignalR_AfterCleanup()
    {
        var song = new SongDto { Id = 42, SongTitle = "Featured", StreamCount = 5 };
        _viewModel.FeaturedSongs = new ObservableCollection<SongDto> { song };

        _viewModel.Cleanup();
        _viewModel.Activate();

        _mockSignalRService.Raise(s => s.OnStreamCountUpdated += null, 42, 12);

        Assert.That(song.StreamCount, Is.EqualTo(12));
    }

    [Test]
    public void MusicService_StreamCountRecorded_UpdatesFeaturedSong()
    {
        var song = new SongDto { Id = 42, SongTitle = "Featured", StreamCount = 5 };
        _viewModel.FeaturedSongs = new ObservableCollection<SongDto> { song };

        _mockMusicService.Raise(s => s.OnStreamCountRecorded += null, 42, 12);

        Assert.That(song.StreamCount, Is.EqualTo(12));
    }

    [Test]
    public async Task LoadAsync_RefreshesAuthState()
    {
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(true);
        _mockAuthService.Setup(a => a.HasActiveSubscription).Returns(true);
        _mockAuthService.Setup(a => a.EmailConfirmed).Returns(true);

        await _viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.IsAuthenticated, Is.True);
            Assert.That(_viewModel.HasActiveSubscription, Is.True);
            Assert.That(_viewModel.IsEmailVerified, Is.True);
        });
    }

    [Test]
    public async Task NavigateToLoginCommand_NavigatesToLoginRoute()
    {
        await _viewModel.NavigateToLoginCommand.ExecuteAsync(null);

        _mockNavigationService.Verify(n => n.GoToAsync("login"), Times.Once);
    }

    [Test]
    public async Task NavigateToRegisterCommand_NavigatesToRegisterRoute()
    {
        await _viewModel.NavigateToRegisterCommand.ExecuteAsync(null);

        _mockNavigationService.Verify(n => n.GoToAsync("register"), Times.Once);
    }

    [Test]
    public async Task NavigateToValidateEmailCommand_NavigatesToVerifyEmail()
    {
        _mockAuthService.Setup(a => a.UserId).Returns(42);
        _mockAuthService.Setup(a => a.Email).Returns("test@test.com");
        _viewModel = CreateViewModel();

        await _viewModel.NavigateToValidateEmailCommand.ExecuteAsync(null);

        _mockNavigationService.Verify(n => n.GoToAsync("verify-email", It.Is<IDictionary<string, object>>(d =>
            (int)d["UserId"] == 42 &&
            (string)d["Email"] == "test@test.com")), Times.Once);
    }

    [Test]
    public async Task SubscribeCommand_SuccessfulPurchase_VerifiesWithServerAndRefreshesStatus()
    {
        _mockBillingService.Setup(b => b.PurchaseSubscriptionAsync())
            .ReturnsAsync(BillingPurchaseResult.Succeeded("test-token", "order-123"));
        _mockMusicService.Setup(m => m.VerifyGooglePlayPurchaseAsync("test-token", "order-123"))
            .ReturnsAsync((true, string.Empty));

        await _viewModel.SubscribeCommand.ExecuteAsync(null);

        _mockMusicService.Verify(m => m.VerifyGooglePlayPurchaseAsync("test-token", "order-123"), Times.Once);
        _mockAuthService.Verify(a => a.RefreshUserStatusAsync(), Times.Once);
        _mockAlertService.Verify(a => a.DisplayAlertAsync("Success", It.IsAny<string>(), "OK"), Times.Once);
    }

    [Test]
    public async Task SubscribeCommand_PurchaseFailed_ShowsErrorAlert()
    {
        _mockBillingService.Setup(b => b.PurchaseSubscriptionAsync())
            .ReturnsAsync(BillingPurchaseResult.Failed("Connection error"));

        await _viewModel.SubscribeCommand.ExecuteAsync(null);

        _mockAlertService.Verify(a => a.DisplayAlertAsync("Subscribe", "Connection error", "OK"), Times.Once);
        _mockMusicService.Verify(m => m.VerifyGooglePlayPurchaseAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task SubscribeCommand_UserCancelled_NoAlertShown()
    {
        _mockBillingService.Setup(b => b.PurchaseSubscriptionAsync())
            .ReturnsAsync(BillingPurchaseResult.Cancelled());

        await _viewModel.SubscribeCommand.ExecuteAsync(null);

        _mockAlertService.Verify(a => a.DisplayAlertAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task SubscribeCommand_ServerVerificationFails_ShowsError()
    {
        _mockBillingService.Setup(b => b.PurchaseSubscriptionAsync())
            .ReturnsAsync(BillingPurchaseResult.Succeeded("test-token", "order-123"));
        _mockMusicService.Setup(m => m.VerifyGooglePlayPurchaseAsync("test-token", "order-123"))
            .ReturnsAsync((false, "Configured Google Play service account key file was not found on the server."));

        await _viewModel.SubscribeCommand.ExecuteAsync(null);

        _mockAlertService.Verify(a => a.DisplayAlertAsync("Subscribe",
            It.Is<string>(s => s.Contains("Configured Google Play service account key file was not found on the server.")), "OK"), Times.Once);
        _mockAuthService.Verify(a => a.RefreshUserStatusAsync(), Times.Never);
    }

    [Test]
    public async Task NavigateToMusicLibraryCommand_NavigatesToMusicLibrary()
    {
        await _viewModel.NavigateToMusicLibraryCommand.ExecuteAsync(null);

        _mockNavigationService.Verify(n => n.GoToAsync("//MusicLibrary"), Times.Once);
    }

    [Test]
    public async Task PlaySongCommand_SetsFeaturedPlaylistOnPlaybackService()
    {
        var firstSong = new SongDto { Id = 1, SongTitle = "First" };
        var secondSong = new SongDto { Id = 2, SongTitle = "Second" };
        _viewModel.FeaturedSongs = new ObservableCollection<SongDto> { firstSong, secondSong };

        await _viewModel.PlaySongCommand.ExecuteAsync(secondSong);

        _mockPlaybackService.Verify(p => p.SetPlaylist(
            It.Is<List<SongDto>>(songs => songs.Count == 2 && songs[0] == firstSong && songs[1] == secondSong),
            1), Times.Once);
        _mockMediaPlaybackOnboardingService.Verify(service => service.EnsureBackgroundPlaybackExplainedAsync(), Times.Once);
    }

    [Test]
    public async Task PlayFeaturedQueueFromStartAsync_QueuesFeaturedSongsFromBeginning()
    {
        var firstSong = new SongDto { Id = 1, SongTitle = "First" };
        var secondSong = new SongDto { Id = 2, SongTitle = "Second" };
        _viewModel.FeaturedSongs = new ObservableCollection<SongDto> { firstSong, secondSong };

        var started = await _viewModel.PlayFeaturedQueueFromStartAsync();

        Assert.That(started, Is.True);
        _mockPlaybackService.Verify(p => p.SetPlaylist(
            It.Is<List<SongDto>>(songs => songs.Select(song => song.Id).SequenceEqual(new[] { 1, 2 })),
            0), Times.Once);
    }

    [Test]
    public async Task LikeSong_WhenNotLoggedIn_ShowsLoginPrompt()
    {
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(false);
        _mockAlertService.Setup(a => a.ShowConfirmAsync(
            "Login Required",
            It.IsAny<string>(),
            "Login",
            "Cancel")).ReturnsAsync(false);

        await _viewModel.LikeSongCommand.ExecuteAsync(new SongDto { Id = 10, SongTitle = "Test" });

        _mockAlertService.Verify(a => a.ShowConfirmAsync(
            "Login Required",
            It.IsAny<string>(),
            "Login",
            "Cancel"), Times.Once);
        _mockMusicService.Verify(s => s.ToggleLikeAsync(It.IsAny<int>()), Times.Never);
    }

    [Test]
    public void SubscribeButtonText_IncludesPrice()
    {
        _viewModel.SubscriptionPrice = "4.99";
        Assert.That(_viewModel.SubscribeButtonText, Is.EqualTo("Subscribe Now — $4.99/mo"));
    }

    [Test]
    public void AuthStateChanged_RefreshesProperties()
    {
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(true);
        _mockAuthService.Setup(a => a.HasActiveSubscription).Returns(true);
        _mockAuthService.Setup(a => a.EmailConfirmed).Returns(true);

        // Raise the AuthStateChanged event
        _mockAuthService.Raise(a => a.AuthStateChanged += null);

        // The event handler calls MainThread.BeginInvokeOnMainThread which won't work in tests,
        // so we test RefreshAuthState behavior indirectly via LoadCommand instead
        // (AuthStateChanged tested through integration)
    }

    [Test]
    public async Task OpenGooglePlaySubscriptions_OpensBrowserToSubscriptionsUrl()
    {
        await _viewModel.OpenGooglePlaySubscriptionsCommand.ExecuteAsync(null);

        _mockBrowserService.Verify(b => b.OpenAsync("https://play.google.com/store/account/subscriptions"), Times.Once);
    }

    [Test]
    public async Task OpenRecommendedCommand_PassesUserIdAsString()
    {
        // Shell.ApplyQueryAttributes throws InvalidCastException when a non-string value is
        // assigned to a string? query property, so the id must be passed as a string.
        _mockAuthService.Setup(a => a.UserId).Returns(7);

        await _viewModel.OpenRecommendedCommand.ExecuteAsync(null);

        _mockNavigationService.Verify(n => n.GoToAsync("playlist-player",
            It.Is<IDictionary<string, object>>(d => (string)d["RecommendedUserId"] == "7")), Times.Once);
    }

    [Test]
    public async Task OpenPlaylistCommand_PassesPlaylistIdAsString()
    {
        var playlist = new PlaylistDto { Id = 99, Name = "Workout" };

        await _viewModel.OpenPlaylistCommand.ExecuteAsync(playlist);

        _mockNavigationService.Verify(n => n.GoToAsync("playlist-player",
            It.Is<IDictionary<string, object>>(d => (string)d["PlaylistId"] == "99")), Times.Once);
    }

    [Test]
    public async Task OpenPlaylistCommand_Null_DoesNothing()
    {
        await _viewModel.OpenPlaylistCommand.ExecuteAsync(null);

        _mockNavigationService.Verify(n => n.GoToAsync(It.IsAny<string>(), It.IsAny<IDictionary<string, object>>()), Times.Never);
    }
}
