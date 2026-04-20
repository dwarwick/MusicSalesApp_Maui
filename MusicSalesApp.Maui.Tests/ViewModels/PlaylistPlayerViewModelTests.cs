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
    private Mock<ISignalRService> _mockSignalRService;
    private Mock<IAppConfig> _mockAppConfig;
    private Mock<IBillingService> _mockBillingService;
    private PlaylistPlayerViewModel _viewModel;

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
        _mockBillingService = new Mock<IBillingService>();
        _mockAppConfig.Setup(c => c.WebBaseUrl).Returns("https://streamtunes.net");

        _viewModel = CreateViewModel();
    }

    private PlaylistPlayerViewModel CreateViewModel() => new(
        _mockMusicService.Object, _mockAlertService.Object,
        _mockAuthService.Object, _mockNavigationService.Object,
        _mockPlaybackService.Object, _mockSignalRService.Object,
        _mockAppConfig.Object, _mockBillingService.Object);

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
            p.SetPlaylist(It.Is<List<SongDto>>(l => l.Count == 3), 0), Times.Once);
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
            p.SetPlaylist(It.Is<List<SongDto>>(l => l.Count == 2), 0), Times.Once);
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
    public async Task PlayTrack_CallsPlayTrackAtIndex()
    {
        var songs = CreateTestSongs();
        _mockMusicService.Setup(s => s.GetSongsAsync()).ReturnsAsync(songs);
        _mockMusicService.Setup(s => s.GetBulkLikeCountsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new List<LikeCountDto>());

        _viewModel.GenreName = "Rock";
        await Task.Delay(100);

        // Play the second rock song (index 1 in filtered list)
        var secondSong = _viewModel.Songs[1];
        _viewModel.PlayTrackCommand.Execute(secondSong);

        _mockPlaybackService.Verify(p => p.PlayTrackAtIndex(1), Times.Once);
    }

    [Test]
    public void PlayTrack_NullSong_DoesNotCallService()
    {
        _viewModel.PlayTrackCommand.Execute(null);

        _mockPlaybackService.Verify(p =>
            p.PlayTrackAtIndex(It.IsAny<int>()), Times.Never);
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
    public async Task LikeSong_WhenAuthenticated_CallsToggleLike()
    {
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(true);
        _mockAuthService.Setup(a => a.EmailConfirmed).Returns(true);
        _mockMusicService.Setup(s => s.ToggleLikeAsync(42)).ReturnsAsync(new LikeToggleResult
        {
            IsLiked = true, LikeCount = 5, DislikeCount = 1
        });
        _viewModel.CurrentSong = new SongDto { Id = 42, SongTitle = "Test" };

        await _viewModel.LikeSongCommand.ExecuteAsync(null);

        _mockMusicService.Verify(s => s.ToggleLikeAsync(42), Times.Once);
        Assert.That(_viewModel.CurrentSong.UserLikeStatus, Is.True);
    }

    [Test]
    public async Task DislikeSong_WhenAuthenticated_CallsToggleDislike()
    {
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(true);
        _mockAuthService.Setup(a => a.EmailConfirmed).Returns(true);
        _mockMusicService.Setup(s => s.ToggleDislikeAsync(42)).ReturnsAsync(new LikeToggleResult
        {
            IsDisliked = true, LikeCount = 3, DislikeCount = 7
        });
        _viewModel.CurrentSong = new SongDto { Id = 42, SongTitle = "Test" };

        await _viewModel.DislikeSongCommand.ExecuteAsync(null);

        _mockMusicService.Verify(s => s.ToggleDislikeAsync(42), Times.Once);
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
    public async Task NavigateToGenre_NavigatesToPlaylistPlayer()
    {
        await _viewModel.NavigateToGenreCommand.ExecuteAsync("Rock");

        _mockNavigationService.Verify(n =>
            n.GoToAsync("playlist-player", It.Is<Dictionary<string, object>>(d =>
                d.ContainsKey("GenreName") && (string)d["GenreName"] == "Rock")),
            Times.Once);
    }

    [Test]
    public async Task NavigateToArtist_NavigatesToPlaylistPlayer()
    {
        await _viewModel.NavigateToArtistCommand.ExecuteAsync("Band A");

        _mockNavigationService.Verify(n =>
            n.GoToAsync("playlist-player", It.Is<Dictionary<string, object>>(d =>
                d.ContainsKey("ArtistName") && (string)d["ArtistName"] == "Band A")),
            Times.Once);
    }

    [Test]
    public async Task NavigateToGenre_NullGenre_DoesNothing()
    {
        await _viewModel.NavigateToGenreCommand.ExecuteAsync(null);

        _mockNavigationService.Verify(n =>
            n.GoToAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()),
            Times.Never);
    }

    [Test]
    public async Task NavigateToArtist_EmptyString_DoesNothing()
    {
        await _viewModel.NavigateToArtistCommand.ExecuteAsync(string.Empty);

        _mockNavigationService.Verify(n =>
            n.GoToAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()),
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

        var newSong = new SongDto { Id = 99, SongTitle = "New Track" };
        _mockPlaybackService.Setup(p => p.CurrentSong).Returns(newSong);

        _viewModel.GenreName = "Rock";
        await Task.Delay(100);

        // Raise PlaybackService.StateChanged for CurrentSong
        _mockPlaybackService.Raise(p => p.StateChanged += null, nameof(IPlaybackService.CurrentSong));

        Assert.That(_viewModel.CurrentSong, Is.SameAs(newSong));
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
}
