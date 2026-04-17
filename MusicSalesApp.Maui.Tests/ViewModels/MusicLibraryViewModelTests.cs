using Moq;
using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Tests.ViewModels;

[TestFixture]
public class MusicLibraryViewModelTests
{
    private Mock<IMusicService> _mockMusicService;
    private Mock<IAlertService> _mockAlertService;
    private Mock<ISignalRService> _mockSignalRService;
    private Mock<IAuthService> _mockAuthService;
    private Mock<INavigationService> _mockNavigationService;
    private Mock<IPlaybackService> _mockPlaybackService;
    private Mock<IAppConfig> _mockAppConfig;
    private MusicLibraryViewModel _viewModel;

    [SetUp]
    public void Setup()
    {
        _mockMusicService = new Mock<IMusicService>();
        _mockAlertService = new Mock<IAlertService>();
        _mockSignalRService = new Mock<ISignalRService>();
        _mockAuthService = new Mock<IAuthService>();
        _mockNavigationService = new Mock<INavigationService>();
        _mockPlaybackService = new Mock<IPlaybackService>();
        _mockAppConfig = new Mock<IAppConfig>();
        _mockAppConfig.Setup(c => c.WebBaseUrl).Returns("https://streamtunes.net");
        _mockAppConfig.Setup(c => c.ApiBaseUrl).Returns("https://streamtunes.net");
        _viewModel = new MusicLibraryViewModel(
            _mockMusicService.Object, _mockAlertService.Object, _mockSignalRService.Object,
            _mockAuthService.Object, _mockNavigationService.Object,
            _mockPlaybackService.Object, _mockAppConfig.Object);
    }

    [Test]
    public async Task LoadSongsAsync_PopulatesSongsCollection()
    {
        // Arrange
        var songs = new List<SongDto>
        {
            new() { Id = 1, SongTitle = "Song One" },
            new() { Id = 2, SongTitle = "Song Two" }
        };
        _mockMusicService.Setup(s => s.GetSongsAsync()).ReturnsAsync(songs);

        // Act
        await _viewModel.LoadSongsCommand.ExecuteAsync(null);

        // Assert
        Assert.That(_viewModel.Songs, Has.Count.EqualTo(2));
        Assert.That(_viewModel.Songs[0].SongTitle, Is.EqualTo("Song One"));
        Assert.That(_viewModel.Songs[1].SongTitle, Is.EqualTo("Song Two"));
    }

    [Test]
    public async Task LoadSongsAsync_SetsIsLoadingDuringLoad()
    {
        // Arrange
        var tcs = new TaskCompletionSource<List<SongDto>>();
        _mockMusicService.Setup(s => s.GetSongsAsync()).Returns(tcs.Task);

        // Act
        var loadTask = _viewModel.LoadSongsCommand.ExecuteAsync(null);

        // Assert - should be loading
        Assert.That(_viewModel.IsLoading, Is.True);

        // Complete
        tcs.SetResult([]);
        await loadTask;

        Assert.That(_viewModel.IsLoading, Is.False);
    }

    [Test]
    public async Task LoadSongsAsync_SetsErrorMessageOnException()
    {
        // Arrange
        _mockMusicService.Setup(s => s.GetSongsAsync())
            .ThrowsAsync(new Exception("Network error"));

        // Act
        await _viewModel.LoadSongsCommand.ExecuteAsync(null);

        // Assert
        Assert.That(_viewModel.ErrorMessage, Does.Contain("Network error"));
        Assert.That(_viewModel.IsLoading, Is.False);
    }

    [Test]
    public async Task LoadSongsAsync_ClearsExistingSongsBeforeReloading()
    {
        // Arrange
        _viewModel.Songs.Add(new SongDto { Id = 99, SongTitle = "Old Song" });
        _mockMusicService.Setup(s => s.GetSongsAsync())
            .ReturnsAsync([new SongDto { Id = 1, SongTitle = "New Song" }]);

        // Act
        await _viewModel.LoadSongsCommand.ExecuteAsync(null);

        // Assert
        Assert.That(_viewModel.Songs, Has.Count.EqualTo(1));
        Assert.That(_viewModel.Songs[0].SongTitle, Is.EqualTo("New Song"));
    }

    [Test]
    public void PlaySong_DelegatesToPlaybackService()
    {
        // Arrange
        var song = new SongDto { Id = 1, SongTitle = "Test", StreamUrl = "https://example.com/test.mp3" };

        // Act
        _viewModel.PlaySongCommand.Execute(song);

        // Assert
        _mockPlaybackService.Verify(p => p.PlaySong(song), Times.Once);
    }

    [Test]
    public void TogglePlayPause_DelegatesToPlaybackService()
    {
        // Act
        _viewModel.TogglePlayPauseCommand.Execute(null);

        // Assert
        _mockPlaybackService.Verify(p => p.TogglePlayPause(), Times.Once);
    }

    [Test]
    public void Stop_DelegatesToPlaybackService()
    {
        // Act
        _viewModel.StopCommand.Execute(null);

        // Assert
        _mockPlaybackService.Verify(p => p.Stop(), Times.Once);
    }

    // --- Like counts ---

    [Test]
    public void GetLikeCount_ReturnsZeroWhenNoData()
    {
        Assert.That(_viewModel.GetLikeCount(999), Is.EqualTo(0));
    }

    [Test]
    public void GetDislikeCount_ReturnsZeroWhenNoData()
    {
        Assert.That(_viewModel.GetDislikeCount(999), Is.EqualTo(0));
    }

    [Test]
    public async Task LoadSongsAsync_LoadsLikeCounts()
    {
        // Arrange
        var songs = new List<SongDto>
        {
            new() { Id = 1, SongTitle = "Song One" },
            new() { Id = 2, SongTitle = "Song Two" }
        };
        var likeCounts = new List<LikeCountDto>
        {
            new() { SongMetadataId = 1, LikeCount = 5, DislikeCount = 2 },
            new() { SongMetadataId = 2, LikeCount = 10, DislikeCount = 0 }
        };
        _mockMusicService.Setup(s => s.GetSongsAsync()).ReturnsAsync(songs);
        _mockMusicService.Setup(s => s.GetBulkLikeCountsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(likeCounts);

        // Act
        await _viewModel.LoadSongsCommand.ExecuteAsync(null);

        // Assert
        Assert.That(_viewModel.GetLikeCount(1), Is.EqualTo(5));
        Assert.That(_viewModel.GetDislikeCount(1), Is.EqualTo(2));
        Assert.That(_viewModel.GetLikeCount(2), Is.EqualTo(10));
        Assert.That(_viewModel.Songs[0].LikeCount, Is.EqualTo(5));
        Assert.That(_viewModel.Songs[1].LikeCount, Is.EqualTo(10));
    }

    [Test]
    public async Task LoadSongsAsync_HandlesLikeCountFailureGracefully()
    {
        // Arrange
        var songs = new List<SongDto> { new() { Id = 1, SongTitle = "Song One" } };
        _mockMusicService.Setup(s => s.GetSongsAsync()).ReturnsAsync(songs);
        _mockMusicService.Setup(s => s.GetBulkLikeCountsAsync(It.IsAny<IEnumerable<int>>()))
            .ThrowsAsync(new Exception("Network error"));

        // Act
        await _viewModel.LoadSongsCommand.ExecuteAsync(null);

        // Assert - songs still loaded, no error message (like counts are non-fatal)
        Assert.That(_viewModel.Songs, Has.Count.EqualTo(1));
        Assert.That(_viewModel.ErrorMessage, Is.Null);
    }

    // --- Stop resets playback state ---

    [Test]
    public void Stop_ResetsPlaybackProgress()
    {
        // Arrange / Act
        _viewModel.StopCommand.Execute(null);

        // Assert - delegates to playback service
        _mockPlaybackService.Verify(p => p.Stop(), Times.Once);
    }

    // --- SignalR real-time updates ---

    [Test]
    public async Task StartSignalRAsync_StartsService()
    {
        // Act
        await _viewModel.StartSignalRAsync();

        // Assert
        _mockSignalRService.Verify(s => s.StartAsync(), Times.Once);
    }

    [Test]
    public void SignalR_StreamCountUpdate_UpdatesSongDto()
    {
        // Arrange - add a song to the collection
        var song = new SongDto { Id = 42, SongTitle = "Test", StreamCount = 10 };
        _viewModel.Songs.Add(song);

        // Act - raise the SignalR event
        _mockSignalRService.Raise(s => s.OnStreamCountUpdated += null, 42, 15);

        // Assert
        Assert.That(song.StreamCount, Is.EqualTo(15));
    }

    [Test]
    public void SignalR_StreamCountUpdate_IgnoresUnknownSong()
    {
        // Arrange
        var song = new SongDto { Id = 1, SongTitle = "Test", StreamCount = 10 };
        _viewModel.Songs.Add(song);

        // Act - fire update for a different song
        _mockSignalRService.Raise(s => s.OnStreamCountUpdated += null, 999, 50);

        // Assert - original song unchanged
        Assert.That(song.StreamCount, Is.EqualTo(10));
    }

    [Test]
    public void SignalR_LikeCountUpdate_UpdatesSongDto()
    {
        // Arrange
        var song = new SongDto { Id = 42, SongTitle = "Test", LikeCount = 5, DislikeCount = 2 };
        _viewModel.Songs.Add(song);

        // Act
        _mockSignalRService.Raise(s => s.OnLikeCountUpdated += null, 42, 10, 3);

        // Assert
        Assert.That(song.LikeCount, Is.EqualTo(10));
        Assert.That(song.DislikeCount, Is.EqualTo(3));
        Assert.That(_viewModel.GetLikeCount(42), Is.EqualTo(10));
        Assert.That(_viewModel.GetDislikeCount(42), Is.EqualTo(3));
    }

    [Test]
    public void SignalR_LikeCountUpdate_IgnoresUnknownSong()
    {
        // Arrange
        var song = new SongDto { Id = 1, SongTitle = "Test", LikeCount = 5, DislikeCount = 2 };
        _viewModel.Songs.Add(song);

        // Act
        _mockSignalRService.Raise(s => s.OnLikeCountUpdated += null, 999, 20, 10);

        // Assert - original song unchanged
        Assert.That(song.LikeCount, Is.EqualTo(5));
        Assert.That(song.DislikeCount, Is.EqualTo(2));
    }

    // --- Stream count tracking ---

    [Test]
    public async Task LoadStreamQualifyingSecondsAsync_DelegatesToPlaybackService()
    {
        // Arrange
        _mockMusicService.Setup(s => s.GetStreamQualifyingSecondsAsync()).ReturnsAsync(45);

        // Act
        await _viewModel.LoadStreamQualifyingSecondsAsync();

        // Assert
        _mockPlaybackService.Verify(p => p.SetStreamQualifyingSeconds(45), Times.Once);
    }

    // --- Filtering ---

    private void LoadTestSongsDirectly()
    {
        // Simulate what LoadSongsAsync does internally, without async
        var songs = new List<SongDto>
        {
            new() { Id = 1, SongTitle = "Rock Anthem", ArtistName = "Alice", Genre = "Rock" },
            new() { Id = 2, SongTitle = "Pop Hit", ArtistName = "Bob", Genre = "Pop" },
            new() { Id = 3, SongTitle = "Rock Ballad", ArtistName = "Bob", Genre = "Rock" },
            new() { Id = 4, SongTitle = "Jazz Tune", ArtistName = "Charlie", Genre = "Jazz" },
            new() { Id = 5, SongTitle = "Pop Bop", ArtistName = "Alice", Genre = "Pop" },
        };

        _mockMusicService.Setup(s => s.GetSongsAsync()).ReturnsAsync(songs);
    }

    [Test]
    public async Task Filter_ByGenre_ShowsOnlyMatchingSongs()
    {
        LoadTestSongsDirectly();
        await _viewModel.LoadSongsCommand.ExecuteAsync(null);

        // Act
        _viewModel.ToggleGenreFilterCommand.Execute("Rock");

        // Assert
        Assert.That(_viewModel.Songs, Has.Count.EqualTo(2));
        Assert.That(_viewModel.Songs.All(s => s.Genre == "Rock"), Is.True);
    }

    [Test]
    public async Task Filter_ByArtist_ShowsOnlyMatchingSongs()
    {
        LoadTestSongsDirectly();
        await _viewModel.LoadSongsCommand.ExecuteAsync(null);

        // Act
        _viewModel.ToggleArtistFilterCommand.Execute("Bob");

        // Assert
        Assert.That(_viewModel.Songs, Has.Count.EqualTo(2));
        Assert.That(_viewModel.Songs.All(s => s.ArtistName == "Bob"), Is.True);
    }

    [Test]
    public async Task Filter_ByGenreAndArtist_ShowsIntersection()
    {
        LoadTestSongsDirectly();
        await _viewModel.LoadSongsCommand.ExecuteAsync(null);

        // Act
        _viewModel.ToggleGenreFilterCommand.Execute("Rock");
        _viewModel.ToggleArtistFilterCommand.Execute("Bob");

        // Assert
        Assert.That(_viewModel.Songs, Has.Count.EqualTo(1));
        Assert.That(_viewModel.Songs[0].SongTitle, Is.EqualTo("Rock Ballad"));
    }

    [Test]
    public async Task Filter_NoSelection_ShowsAllSongs()
    {
        LoadTestSongsDirectly();
        await _viewModel.LoadSongsCommand.ExecuteAsync(null);

        // Assert
        Assert.That(_viewModel.Songs, Has.Count.EqualTo(5));
    }

    [Test]
    public async Task ClearFilters_RestoresAllSongs()
    {
        LoadTestSongsDirectly();
        await _viewModel.LoadSongsCommand.ExecuteAsync(null);

        _viewModel.ToggleGenreFilterCommand.Execute("Jazz");
        Assert.That(_viewModel.Songs, Has.Count.EqualTo(1));

        // Act
        _viewModel.ClearFiltersCommand.Execute(null);

        // Assert
        Assert.That(_viewModel.Songs, Has.Count.EqualTo(5));
        Assert.That(_viewModel.SelectedGenres, Is.Empty);
        Assert.That(_viewModel.SelectedArtists, Is.Empty);
    }

    [Test]
    public async Task AvailableGenres_PopulatedAfterLoad()
    {
        LoadTestSongsDirectly();
        await _viewModel.LoadSongsCommand.ExecuteAsync(null);

        // Assert - sorted alphabetically
        Assert.That(_viewModel.AvailableGenres, Has.Count.EqualTo(3));
        Assert.That(_viewModel.AvailableGenres[0], Is.EqualTo("Jazz"));
        Assert.That(_viewModel.AvailableGenres[1], Is.EqualTo("Pop"));
        Assert.That(_viewModel.AvailableGenres[2], Is.EqualTo("Rock"));
    }

    [Test]
    public async Task AvailableArtists_PopulatedAfterLoad()
    {
        LoadTestSongsDirectly();
        await _viewModel.LoadSongsCommand.ExecuteAsync(null);

        // Assert - sorted alphabetically
        Assert.That(_viewModel.AvailableArtists, Has.Count.EqualTo(3));
        Assert.That(_viewModel.AvailableArtists[0], Is.EqualTo("Alice"));
        Assert.That(_viewModel.AvailableArtists[1], Is.EqualTo("Bob"));
        Assert.That(_viewModel.AvailableArtists[2], Is.EqualTo("Charlie"));
    }

    [Test]
    public async Task AvailableArtists_CrossFilteredByGenre()
    {
        LoadTestSongsDirectly();
        await _viewModel.LoadSongsCommand.ExecuteAsync(null);

        // Act - select Jazz genre
        _viewModel.ToggleGenreFilterCommand.Execute("Jazz");

        // Assert - only Charlie has Jazz songs
        Assert.That(_viewModel.AvailableArtists, Has.Count.EqualTo(1));
        Assert.That(_viewModel.AvailableArtists[0], Is.EqualTo("Charlie"));
    }

    [Test]
    public async Task AvailableGenres_CrossFilteredByArtist()
    {
        LoadTestSongsDirectly();
        await _viewModel.LoadSongsCommand.ExecuteAsync(null);

        // Act - select Alice
        _viewModel.ToggleArtistFilterCommand.Execute("Alice");

        // Assert - Alice has Pop and Rock
        Assert.That(_viewModel.AvailableGenres, Has.Count.EqualTo(2));
        Assert.That(_viewModel.AvailableGenres, Does.Contain("Pop"));
        Assert.That(_viewModel.AvailableGenres, Does.Contain("Rock"));
    }

    [Test]
    public async Task Filter_IsCaseInsensitive()
    {
        var songs = new List<SongDto>
        {
            new() { Id = 1, SongTitle = "Test", ArtistName = "alice", Genre = "rock" },
            new() { Id = 2, SongTitle = "Test2", ArtistName = "Alice", Genre = "Rock" },
        };
        _mockMusicService.Setup(s => s.GetSongsAsync()).ReturnsAsync(songs);
        await _viewModel.LoadSongsCommand.ExecuteAsync(null);

        // Act - filter with different case
        _viewModel.ToggleGenreFilterCommand.Execute("Rock");

        // Assert - both match
        Assert.That(_viewModel.Songs, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task LoadSongs_ResetsFilters()
    {
        LoadTestSongsDirectly();
        await _viewModel.LoadSongsCommand.ExecuteAsync(null);

        _viewModel.ToggleGenreFilterCommand.Execute("Jazz");
        Assert.That(_viewModel.Songs, Has.Count.EqualTo(1));

        // Act - reload songs
        await _viewModel.LoadSongsCommand.ExecuteAsync(null);

        // Assert - filters reset, all songs shown
        Assert.That(_viewModel.SelectedGenres, Is.Empty);
        Assert.That(_viewModel.SelectedArtists, Is.Empty);
        Assert.That(_viewModel.Songs, Has.Count.EqualTo(5));
    }

    [Test]
    public async Task Filter_NoMatch_ShowsEmptyCollection()
    {
        LoadTestSongsDirectly();
        await _viewModel.LoadSongsCommand.ExecuteAsync(null);

        // Act - select genre that won't combine with artist
        _viewModel.ToggleGenreFilterCommand.Execute("Jazz");
        _viewModel.ToggleArtistFilterCommand.Execute("Alice");

        // Assert - Alice has no Jazz songs
        Assert.That(_viewModel.Songs, Has.Count.EqualTo(0));
    }

    // --- Multi-select filter tests ---

    [Test]
    public async Task Filter_MultipleGenres_ShowsUnion()
    {
        LoadTestSongsDirectly();
        await _viewModel.LoadSongsCommand.ExecuteAsync(null);

        // Act - select both Rock and Jazz
        _viewModel.ToggleGenreFilterCommand.Execute("Rock");
        _viewModel.ToggleGenreFilterCommand.Execute("Jazz");

        // Assert - Rock (2) + Jazz (1) = 3
        Assert.That(_viewModel.Songs, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task Filter_ToggleRemovesSelection()
    {
        LoadTestSongsDirectly();
        await _viewModel.LoadSongsCommand.ExecuteAsync(null);

        // Act - select then deselect Rock
        _viewModel.ToggleGenreFilterCommand.Execute("Rock");
        Assert.That(_viewModel.Songs, Has.Count.EqualTo(2));

        _viewModel.ToggleGenreFilterCommand.Execute("Rock");

        // Assert - all songs shown again
        Assert.That(_viewModel.Songs, Has.Count.EqualTo(5));
        Assert.That(_viewModel.SelectedGenres, Is.Empty);
    }

    [Test]
    public async Task GenrePillText_ShowsCountWhenActive()
    {
        LoadTestSongsDirectly();
        await _viewModel.LoadSongsCommand.ExecuteAsync(null);

        Assert.That(_viewModel.GenrePillText, Is.EqualTo("Genre"));

        _viewModel.ToggleGenreFilterCommand.Execute("Rock");
        Assert.That(_viewModel.GenrePillText, Is.EqualTo("Genre (1)"));

        _viewModel.ToggleGenreFilterCommand.Execute("Jazz");
        Assert.That(_viewModel.GenrePillText, Is.EqualTo("Genre (2)"));
    }

    [Test]
    public async Task ArtistPillText_ShowsCountWhenActive()
    {
        LoadTestSongsDirectly();
        await _viewModel.LoadSongsCommand.ExecuteAsync(null);

        Assert.That(_viewModel.ArtistPillText, Is.EqualTo("Artist"));

        _viewModel.ToggleArtistFilterCommand.Execute("Alice");
        Assert.That(_viewModel.ArtistPillText, Is.EqualTo("Artist (1)"));
    }

    [Test]
    public async Task GenreFilterItems_ContainCountsAndSelectionState()
    {
        LoadTestSongsDirectly();
        await _viewModel.LoadSongsCommand.ExecuteAsync(null);

        // Open panel to populate items
        _viewModel.ToggleGenrePanelCommand.Execute(null);

        // Assert - 3 genres with counts
        Assert.That(_viewModel.GenreFilterItems, Has.Count.EqualTo(3));
        var jazz = _viewModel.GenreFilterItems.First(f => f.Name == "Jazz");
        var pop = _viewModel.GenreFilterItems.First(f => f.Name == "Pop");
        var rock = _viewModel.GenreFilterItems.First(f => f.Name == "Rock");

        Assert.That(jazz.Count, Is.EqualTo(1));
        Assert.That(pop.Count, Is.EqualTo(2));
        Assert.That(rock.Count, Is.EqualTo(2));
        Assert.That(jazz.IsSelected, Is.False);
    }

    [Test]
    public async Task GenreFilterItems_SearchFiltersItems()
    {
        LoadTestSongsDirectly();
        await _viewModel.LoadSongsCommand.ExecuteAsync(null);

        _viewModel.ToggleGenrePanelCommand.Execute(null);

        // Act - search for "ro"
        _viewModel.GenreSearchText = "ro";

        // Assert - only Rock matches
        Assert.That(_viewModel.GenreFilterItems, Has.Count.EqualTo(1));
        Assert.That(_viewModel.GenreFilterItems[0].Name, Is.EqualTo("Rock"));
    }

    [Test]
    public void ToggleGenrePanel_ClosesArtistPanel()
    {
        _viewModel.IsArtistPanelOpen = true;

        // Act
        _viewModel.ToggleGenrePanelCommand.Execute(null);

        // Assert
        Assert.That(_viewModel.IsGenrePanelOpen, Is.True);
        Assert.That(_viewModel.IsArtistPanelOpen, Is.False);
    }

    [Test]
    public void ToggleArtistPanel_ClosesGenrePanel()
    {
        _viewModel.IsGenrePanelOpen = true;

        // Act
        _viewModel.ToggleArtistPanelCommand.Execute(null);

        // Assert
        Assert.That(_viewModel.IsArtistPanelOpen, Is.True);
        Assert.That(_viewModel.IsGenrePanelOpen, Is.False);
    }

    // --- Auth-dependent like/dislike ---

    [Test]
    public async Task LikeSong_WhenNotLoggedIn_ShowsLoginPrompt()
    {
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(false);
        var song = new SongDto { Id = 1, SongTitle = "Test" };

        await _viewModel.LikeSongCommand.ExecuteAsync(song);

        _mockAlertService.Verify(a => a.ShowConfirmAsync(
            "Login Required", It.IsAny<string>(), "Login", "Cancel"), Times.Once);
        _mockMusicService.Verify(s => s.ToggleLikeAsync(It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task DislikeSong_WhenNotLoggedIn_ShowsLoginPrompt()
    {
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(false);
        var song = new SongDto { Id = 1, SongTitle = "Test" };

        await _viewModel.DislikeSongCommand.ExecuteAsync(song);

        _mockAlertService.Verify(a => a.ShowConfirmAsync(
            "Login Required", It.IsAny<string>(), "Login", "Cancel"), Times.Once);
        _mockMusicService.Verify(s => s.ToggleDislikeAsync(It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task LikeSong_WhenLoggedInButEmailNotConfirmed_ShowsVerifyAlert()
    {
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(true);
        _mockAuthService.Setup(a => a.EmailConfirmed).Returns(false);
        var song = new SongDto { Id = 1, SongTitle = "Test" };

        await _viewModel.LikeSongCommand.ExecuteAsync(song);

        _mockAlertService.Verify(a => a.DisplayAlertAsync(
            "Email Not Verified", It.IsAny<string>(), "OK"), Times.Once);
        _mockMusicService.Verify(s => s.ToggleLikeAsync(It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task LikeSong_WhenAuthenticatedUser_CallsToggleLike()
    {
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(true);
        _mockAuthService.Setup(a => a.EmailConfirmed).Returns(true);
        var song = new SongDto { Id = 42, SongTitle = "Test" };

        await _viewModel.LikeSongCommand.ExecuteAsync(song);

        _mockMusicService.Verify(s => s.ToggleLikeAsync(42), Times.Once);
    }

    [Test]
    public async Task DislikeSong_WhenAuthenticatedUser_CallsToggleDislike()
    {
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(true);
        _mockAuthService.Setup(a => a.EmailConfirmed).Returns(true);
        var song = new SongDto { Id = 42, SongTitle = "Test" };

        await _viewModel.DislikeSongCommand.ExecuteAsync(song);

        _mockMusicService.Verify(s => s.ToggleDislikeAsync(42), Times.Once);
    }

    // --- Playback restriction (now in PlaybackService) ---
    // Preview limit tests have been moved to PlaybackServiceTests

    [Test]
    public async Task OpenSong_NavigatesToSongPlayer()
    {
        // Arrange
        var song = new SongDto { Id = 1, SongTitle = "Test" };

        // Act
        await _viewModel.OpenSongCommand.ExecuteAsync(song);

        // Assert
        _mockNavigationService.Verify(n => n.GoToAsync("song-player",
            It.Is<Dictionary<string, object>>(d => d.ContainsKey("Song") && d["Song"] == song)),
            Times.Once);
    }

    [Test]
    public async Task OpenSong_NullSong_DoesNotNavigate()
    {
        // Act
        await _viewModel.OpenSongCommand.ExecuteAsync(null);

        // Assert
        _mockNavigationService.Verify(n => n.GoToAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()), Times.Never);
    }
}
