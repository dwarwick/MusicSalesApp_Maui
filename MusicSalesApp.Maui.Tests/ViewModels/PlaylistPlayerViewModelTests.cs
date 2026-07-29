using Moq;
using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Tests.ViewModels;

[TestFixture]
public class PlaylistPlayerViewModelTests
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
    private Mock<IPlaylistService> _mockPlaylistService;
    private PlaylistPlayerViewModel _viewModel;

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
        _mockPlaylistService = new Mock<IPlaylistService>();
        _mockAppConfig.Setup(c => c.WebBaseUrl).Returns("https://streamtunes.net");
        _mockMediaPlaybackOnboardingService.Setup(s => s.EnsureBackgroundPlaybackExplainedAsync()).Returns(Task.CompletedTask);

        _viewModel = CreateViewModel();
    }

    private PlaylistPlayerViewModel CreateViewModel() => new(
        _mockMusicService.Object, _mockAlertService.Object,
        _mockAuthService.Object, _mockNavigationService.Object,
        _mockPlaybackService.Object, _mockMediaPlaybackOnboardingService.Object, _mockSignalRService.Object,
        _mockAppConfig.Object, _mockBillingService.Object,
        _mockPlaylistService.Object);

    private List<SongDto> CreateTestSongs() =>
    [
        new SongDto { Id = 1, SongTitle = "Rock Song 1", ArtistName = "Band A", Genre = "Rock", StreamUrl = "http://a.mp3" },
        new SongDto { Id = 2, SongTitle = "Rock Song 2", ArtistName = "Band B", Genre = "Rock", StreamUrl = "http://b.mp3" },
        new SongDto { Id = 3, SongTitle = "Pop Song", ArtistName = "Singer C", Genre = "Pop", StreamUrl = "http://c.mp3" },
        new SongDto { Id = 4, SongTitle = "Rock Song 3", ArtistName = "Band A", Genre = "Rock", StreamUrl = "http://d.mp3" },
    ];

    // --- Loading by Genre ---

    [Test]
    public async Task GenreName_WhenSet_LoadsFilteredSongs()
    {
        var songs = CreateTestSongs();
        _mockMusicService.Setup(s => s.GetSongsAsync()).ReturnsAsync(songs);
        _mockMusicService.Setup(s => s.GetBulkLikeCountsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new List<LikeCountDto>());

        _viewModel.GenreName = "Rock";
        await Task.Delay(100); // let async fire

        Assert.That(_viewModel.Songs, Has.Count.EqualTo(3));
        Assert.That(_viewModel.Songs.All(s => s.Genre == "Rock"), Is.True);
        Assert.That(_viewModel.PlaylistTitle, Is.EqualTo("Rock"));
    }

    [Test]
    public async Task GenreName_WhenSet_SetsPlaylistOnPlaybackService()
    {
        var songs = CreateTestSongs();
        _mockMusicService.Setup(s => s.GetSongsAsync()).ReturnsAsync(songs);
        _mockMusicService.Setup(s => s.GetBulkLikeCountsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new List<LikeCountDto>());

        _viewModel.GenreName = "Rock";
        await Task.Delay(100);

        _mockPlaybackService.Verify(p =>
            p.SetPlaylist(
                It.Is<List<SongDto>>(l => l.Count == 3),
                0,
                PlaybackQueueStartBehavior.PreserveCurrentSongIfPresent,
                "Genre Rock"), Times.Once);
    }

    [Test]
    public async Task GenreName_WhenOrderedQueueMatchesActivePlaylist_PreservesPlaybackState()
    {
        var songs = CreateTestSongs();
        var activeQueue = songs.Where(s => s.Genre == "Rock").ToList();
        _mockMusicService.Setup(s => s.GetSongsAsync()).ReturnsAsync(songs);
        _mockMusicService.Setup(s => s.GetBulkLikeCountsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new List<LikeCountDto>());
        _mockPlaybackService.SetupGet(p => p.Playlist).Returns(activeQueue);
        _mockPlaybackService.SetupGet(p => p.CurrentSong).Returns(activeQueue[1]);

        _viewModel.GenreName = "Rock";
        await Task.Delay(100);

        _mockPlaybackService.Verify(p => p.SetPlaylist(It.IsAny<List<SongDto>>(), It.IsAny<int>()), Times.Never);
        _mockPlaybackService.Verify(p => p.SetPlaylist(
            It.IsAny<List<SongDto>>(),
            It.IsAny<int>(),
            It.IsAny<PlaybackQueueStartBehavior>()), Times.Never);
        Assert.That(_viewModel.CurrentSong, Is.SameAs(_viewModel.Songs[1]));
        Assert.That(_viewModel.CurrentSong?.Id, Is.EqualTo(2));
    }

    [Test]
    public async Task ArtistName_WhenActiveQueueMatchesButCurrentSongIsOutsideArtistSongs_RestartsArtistQueue()
    {
        var songs = CreateTestSongs();
        var artistQueue = songs.Where(s => s.ArtistName == "Band A").ToList();
        var featuredSongFromAnotherArtist = new SongDto
        {
            Id = 99,
            SongTitle = "Featured Song",
            ArtistName = "Other Artist",
            DisplayOnHomePage = true
        };
        _mockMusicService.Setup(s => s.GetSongsAsync()).ReturnsAsync(songs);
        _mockMusicService.Setup(s => s.GetBulkLikeCountsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new List<LikeCountDto>());
        _mockPlaybackService.SetupGet(p => p.Playlist).Returns(artistQueue);
        _mockPlaybackService.SetupGet(p => p.CurrentSong).Returns(featuredSongFromAnotherArtist);

        _viewModel.ArtistName = "Band A";
        await Task.Delay(100);

        _mockPlaybackService.Verify(p =>
            p.SetPlaylist(
                It.Is<List<SongDto>>(l => l.Select(song => song.Id).SequenceEqual(new[] { 1, 4 })),
                0,
                PlaybackQueueStartBehavior.RestartAtRequestedIndex,
                "Artist Band A"), Times.Once);
        Assert.That(_viewModel.Songs.Select(song => song.Id), Is.EqualTo(new[] { 1, 4 }));
        Assert.That(_viewModel.CurrentSong, Is.Null);
    }

    [Test]
    public async Task GenreName_WhenOrderedQueueDiffers_ResetsPlaybackState()
    {
        var songs = CreateTestSongs();
        var reorderedQueue = new List<SongDto>
        {
            songs[1],
            songs[0],
            songs[3]
        };
        _mockMusicService.Setup(s => s.GetSongsAsync()).ReturnsAsync(songs);
        _mockMusicService.Setup(s => s.GetBulkLikeCountsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new List<LikeCountDto>());
        _mockPlaybackService.SetupGet(p => p.Playlist).Returns(reorderedQueue);

        _viewModel.GenreName = "Rock";
        await Task.Delay(100);

        _mockPlaybackService.Verify(p =>
            p.SetPlaylist(
                It.Is<List<SongDto>>(l => l.Select(song => song.Id).SequenceEqual(new[] { 1, 2, 4 })),
                0,
                PlaybackQueueStartBehavior.PreserveCurrentSongIfPresent,
                "Genre Rock"), Times.Once);
    }

    [Test]
    public async Task GenreName_WhenApiOrderIsRandom_LoadsSongsInIdOrder()
    {
        var songs = new List<SongDto>
        {
            new() { Id = 4, SongTitle = "Rock Song 3", ArtistName = "Band A", Genre = "Rock", StreamUrl = "http://d.mp3" },
            new() { Id = 1, SongTitle = "Rock Song 1", ArtistName = "Band A", Genre = "Rock", StreamUrl = "http://a.mp3" },
            new() { Id = 3, SongTitle = "Pop Song", ArtistName = "Singer C", Genre = "Pop", StreamUrl = "http://c.mp3" },
            new() { Id = 2, SongTitle = "Rock Song 2", ArtistName = "Band B", Genre = "Rock", StreamUrl = "http://b.mp3" }
        };

        _mockMusicService.Setup(s => s.GetSongsAsync()).ReturnsAsync(songs);
        _mockMusicService.Setup(s => s.GetBulkLikeCountsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new List<LikeCountDto>());

        _viewModel.GenreName = "Rock";
        await Task.Delay(100);

        Assert.That(_viewModel.Songs.Select(song => song.Id), Is.EqualTo(new[] { 1, 2, 4 }));
    }

    [Test]
    public async Task GenreName_NoMatches_ShowsError()
    {
        _mockMusicService.Setup(s => s.GetSongsAsync()).ReturnsAsync(CreateTestSongs());
        _mockMusicService.Setup(s => s.GetBulkLikeCountsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new List<LikeCountDto>());

        _viewModel.GenreName = "Jazz";
        await Task.Delay(100);

        Assert.That(_viewModel.ErrorMessage, Does.Contain("Jazz"));
        _mockPlaybackService.Verify(p =>
            p.SetPlaylist(It.IsAny<List<SongDto>>(), It.IsAny<int>()), Times.Never);
        _mockPlaybackService.Verify(p =>
            p.SetPlaylist(
                It.IsAny<List<SongDto>>(),
                It.IsAny<int>(),
                It.IsAny<PlaybackQueueStartBehavior>()), Times.Never);
    }

    [Test]
    public async Task GenreName_WhenSongsRequestFails_ShowsMusicServiceError()
    {
        _mockMusicService.Setup(s => s.GetSongsAsync()).ReturnsAsync([]);
        _mockMusicService.SetupGet(s => s.LastSongsError).Returns("Request to https://davidtest.dev/api/music/songs failed (500).");

        _viewModel.GenreName = "Rock";
        await Task.Delay(100);

        Assert.That(_viewModel.ErrorMessage, Does.Contain("https://davidtest.dev/api/music/songs"));
    }

    [Test]
    public async Task GenreName_CaseInsensitiveMatch()
    {
        _mockMusicService.Setup(s => s.GetSongsAsync()).ReturnsAsync(CreateTestSongs());
        _mockMusicService.Setup(s => s.GetBulkLikeCountsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new List<LikeCountDto>());

        _viewModel.GenreName = "rock"; // lowercase
        await Task.Delay(100);

        Assert.That(_viewModel.Songs, Has.Count.EqualTo(3));
    }

    // --- Loading by Artist ---

        [Test]
        public async Task OnShowSubscribeCta_WhenServerVerificationFails_ShowsSpecificErrorMessage()
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

            var method = typeof(PlaylistPlayerViewModel).GetMethod(
                "OnShowSubscribeCta",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            var task = (Task)method!.Invoke(_viewModel, null)!;
            await task;

            _mockAlertService.Verify(a => a.DisplayAlertAsync("Subscribe",
                It.Is<string>(s => s.Contains("Configured Google Play service account key file was not found on the server.")),
                "OK"), Times.Once);
        }

    [Test]
    public async Task ArtistName_WhenSet_LoadsFilteredSongs()
    {
        _mockMusicService.Setup(s => s.GetSongsAsync()).ReturnsAsync(CreateTestSongs());
        _mockMusicService.Setup(s => s.GetBulkLikeCountsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new List<LikeCountDto>());

        _viewModel.ArtistName = "Band A";
        await Task.Delay(100);

        Assert.That(_viewModel.Songs, Has.Count.EqualTo(2));
        Assert.That(_viewModel.Songs.All(s => s.ArtistName == "Band A"), Is.True);
        Assert.That(_viewModel.PlaylistTitle, Is.EqualTo("Band A"));
    }

    [Test]
    public async Task ArtistName_WhenSet_SetsPlaylistOnPlaybackService()
    {
        _mockMusicService.Setup(s => s.GetSongsAsync()).ReturnsAsync(CreateTestSongs());
        _mockMusicService.Setup(s => s.GetBulkLikeCountsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new List<LikeCountDto>());

        _viewModel.ArtistName = "Band A";
        await Task.Delay(100);

        _mockPlaybackService.Verify(p =>
            p.SetPlaylist(
                It.Is<List<SongDto>>(l => l.Count == 2),
                0,
                PlaybackQueueStartBehavior.PreserveCurrentSongIfPresent,
                "Artist Band A"), Times.Once);
    }

    [Test]
    public async Task ArtistName_CaseInsensitiveMatch()
    {
        _mockMusicService.Setup(s => s.GetSongsAsync()).ReturnsAsync(CreateTestSongs());
        _mockMusicService.Setup(s => s.GetBulkLikeCountsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new List<LikeCountDto>());

        _viewModel.ArtistName = "band a"; // lowercase
        await Task.Delay(100);

        Assert.That(_viewModel.Songs, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task PlaylistServiceLoad_PreservesAiDisclosureFlags()
    {
        SongDto? currentSong = null;
        _mockPlaybackService.SetupGet(s => s.CurrentSong).Returns(() => currentSong);
        _mockPlaybackService
            .Setup(s => s.SetPlaylist(
                It.IsAny<List<SongDto>>(),
                0,
                PlaybackQueueStartBehavior.PreserveCurrentSongIfPresent,
                "Playlist AI Mix"))
            .Callback<List<SongDto>, int, PlaybackQueueStartBehavior, string>((songs, _, _, _) => currentSong = songs.FirstOrDefault());
        _mockPlaylistService.Setup(s => s.GetPlaylistSongsAsync(7)).ReturnsAsync(new PlaylistSongsDto
        {
            PlaylistId = 7,
            PlaylistName = "AI Mix",
            IsSystemGenerated = false,
            Songs =
            [
                new PlaylistSongDto
                {
                    SongMetadataId = 21,
                    SongTitle = "AI Anthem",
                    ArtistName = "Synth Artist",
                    Genre = "Electronic",
                    StreamUrl = "https://example.com/anthem.mp3",
                    IsAiGenerated = true,
                    IsAiVocals = true,
                    IsAiLyrics = true
                }
            ]
        });
        _mockMusicService.Setup(s => s.GetBulkLikeCountsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new List<LikeCountDto>());

        _viewModel.PlaylistIdParam = "7";
        await Task.Delay(100);

        Assert.That(_viewModel.Songs, Has.Count.EqualTo(1));
        Assert.That(_viewModel.Songs[0].IsAiGenerated, Is.True);
        Assert.That(_viewModel.Songs[0].IsAiVocals, Is.True);
        Assert.That(_viewModel.Songs[0].IsAiLyrics, Is.True);
        Assert.That(_viewModel.CurrentSong?.IsAiGenerated, Is.True);
        Assert.That(_viewModel.CurrentSong?.IsAiVocals, Is.True);
        Assert.That(_viewModel.CurrentSong?.IsAiLyrics, Is.True);
    }

    [Test]
    public async Task PlaylistServiceLoad_PreservesStreamQualifyingSeconds()
    {
        SongDto? currentSong = null;
        _mockPlaybackService.SetupGet(s => s.CurrentSong).Returns(() => currentSong);
        _mockPlaybackService
            .Setup(s => s.SetPlaylist(
                It.IsAny<List<SongDto>>(),
                0,
                PlaybackQueueStartBehavior.PreserveCurrentSongIfPresent,
                "Playlist Threshold Mix"))
            .Callback<List<SongDto>, int, PlaybackQueueStartBehavior, string>((songs, _, _, _) => currentSong = songs.FirstOrDefault());
        _mockPlaylistService.Setup(s => s.GetPlaylistSongsAsync(7)).ReturnsAsync(new PlaylistSongsDto
        {
            PlaylistId = 7,
            PlaylistName = "Threshold Mix",
            IsSystemGenerated = false,
            Songs =
            [
                new PlaylistSongDto
                {
                    SongMetadataId = 21,
                    SongTitle = "Threshold Song",
                    ArtistName = "Synth Artist",
                    Genre = "Electronic",
                    StreamUrl = "https://example.com/anthem.mp3",
                    StreamQualifyingSeconds = 65
                }
            ]
        });
        _mockMusicService.Setup(s => s.GetBulkLikeCountsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new List<LikeCountDto>());

        _viewModel.PlaylistIdParam = "7";
        await Task.Delay(100);

        Assert.That(_viewModel.Songs, Has.Count.EqualTo(1));
        Assert.That(_viewModel.Songs[0].StreamQualifyingSeconds, Is.EqualTo(65));
        Assert.That(_viewModel.CurrentSong?.StreamQualifyingSeconds, Is.EqualTo(65));
    }

    // --- Share URL ---

    [Test]
    public async Task ShareUrl_UpdatesWhenCurrentSongChanges()
    {
        var songs = CreateTestSongs();
        _mockMusicService.Setup(s => s.GetSongsAsync()).ReturnsAsync(songs);
        _mockMusicService.Setup(s => s.GetBulkLikeCountsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new List<LikeCountDto>());
        // After SetPlaylist, CurrentSong returns the first Rock song
        _mockPlaybackService.Setup(p => p.CurrentSong).Returns(songs[0]);

        _viewModel.GenreName = "Rock";
        await Task.Delay(100);

        Assert.That(_viewModel.ShareUrl, Is.EqualTo("https://streamtunes.net/share/1"));
    }

    [Test]
    public void ShareUrl_WhenNoCurrentSong_ReturnsEmpty()
    {
        Assert.That(_viewModel.ShareUrl, Is.EqualTo(string.Empty));
    }

    // --- PlayTrack command ---

    [Test]
    public async Task PlayTrack_SetsPlaylistFromVisibleSongs()
    {
        var songs = CreateTestSongs();
        _mockMusicService.Setup(s => s.GetSongsAsync()).ReturnsAsync(songs);
        _mockMusicService.Setup(s => s.GetBulkLikeCountsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new List<LikeCountDto>());

        _viewModel.GenreName = "Rock";
        await Task.Delay(100);

        // Play the second rock song (index 1 in filtered list)
        var secondSong = _viewModel.Songs[1];
        await _viewModel.PlayTrackCommand.ExecuteAsync(secondSong);

        _mockPlaybackService.Verify(p => p.SetPlaylist(
            It.Is<List<SongDto>>(playlist => playlist.Select(song => song.Id).SequenceEqual(new[] { 1, 2, 4 })),
            1,
            "Genre Rock"), Times.Once);
    }

    [Test]
    public async Task PlayTrack_WhenSongMatchesCurrentPlayback_TogglesPlayPause()
    {
        var songs = CreateTestSongs();
        _mockMusicService.Setup(s => s.GetSongsAsync()).ReturnsAsync(songs);
        _mockMusicService.Setup(s => s.GetBulkLikeCountsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new List<LikeCountDto>());
        _mockPlaybackService.SetupGet(p => p.PreviewLimitReached).Returns(false);

        _viewModel.GenreName = "Rock";
        await Task.Delay(100);

    _mockPlaybackService.Invocations.Clear();
    _mockMediaPlaybackOnboardingService.Invocations.Clear();

        var currentSong = _viewModel.Songs[1];
        _mockPlaybackService.SetupGet(p => p.CurrentSong).Returns(currentSong);

        await _viewModel.PlayTrackCommand.ExecuteAsync(currentSong);

        _mockPlaybackService.Verify(p => p.TogglePlayPause(), Times.Once);
        _mockPlaybackService.Verify(p => p.SetPlaylist(It.IsAny<List<SongDto>>(), It.IsAny<int>()), Times.Never);
        _mockMediaPlaybackOnboardingService.Verify(s => s.EnsureBackgroundPlaybackExplainedAsync(), Times.Never);
    }

    [Test]
    public void PlayTrack_NullSong_DoesNotCallService()
    {
        _viewModel.PlayTrackCommand.Execute(null);

        _mockPlaybackService.Verify(p =>
            p.SetPlaylist(It.IsAny<List<SongDto>>(), It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task PlayVisibleQueueFromStartAsync_QueuesVisibleSongsFromBeginning()
    {
        var songs = CreateTestSongs();
        _mockMusicService.Setup(s => s.GetSongsAsync()).ReturnsAsync(songs);
        _mockMusicService.Setup(s => s.GetBulkLikeCountsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new List<LikeCountDto>());

        _viewModel.GenreName = "Rock";
        await Task.Delay(100);
        _mockPlaybackService.Invocations.Clear();

        var started = await _viewModel.PlayVisibleQueueFromStartAsync();

        Assert.That(started, Is.True);
        _mockPlaybackService.Verify(p => p.SetPlaylist(
            It.Is<List<SongDto>>(playlist => playlist.Select(song => song.Id).SequenceEqual(new[] { 1, 2, 4 })),
            0,
            "Genre Rock"), Times.Once);
    }

    // --- Like/Dislike ---

    [Test]
    public async Task LikeSong_WhenNotLoggedIn_ShowsLoginPrompt()
    {
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(false);
        _viewModel.CurrentSong = new SongDto { Id = 1, SongTitle = "Test" };

        await _viewModel.LikeSongCommand.ExecuteAsync(null);

        _mockAlertService.Verify(a => a.ShowConfirmAsync(
            "Login Required", It.IsAny<string>(), "Login", "Cancel"), Times.Once);
        _mockMusicService.Verify(s => s.ToggleLikeAsync(It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task LikeSong_WhenNotLoggedInAndPromptAccepted_NavigatesToAnchoredLoginEntry()
    {
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(false);
        _mockAlertService.Setup(a => a.ShowConfirmAsync(
            "Login Required", It.IsAny<string>(), "Login", "Cancel"))
            .ReturnsAsync(true);
        _viewModel.CurrentSong = new SongDto { Id = 1, SongTitle = "Test" };

        await _viewModel.LikeSongCommand.ExecuteAsync(null);

        _mockNavigationService.Verify(n => n.GoToAsync(NavigationRoutes.LoginEntry), Times.Once);
        _mockNavigationService.Verify(n => n.GoToAsync(NavigationRoutes.Login), Times.Never);
        _mockMusicService.Verify(s => s.ToggleLikeAsync(It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task LikeSong_WhenAuthenticated_SetsTheLikeState()
    {
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(true);
        _mockAuthService.Setup(a => a.EmailConfirmed).Returns(true);
        _mockMusicService.Setup(s => s.SetLikeStateAsync(42, true))
            .ReturnsAsync(SetLikeStateOutcome.Applied(new LikeStateResult
            {
                UserLikeStatus = true, LikeCount = 5, DislikeCount = 1
            }));
        _viewModel.CurrentSong = new SongDto { Id = 42, SongTitle = "Test" };

        await _viewModel.LikeSongCommand.ExecuteAsync(null);

        _mockMusicService.Verify(s => s.SetLikeStateAsync(42, true), Times.Once);
        Assert.That(_viewModel.CurrentSong.UserLikeStatus, Is.True);
    }

    [Test]
    public async Task DislikeSong_WhenAuthenticated_SetsTheLikeState()
    {
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(true);
        _mockAuthService.Setup(a => a.EmailConfirmed).Returns(true);
        _mockMusicService.Setup(s => s.SetLikeStateAsync(42, false))
            .ReturnsAsync(SetLikeStateOutcome.Applied(new LikeStateResult
            {
                UserLikeStatus = false, LikeCount = 3, DislikeCount = 7
            }));
        _viewModel.CurrentSong = new SongDto { Id = 42, SongTitle = "Test" };

        await _viewModel.DislikeSongCommand.ExecuteAsync(null);

        _mockMusicService.Verify(s => s.SetLikeStateAsync(42, false), Times.Once);
        Assert.That(_viewModel.CurrentSong.UserLikeStatus, Is.False);
    }

    [Test]
    public async Task LikeSong_NullCurrentSong_DoesNothing()
    {
        await _viewModel.LikeSongCommand.ExecuteAsync(null);

        _mockMusicService.Verify(s => s.ToggleLikeAsync(It.IsAny<int>()), Times.Never);
    }

    // --- Navigation ---

    [Test]
    public async Task NavigateToGenre_ReplacesCurrentPlaylistPlayer()
    {
        await _viewModel.NavigateToGenreCommand.ExecuteAsync("Rock");

        _mockNavigationService.Verify(n =>
            n.GoToReplacingCurrentAsync("playlist-player", It.Is<IDictionary<string, object>>(d =>
                d.ContainsKey("GenreName") && (string)d["GenreName"] == "Rock")),
            Times.Once);
        _mockNavigationService.Verify(n =>
            n.GoToAsync("playlist-player", It.IsAny<IDictionary<string, object>>()),
            Times.Never);
    }

    [Test]
    public async Task NavigateToArtist_ReplacesCurrentPlaylistPlayer()
    {
        await _viewModel.NavigateToArtistCommand.ExecuteAsync("Band A");

        _mockNavigationService.Verify(n =>
            n.GoToReplacingCurrentAsync("playlist-player", It.Is<IDictionary<string, object>>(d =>
                d.ContainsKey("ArtistName") && (string)d["ArtistName"] == "Band A")),
            Times.Once);
        _mockNavigationService.Verify(n =>
            n.GoToAsync("playlist-player", It.IsAny<IDictionary<string, object>>()),
            Times.Never);
    }

    [Test]
    public async Task NavigateToGenre_NullGenre_DoesNothing()
    {
        await _viewModel.NavigateToGenreCommand.ExecuteAsync(null);

        _mockNavigationService.Verify(n =>
            n.GoToReplacingCurrentAsync(It.IsAny<string>(), It.IsAny<IDictionary<string, object>>()),
            Times.Never);
    }

    [Test]
    public async Task NavigateToArtist_EmptyString_DoesNothing()
    {
        await _viewModel.NavigateToArtistCommand.ExecuteAsync(string.Empty);

        _mockNavigationService.Verify(n =>
            n.GoToReplacingCurrentAsync(It.IsAny<string>(), It.IsAny<IDictionary<string, object>>()),
            Times.Never);
    }

    [Test]
    public async Task ViewBio_NavigatesToPersonaPage()
    {
        _viewModel.CurrentSong = new SongDto
        {
            Id = 1, SongTitle = "Test", ArtistName = "Band A",
            PersonaImageUrl = "http://img.png", PersonaBio = "A cool band"
        };

        await _viewModel.ViewBioCommand.ExecuteAsync(null);

        _mockNavigationService.Verify(n =>
            n.GoToAsync("persona", It.Is<Dictionary<string, object>>(d =>
                (string)d["PersonaName"] == "Band A")),
            Times.Once);
    }

    [Test]
    public async Task ViewBio_NullCurrentSong_DoesNothing()
    {
        await _viewModel.ViewBioCommand.ExecuteAsync(null);

        _mockNavigationService.Verify(n =>
            n.GoToAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()),
            Times.Never);
    }

    // --- SignalR updates ---

    [Test]
    public async Task SignalR_StreamCountUpdate_UpdatesSong()
    {
        var songs = CreateTestSongs();
        _mockMusicService.Setup(s => s.GetSongsAsync()).ReturnsAsync(songs);
        _mockMusicService.Setup(s => s.GetBulkLikeCountsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new List<LikeCountDto>());

        _viewModel.GenreName = "Rock";
        await Task.Delay(100);

        // Raise the SignalR event
        _mockSignalRService.Raise(s => s.OnStreamCountUpdated += null, 1, 999);

        Assert.That(_viewModel.Songs.First(s => s.Id == 1).StreamCount, Is.EqualTo(999));
    }

    [Test]
    public async Task MusicService_StreamCountRecorded_UpdatesSong()
    {
        var songs = CreateTestSongs();
        _mockMusicService.Setup(s => s.GetSongsAsync()).ReturnsAsync(songs);
        _mockMusicService.Setup(s => s.GetBulkLikeCountsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new List<LikeCountDto>());

        _viewModel.GenreName = "Rock";
        await Task.Delay(100);

        _mockMusicService.Raise(s => s.OnStreamCountRecorded += null, 1, 999);

        Assert.That(_viewModel.Songs.First(s => s.Id == 1).StreamCount, Is.EqualTo(999));
    }

    [Test]
    public async Task SignalR_LikeCountUpdate_UpdatesSong()
    {
        var songs = CreateTestSongs();
        _mockMusicService.Setup(s => s.GetSongsAsync()).ReturnsAsync(songs);
        _mockMusicService.Setup(s => s.GetBulkLikeCountsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new List<LikeCountDto>());

        _viewModel.GenreName = "Rock";
        await Task.Delay(100);

        _mockSignalRService.Raise(s => s.OnLikeCountUpdated += null, 2, 10, 3);

        var song = _viewModel.Songs.First(s => s.Id == 2);
        Assert.That(song.LikeCount, Is.EqualTo(10));
        Assert.That(song.DislikeCount, Is.EqualTo(3));
    }

    // --- CurrentSong tracks PlaybackService ---

    [Test]
    public async Task StateChanged_CurrentSong_UpdatesCurrentSong()
    {
        var songs = CreateTestSongs();
        _mockMusicService.Setup(s => s.GetSongsAsync()).ReturnsAsync(songs);
        _mockMusicService.Setup(s => s.GetBulkLikeCountsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new List<LikeCountDto>());

        _viewModel.GenreName = "Rock";
        await Task.Delay(100);
        var newSong = _viewModel.Songs[1];
        _mockPlaybackService.Setup(p => p.CurrentSong).Returns(newSong);

        // Raise PlaybackService.StateChanged for CurrentSong
        _mockPlaybackService.Raise(p => p.StateChanged += null, nameof(IPlaybackService.CurrentSong));

        Assert.That(_viewModel.CurrentSong, Is.SameAs(newSong));
    }

    [Test]
    public async Task StateChanged_CurrentSongOutsideVisibleSongs_ClearsCurrentSong()
    {
        var songs = CreateTestSongs();
        _mockMusicService.Setup(s => s.GetSongsAsync()).ReturnsAsync(songs);
        _mockMusicService.Setup(s => s.GetBulkLikeCountsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new List<LikeCountDto>());

        _viewModel.GenreName = "Rock";
        await Task.Delay(100);

        var offPageSong = new SongDto { Id = 99, SongTitle = "Featured Song", ArtistName = "Other Artist" };
        _mockPlaybackService.Setup(p => p.CurrentSong).Returns(offPageSong);

        _mockPlaybackService.Raise(p => p.StateChanged += null, nameof(IPlaybackService.CurrentSong));

        Assert.That(_viewModel.CurrentSong, Is.Null);
        Assert.That(_viewModel.ShareUrl, Is.EqualTo(string.Empty));
        Assert.That(_viewModel.ShowTracksHeader, Is.True);
    }

    // --- Subscription badge ---

    [Test]
    public async Task HasActiveSubscription_SetFromAuthService()
    {
        _mockAuthService.Setup(a => a.HasActiveSubscription).Returns(true);
        _mockMusicService.Setup(s => s.GetSongsAsync()).ReturnsAsync(CreateTestSongs());
        _mockMusicService.Setup(s => s.GetBulkLikeCountsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new List<LikeCountDto>());

        _viewModel.GenreName = "Rock";
        await Task.Delay(100);

        Assert.That(_viewModel.HasActiveSubscription, Is.True);
    }

    // --- Cleanup ---

    [Test]
    public void Cleanup_UnsubscribesFromEvents()
    {
        _viewModel.Cleanup();

        // After cleanup, raising events should not cause issues
        _mockSignalRService.Raise(s => s.OnStreamCountUpdated += null, 1, 100);
        _mockSignalRService.Raise(s => s.OnLikeCountUpdated += null, 1, 5, 2);
        _mockPlaybackService.Raise(p => p.StateChanged += null, "CurrentSong");

        // No exception = success
        Assert.Pass();
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
        var song = new SongDto { Id = 1, SongTitle = "Test", StreamCount = 3 };
        _viewModel.Songs.Add(song);

        _viewModel.Cleanup();
        _viewModel.Activate();

        _mockSignalRService.Raise(s => s.OnStreamCountUpdated += null, 1, 9);

        Assert.That(song.StreamCount, Is.EqualTo(9));
    }

    // --- Like counts loaded ---

    [Test]
    public async Task LoadPlaylist_LoadsLikeCounts()
    {
        var songs = CreateTestSongs();
        _mockMusicService.Setup(s => s.GetSongsAsync()).ReturnsAsync(songs);
        _mockMusicService.Setup(s => s.GetBulkLikeCountsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new List<LikeCountDto>
            {
                new() { SongMetadataId = 1, LikeCount = 10, DislikeCount = 2 },
                new() { SongMetadataId = 2, LikeCount = 5, DislikeCount = 0 },
            });

        _viewModel.GenreName = "Rock";
        await Task.Delay(100);

        Assert.That(_viewModel.Songs.First(s => s.Id == 1).LikeCount, Is.EqualTo(10));
        Assert.That(_viewModel.Songs.First(s => s.Id == 2).LikeCount, Is.EqualTo(5));
    }

    // --- URI decoding ---

    [Test]
    public async Task GenreName_DecodesUriEncodedName()
    {
        var songs = new List<SongDto>
        {
            new() { Id = 1, SongTitle = "S1", ArtistName = "A", Genre = "R&B", StreamUrl = "http://a.mp3" },
        };
        _mockMusicService.Setup(s => s.GetSongsAsync()).ReturnsAsync(songs);
        _mockMusicService.Setup(s => s.GetBulkLikeCountsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new List<LikeCountDto>());

        _viewModel.GenreName = "R%26B";
        await Task.Delay(100);

        Assert.That(_viewModel.Songs, Has.Count.EqualTo(1));
        Assert.That(_viewModel.PlaylistTitle, Is.EqualTo("R&B"));
    }

    // --- Refresh ---

    [Test]
    public async Task Refresh_ReloadsPlaylistData()
    {
        var songs = CreateTestSongs();
        _mockMusicService.Setup(s => s.GetSongsAsync()).ReturnsAsync(songs);
        _mockMusicService.Setup(s => s.GetBulkLikeCountsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new List<LikeCountDto>());

        _viewModel.GenreName = "Rock";
        await Task.Delay(100);

        Assert.That(_viewModel.Songs, Has.Count.EqualTo(3));

        // Refresh should reload
        await _viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.That(_viewModel.IsLoading, Is.False);
        Assert.That(_viewModel.Songs, Has.Count.EqualTo(3));
        _mockMusicService.Verify(s => s.GetSongsAsync(), Times.AtLeast(2));
    }

    // --- Loading by PlaylistId / RecommendedUserId (mobile-playlist endpoints) ---

    private PlaylistSongsDto MakePlaylistSongs(int playlistId, string name, bool isSystem, params (int SongMetaId, int UserPlaylistId)[] entries)
    {
        var songs = entries.Select(e => new PlaylistSongDto
        {
            Id = e.SongMetaId,
            SongMetadataId = e.SongMetaId,
            UserPlaylistId = e.UserPlaylistId,
            SongTitle = $"Song {e.SongMetaId}",
            ArtistName = "Someone",
            Genre = "Pop",
            StreamUrl = $"http://{e.SongMetaId}.mp3",
        }).ToList();
        return new PlaylistSongsDto
        {
            PlaylistId = playlistId,
            PlaylistName = name,
            IsSystemGenerated = isSystem,
            Songs = songs,
        };
    }

    [Test]
    public async Task PlaylistIdParam_CustomPlaylist_LoadsAndEnablesReorderWithSubscription()
    {
        _mockAuthService.SetupGet(a => a.HasActiveSubscription).Returns(true);
        _mockMusicService.Setup(s => s.GetBulkLikeCountsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new List<LikeCountDto>());
        _mockPlaylistService.Setup(p => p.GetPlaylistSongsAsync(42))
            .ReturnsAsync(MakePlaylistSongs(42, "My Mix", isSystem: false, (10, 100), (11, 101)));

        _viewModel.PlaylistIdParam = "42";
        await Task.Delay(100);

        Assert.That(_viewModel.PlaylistTitle, Is.EqualTo("My Mix"));
        Assert.That(_viewModel.Songs, Has.Count.EqualTo(2));
        Assert.That(_viewModel.IsUserPlaylist, Is.True);
        Assert.That(_viewModel.IsReorderEnabled, Is.True);
    }

    [Test]
    public async Task PlaylistIdParam_SystemPlaylist_DisablesReorder()
    {
        _mockAuthService.SetupGet(a => a.HasActiveSubscription).Returns(true);
        _mockMusicService.Setup(s => s.GetBulkLikeCountsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new List<LikeCountDto>());
        _mockPlaylistService.Setup(p => p.GetPlaylistSongsAsync(7))
            .ReturnsAsync(MakePlaylistSongs(7, "Liked Songs", isSystem: true, (1, 200)));

        _viewModel.PlaylistIdParam = "7";
        await Task.Delay(100);

        Assert.That(_viewModel.IsUserPlaylist, Is.False);
        Assert.That(_viewModel.IsReorderEnabled, Is.False);
    }

    [Test]
    public async Task PlaylistIdParam_NoSubscription_DisablesReorderEvenForCustom()
    {
        _mockAuthService.SetupGet(a => a.HasActiveSubscription).Returns(false);
        _mockMusicService.Setup(s => s.GetBulkLikeCountsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new List<LikeCountDto>());
        _mockPlaylistService.Setup(p => p.GetPlaylistSongsAsync(42))
            .ReturnsAsync(MakePlaylistSongs(42, "My Mix", isSystem: false, (10, 100)));

        _viewModel.PlaylistIdParam = "42";
        await Task.Delay(100);

        Assert.That(_viewModel.IsUserPlaylist, Is.True);
        Assert.That(_viewModel.IsReorderEnabled, Is.False);
    }

    [Test]
    public async Task RecommendedUserIdParam_LoadsRecommended()
    {
        _mockAuthService.SetupGet(a => a.HasActiveSubscription).Returns(true);
        _mockMusicService.Setup(s => s.GetBulkLikeCountsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new List<LikeCountDto>());
        _mockPlaylistService.Setup(p => p.GetRecommendedSongsAsync())
            .ReturnsAsync(new PlaylistSongsDto
            {
                PlaylistId = 0,
                PlaylistName = "Recommended",
                IsSystemGenerated = true,
                Songs =
                [
                    new PlaylistSongDto { Id = 5, SongMetadataId = 5, SongTitle = "Hit", ArtistName = "Star", Genre = "Pop", StreamUrl = "http://5.mp3" }
                ]
            });

        _viewModel.RecommendedUserIdParam = "99";
        await Task.Delay(100);

        Assert.That(_viewModel.PlaylistTitle, Is.EqualTo("Recommended"));
        Assert.That(_viewModel.Songs, Has.Count.EqualTo(1));
        Assert.That(_viewModel.IsUserPlaylist, Is.False);
        _mockPlaylistService.Verify(p => p.GetRecommendedSongsAsync(), Times.Once);
    }

    [Test]
    public async Task PlaylistIdParam_MapsCreatorIdentifiersForTipButtons()
    {
        _mockAuthService.SetupGet(a => a.HasActiveSubscription).Returns(true);
        _mockMusicService.Setup(s => s.GetBulkLikeCountsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new List<LikeCountDto>());
        _mockPlaylistService.Setup(p => p.GetPlaylistSongsAsync(42))
            .ReturnsAsync(new PlaylistSongsDto
            {
                PlaylistId = 42,
                PlaylistName = "My Mix",
                IsSystemGenerated = false,
                Songs =
                [
                    new PlaylistSongDto
                    {
                        Id = 5,
                        SongMetadataId = 5,
                        SongTitle = "Hit",
                        ArtistName = "Star",
                        Genre = "Pop",
                        StreamUrl = "http://5.mp3",
                        CreatorId = 55,
                        CreatorUserId = 77
                    }
                ]
            });

        _viewModel.PlaylistIdParam = "42";
        await Task.Delay(100);

        Assert.That(_viewModel.Songs, Has.Count.EqualTo(1));
        Assert.That(_viewModel.Songs[0].CreatorId, Is.EqualTo(55));
        Assert.That(_viewModel.Songs[0].CreatorUserId, Is.EqualTo(77));
    }

    [Test]
    public async Task MoveTrackUp_ReordersAndPersists()
    {
        _mockAuthService.SetupGet(a => a.HasActiveSubscription).Returns(true);
        _mockMusicService.Setup(s => s.GetBulkLikeCountsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new List<LikeCountDto>());
        _mockPlaylistService.Setup(p => p.GetPlaylistSongsAsync(42))
            .ReturnsAsync(MakePlaylistSongs(42, "My Mix", isSystem: false, (10, 100), (11, 101), (12, 102)));
        _mockPlaylistService.Setup(p => p.ReorderAsync(42, It.IsAny<IReadOnlyList<int>>()))
            .ReturnsAsync(PlaylistOperationResult.Ok());

        _viewModel.PlaylistIdParam = "42";
        await Task.Delay(100);

        // Move third item up
        var third = _viewModel.Songs[2];
        await _viewModel.MoveTrackUpCommand.ExecuteAsync(third);

        Assert.That(_viewModel.Songs[1].Id, Is.EqualTo(12));
        _mockPlaylistService.Verify(p => p.ReorderAsync(42,
            It.Is<IReadOnlyList<int>>(ids => ids.SequenceEqual(new[] { 100, 102, 101 }))),
            Times.Once);
    }

    [Test]
    public async Task MoveTrackUp_FirstItem_DoesNothing()
    {
        _mockAuthService.SetupGet(a => a.HasActiveSubscription).Returns(true);
        _mockMusicService.Setup(s => s.GetBulkLikeCountsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new List<LikeCountDto>());
        _mockPlaylistService.Setup(p => p.GetPlaylistSongsAsync(42))
            .ReturnsAsync(MakePlaylistSongs(42, "My Mix", isSystem: false, (10, 100), (11, 101)));

        _viewModel.PlaylistIdParam = "42";
        await Task.Delay(100);

        await _viewModel.MoveTrackUpCommand.ExecuteAsync(_viewModel.Songs[0]);

        _mockPlaylistService.Verify(p => p.ReorderAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<int>>()), Times.Never);
    }

    [Test]
    public async Task MoveTrackDown_LastItem_DoesNothing()
    {
        _mockAuthService.SetupGet(a => a.HasActiveSubscription).Returns(true);
        _mockMusicService.Setup(s => s.GetBulkLikeCountsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new List<LikeCountDto>());
        _mockPlaylistService.Setup(p => p.GetPlaylistSongsAsync(42))
            .ReturnsAsync(MakePlaylistSongs(42, "My Mix", isSystem: false, (10, 100), (11, 101)));

        _viewModel.PlaylistIdParam = "42";
        await Task.Delay(100);

        await _viewModel.MoveTrackDownCommand.ExecuteAsync(_viewModel.Songs[^1]);

        _mockPlaylistService.Verify(p => p.ReorderAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<int>>()), Times.Never);
    }

    [Test]
    public async Task MoveTrack_WhenReorderDisabled_DoesNothing()
    {
        _mockAuthService.SetupGet(a => a.HasActiveSubscription).Returns(false);
        _mockMusicService.Setup(s => s.GetBulkLikeCountsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new List<LikeCountDto>());
        _mockPlaylistService.Setup(p => p.GetPlaylistSongsAsync(42))
            .ReturnsAsync(MakePlaylistSongs(42, "My Mix", isSystem: false, (10, 100), (11, 101)));

        _viewModel.PlaylistIdParam = "42";
        await Task.Delay(100);

        Assume.That(_viewModel.IsReorderEnabled, Is.False);
        await _viewModel.MoveTrackDownCommand.ExecuteAsync(_viewModel.Songs[0]);

        _mockPlaylistService.Verify(p => p.ReorderAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<int>>()), Times.Never);
    }

    // --- Add / Remove songs (custom playlists only) ---

    private async Task LoadCustomPlaylistAsync()
    {
        _mockAuthService.SetupGet(a => a.HasActiveSubscription).Returns(true);
        _mockAuthService.SetupGet(a => a.IsLoggedIn).Returns(true);
        _mockAuthService.SetupGet(a => a.EmailConfirmed).Returns(true);
        _mockMusicService.Setup(s => s.GetBulkLikeCountsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new List<LikeCountDto>());
        _mockPlaylistService.Setup(p => p.GetPlaylistSongsAsync(42))
            .ReturnsAsync(MakePlaylistSongs(42, "My Mix", isSystem: false, (10, 100), (11, 101)));
        _viewModel.PlaylistIdParam = "42";
        await Task.Delay(100);
        Assume.That(_viewModel.IsUserPlaylist, Is.True);
    }

    [Test]
    public async Task RemoveSongFromPlaylist_Confirmed_CallsServiceAndReloads()
    {
        await LoadCustomPlaylistAsync();
        _mockAlertService.Setup(a => a.ShowConfirmAsync("Remove song",
            It.IsAny<string>(), "Remove", "Cancel")).ReturnsAsync(true);
        _mockPlaylistService.Setup(p => p.RemoveSongAsync(42, 100))
            .ReturnsAsync(PlaylistOperationResult.Ok());

        var target = _viewModel.Songs.First(s => s.Id == 10);
        await _viewModel.RemoveSongFromPlaylistCommand.ExecuteAsync(target);

        _mockPlaylistService.Verify(p => p.RemoveSongAsync(42, 100), Times.Once);
        _mockPlaylistService.Verify(p => p.GetPlaylistSongsAsync(42), Times.AtLeast(2));
    }

    [Test]
    public async Task RemoveSongFromPlaylist_Cancelled_DoesNothing()
    {
        await LoadCustomPlaylistAsync();
        _mockAlertService.Setup(a => a.ShowConfirmAsync("Remove song",
            It.IsAny<string>(), "Remove", "Cancel")).ReturnsAsync(false);

        var target = _viewModel.Songs.First(s => s.Id == 10);
        await _viewModel.RemoveSongFromPlaylistCommand.ExecuteAsync(target);

        _mockPlaylistService.Verify(p => p.RemoveSongAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task RemoveSongFromPlaylist_NotUserPlaylist_DoesNothing()
    {
        // Liked Songs is a system playlist — remove should be a no-op
        _mockAuthService.SetupGet(a => a.HasActiveSubscription).Returns(true);
        _mockMusicService.Setup(s => s.GetBulkLikeCountsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new List<LikeCountDto>());
        _mockPlaylistService.Setup(p => p.GetPlaylistSongsAsync(7))
            .ReturnsAsync(MakePlaylistSongs(7, "Liked", isSystem: true, (1, 200)));
        _viewModel.PlaylistIdParam = "7";
        await Task.Delay(100);

        var target = _viewModel.Songs.First();
        await _viewModel.RemoveSongFromPlaylistCommand.ExecuteAsync(target);

        _mockPlaylistService.Verify(p => p.RemoveSongAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task RemoveSongFromPlaylist_Failure_SetsErrorMessage()
    {
        await LoadCustomPlaylistAsync();
        _mockAlertService.Setup(a => a.ShowConfirmAsync(It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
        _mockPlaylistService.Setup(p => p.RemoveSongAsync(42, 100))
            .ReturnsAsync(PlaylistOperationResult.Fail("Boom"));

        var target = _viewModel.Songs.First(s => s.Id == 10);
        await _viewModel.RemoveSongFromPlaylistCommand.ExecuteAsync(target);

        Assert.That(_viewModel.ErrorMessage, Is.EqualTo("Boom"));
    }

    // --- Empty custom playlist UI flags ---

    [Test]
    public async Task EmptyCustomPlaylist_ExposesAddSongsAffordance()
    {
        _mockAuthService.SetupGet(a => a.HasActiveSubscription).Returns(true);
        _mockMusicService.Setup(s => s.GetBulkLikeCountsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new List<LikeCountDto>());
        _mockPlaylistService.Setup(p => p.GetPlaylistSongsAsync(42))
            .ReturnsAsync(new PlaylistSongsDto
            {
                PlaylistId = 42,
                PlaylistName = "Empty Mix",
                IsSystemGenerated = false,
                Songs = [],
            });

        _viewModel.PlaylistIdParam = "42";
        await Task.Delay(100);

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.IsUserPlaylist, Is.True);
            Assert.That(_viewModel.HasSongs, Is.False);
            Assert.That(_viewModel.ShowTracksHeader, Is.True, "Header must be visible so the Add Songs button shows.");
            Assert.That(_viewModel.ShowEmptyPlaylistPrompt, Is.True, "Empty custom playlists must show the Add Songs prompt.");
            Assert.That(_viewModel.IsLoading, Is.False);
        });
    }

    [Test]
    public async Task EmptySystemPlaylist_DoesNotShowEmptyPrompt()
    {
        _mockAuthService.SetupGet(a => a.HasActiveSubscription).Returns(true);
        _mockMusicService.Setup(s => s.GetBulkLikeCountsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new List<LikeCountDto>());
        _mockPlaylistService.Setup(p => p.GetPlaylistSongsAsync(7))
            .ReturnsAsync(new PlaylistSongsDto
            {
                PlaylistId = 7,
                PlaylistName = "Liked",
                IsSystemGenerated = true,
                Songs = [],
            });

        _viewModel.PlaylistIdParam = "7";
        await Task.Delay(100);

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.IsUserPlaylist, Is.False);
            Assert.That(_viewModel.ShowEmptyPlaylistPrompt, Is.False);
        });
    }

    [Test]
    public async Task CustomPlaylist_WithSongs_HidesEmptyPromptAndShowsHeader()
    {
        await LoadCustomPlaylistAsync();

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.HasSongs, Is.True);
            Assert.That(_viewModel.ShowEmptyPlaylistPrompt, Is.False);
            Assert.That(_viewModel.ShowTracksHeader, Is.True);
        });
    }
}
