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
    private Mock<IMediaPlaybackOnboardingService> _mockMediaPlaybackOnboardingService;
    private Mock<IAppConfig> _mockAppConfig;
    private Mock<IBillingService> _mockBillingService;
    private Mock<IAudioCacheService> _mockAudioCacheService;
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
        _mockMediaPlaybackOnboardingService = new Mock<IMediaPlaybackOnboardingService>();
        _mockAppConfig = new Mock<IAppConfig>();
        _mockBillingService = new Mock<IBillingService>();
        _mockAudioCacheService = new Mock<IAudioCacheService>();
        _mockAppConfig.Setup(c => c.WebBaseUrl).Returns("https://streamtunes.net");
        _mockAppConfig.Setup(c => c.ApiBaseUrl).Returns("https://streamtunes.net");
        _mockMediaPlaybackOnboardingService.Setup(s => s.EnsureBackgroundPlaybackExplainedAsync()).Returns(Task.CompletedTask);
        _mockAudioCacheService
            .Setup(service => service.GetCacheStatusesAsync(
                It.IsAny<IReadOnlyList<SongDto>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<int, TrackCacheStatus>());
        _viewModel = new MusicLibraryViewModel(
            _mockMusicService.Object, _mockAlertService.Object, _mockSignalRService.Object,
            _mockAuthService.Object, _mockNavigationService.Object,
            _mockPlaybackService.Object, _mockMediaPlaybackOnboardingService.Object, _mockAppConfig.Object,
            _mockBillingService.Object, _mockAudioCacheService.Object);
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
        Assert.That(_viewModel.Songs.Select(song => song.SongTitle), Is.EqualTo(new[] { "Song Two", "Song One" }));
    }

    [Test]
    public async Task LoadSongsAsync_OrdersSongsByDisplayOrderForLibrary()
    {
        // Arrange
        var songs = new List<SongDto>
        {
            new() { Id = 10, SongTitle = "Ranked One", DisplayOrder = 1 },
            new() { Id = 40, SongTitle = "Ranked Two", DisplayOrder = 2 },
            new() { Id = 30, SongTitle = "Null Newest", DisplayOrder = null },
            new() { Id = 20, SongTitle = "Null Older", DisplayOrder = null }
        };
        _mockMusicService.Setup(s => s.GetSongsAsync()).ReturnsAsync(songs);

        // Act
        await _viewModel.LoadSongsCommand.ExecuteAsync(null);

        // Assert
        Assert.That(_viewModel.Songs.Select(song => song.SongTitle), Is.EqualTo(new[]
        {
            "Null Newest",
            "Null Older",
            "Ranked One",
            "Ranked Two"
        }));
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
    public async Task LoadSongsAsync_WhenCacheStatusScanThrows_StillLoadsLibraryWithoutError()
    {
        var songs = new List<SongDto>
        {
            new() { Id = 1, SongTitle = "Song One" },
            new() { Id = 2, SongTitle = "Song Two" }
        };
        _mockMusicService.Setup(s => s.GetSongsAsync()).ReturnsAsync(songs);
        _mockAudioCacheService
            .Setup(service => service.GetCacheStatusesAsync(
                It.IsAny<IReadOnlyList<SongDto>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("cache faulted"));

        // A faulted downloaded-status scan must not abort the library load (or throw out of the command).
        await _viewModel.LoadSongsCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.Songs, Has.Count.EqualTo(2));
            Assert.That(_viewModel.ErrorMessage, Is.Null.Or.Empty);
        });
    }

    [Test]
    public async Task LoadSongsAsync_SetsErrorMessageFromMusicService_WhenSongsRequestFails()
    {
        _mockMusicService.Setup(s => s.GetSongsAsync()).ReturnsAsync([]);
        _mockMusicService.SetupGet(s => s.LastSongsError).Returns("Request to https://davidtest.dev/api/music/songs failed (500).");

        await _viewModel.LoadSongsCommand.ExecuteAsync(null);

        Assert.That(_viewModel.ErrorMessage, Does.Contain("https://davidtest.dev/api/music/songs"));
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
    public async Task SelectAiFilter_AiMusicOnly_FiltersSongs()
    {
        var songs = new List<SongDto>
        {
            new() { Id = 1, SongTitle = "AI Song", IsAiGenerated = true },
            new() { Id = 2, SongTitle = "Human Song", IsAiGenerated = false },
            new() { Id = 3, SongTitle = "Another AI Song", IsAiGenerated = true }
        };
        _mockMusicService.Setup(s => s.GetSongsAsync()).ReturnsAsync(songs);

        await _viewModel.LoadSongsCommand.ExecuteAsync(null);

        _viewModel.SelectAiFilterCommand.Execute("AiMusic");

        Assert.That(_viewModel.Songs.Select(s => s.SongTitle), Is.EqualTo(new[] { "Another AI Song", "AI Song" }));
        Assert.That(_viewModel.IsAiMusicFilterSelected, Is.True);
        Assert.That(_viewModel.IsAllAiFilterSelected, Is.False);
    }

    [Test]
    public async Task SelectAiFilter_AnyAndIndividualAiChoices_FilterCorrectly()
    {
        var songs = new List<SongDto>
        {
            new() { Id = 1, SongTitle = "AI Music", IsAiGenerated = true },
            new() { Id = 2, SongTitle = "AI Vocals", IsAiVocals = true },
            new() { Id = 3, SongTitle = "AI Lyrics", IsAiLyrics = true },
            new() { Id = 4, SongTitle = "Human Song" }
        };
        _mockMusicService.Setup(s => s.GetSongsAsync()).ReturnsAsync(songs);

        await _viewModel.LoadSongsCommand.ExecuteAsync(null);

        _viewModel.SelectAiFilterCommand.Execute("AnyAi");
        Assert.That(_viewModel.Songs.Select(s => s.SongTitle), Is.EqualTo(new[] { "AI Lyrics", "AI Vocals", "AI Music" }));
        Assert.That(_viewModel.IsAnyAiFilterSelected, Is.True);
        Assert.That(_viewModel.IsNonAiOnlyFilterSelected, Is.False);

        _viewModel.SelectAiFilterCommand.Execute("AiVocals");
        Assert.That(_viewModel.Songs.Select(s => s.SongTitle), Is.EqualTo(new[] { "AI Vocals" }));
        Assert.That(_viewModel.IsAiVocalsFilterSelected, Is.True);
        Assert.That(_viewModel.IsAnyAiFilterSelected, Is.False);

        _viewModel.SelectAiFilterCommand.Execute("AiLyrics");
        Assert.That(_viewModel.Songs.Select(s => s.SongTitle), Is.EqualTo(new[] { "AI Lyrics" }));
        Assert.That(_viewModel.IsAiLyricsFilterSelected, Is.True);
    }

    [Test]
    public async Task SelectAiFilter_NonAi_ExcludesAllAiDisclosureTypes()
    {
        var songs = new List<SongDto>
        {
            new() { Id = 1, SongTitle = "AI Music", IsAiGenerated = true },
            new() { Id = 2, SongTitle = "AI Vocals", IsAiVocals = true },
            new() { Id = 3, SongTitle = "AI Lyrics", IsAiLyrics = true },
            new() { Id = 4, SongTitle = "Human Song" }
        };
        _mockMusicService.Setup(s => s.GetSongsAsync()).ReturnsAsync(songs);

        await _viewModel.LoadSongsCommand.ExecuteAsync(null);

        _viewModel.SelectAiFilterCommand.Execute("NonAiOnly");

        Assert.That(_viewModel.Songs.Select(s => s.SongTitle), Is.EqualTo(new[] { "Human Song" }));
        Assert.That(_viewModel.IsNonAiOnlyFilterSelected, Is.True);
        Assert.That(_viewModel.IsAnyAiFilterSelected, Is.False);
        Assert.That(_viewModel.IsAiMusicFilterSelected, Is.False);
    }

    [Test]
    public async Task ClearFilters_ResetsAiFilterToAll()
    {
        var songs = new List<SongDto>
        {
            new() { Id = 1, SongTitle = "AI Song", IsAiGenerated = true },
            new() { Id = 2, SongTitle = "Human Song", IsAiGenerated = false }
        };
        _mockMusicService.Setup(s => s.GetSongsAsync()).ReturnsAsync(songs);

        await _viewModel.LoadSongsCommand.ExecuteAsync(null);
        _viewModel.SelectAiFilterCommand.Execute("AiVocals");

        _viewModel.ClearFiltersCommand.Execute(null);

        Assert.That(_viewModel.IsAllAiFilterSelected, Is.True);
        Assert.That(_viewModel.IsAiVocalsFilterSelected, Is.False);
        Assert.That(_viewModel.Songs, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task SelectAiFilter_UpdatesAiPillTextAndActiveState()
    {
        _mockMusicService.Setup(s => s.GetSongsAsync()).ReturnsAsync(new List<SongDto>
        {
            new() { Id = 1, SongTitle = "AI Song", IsAiGenerated = true },
            new() { Id = 2, SongTitle = "Human Song", IsAiGenerated = false }
        });

        await _viewModel.LoadSongsCommand.ExecuteAsync(null);

        Assert.That(_viewModel.AiPillText, Is.EqualTo("Music Type"));
        Assert.That(_viewModel.HasActiveAiFilter, Is.False);

        _viewModel.SelectAiFilterCommand.Execute("AnyAi");

        Assert.That(_viewModel.AiPillText, Is.EqualTo("Any AI"));
        Assert.That(_viewModel.HasActiveAiFilter, Is.True);
        Assert.That(_viewModel.HasAnyActiveFilters, Is.True);
        Assert.That(_viewModel.IsAnyAiFilterSelected, Is.True);

        _viewModel.SelectAiFilterCommand.Execute("AiMusic");

        Assert.That(_viewModel.AiPillText, Is.EqualTo("AI Music"));
        Assert.That(_viewModel.HasActiveAiFilter, Is.True);
        Assert.That(_viewModel.HasAnyActiveFilters, Is.True);
        Assert.That(_viewModel.IsAiMusicFilterSelected, Is.True);
        Assert.That(_viewModel.IsAnyAiFilterSelected, Is.False);
    }

    [Test]
    public void HasAnyActiveFilters_IsFalseByDefault()
    {
        Assert.That(_viewModel.HasAnyActiveFilters, Is.False);
    }

    [Test]
    public void ToggleAiPanel_ClosesOtherPanels()
    {
        _viewModel.ToggleGenrePanelCommand.Execute(null);
        Assert.That(_viewModel.IsGenrePanelOpen, Is.True);

        _viewModel.ToggleAiPanelCommand.Execute(null);

        Assert.That(_viewModel.IsAiPanelOpen, Is.True);
        Assert.That(_viewModel.IsGenrePanelOpen, Is.False);
        Assert.That(_viewModel.IsArtistPanelOpen, Is.False);
    }

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

        var method = typeof(MusicLibraryViewModel).GetMethod(
            "OnShowSubscribeCta",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var task = (Task)method!.Invoke(_viewModel, null)!;
        await task;

        _mockAlertService.Verify(a => a.DisplayAlertAsync("Subscribe",
            It.Is<string>(s => s.Contains("Configured Google Play service account key file was not found on the server.")),
            "OK"), Times.Once);
    }

    [Test]
    public async Task Cleanup_UnsubscribesFromPlaybackSubscribeCta()
    {
        _viewModel.Cleanup();

        _mockPlaybackService.Raise(p => p.ShowSubscribeCtaRequested += null);
        await Task.Delay(50);

        _mockAlertService.Verify(a => a.ShowConfirmAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task Activate_ReattachesPlaybackSubscribeCta_AfterCleanup()
    {
        _viewModel.Cleanup();
        _viewModel.Activate();
        _mockAlertService.Setup(a => a.ShowConfirmAsync("Preview Limit", It.IsAny<string>(), "Subscribe Now", "Not Now"))
            .ReturnsAsync(false);

        _mockPlaybackService.Raise(p => p.ShowSubscribeCtaRequested += null);
        await Task.Delay(50);

        _mockAlertService.Verify(a => a.ShowConfirmAsync("Preview Limit", It.IsAny<string>(), "Subscribe Now", "Not Now"), Times.Once);
    }

    [Test]
    public async Task PlaySong_SetsPlaylistOnPlaybackService()
    {
        // Arrange
        var song = new SongDto { Id = 1, SongTitle = "Test", StreamUrl = "https://example.com/test.mp3" };
        _viewModel.Songs.Add(song);

        // Act
        await _viewModel.PlaySongCommand.ExecuteAsync(song);

        // Assert — now uses SetPlaylist instead of PlaySong
        _mockPlaybackService.Verify(p => p.SetPlaylist(
            It.Is<List<SongDto>>(l => l.Count == 1 && l[0] == song),
            0,
            "Unfiltered media library"), Times.Once);
        _mockMediaPlaybackOnboardingService.Verify(s => s.EnsureBackgroundPlaybackExplainedAsync(), Times.Once);
    }

    [Test]
    public async Task PlaySong_WhenSongMatchesCurrentPlayback_TogglesPlayPause()
    {
        var song = new SongDto { Id = 1, SongTitle = "Test", StreamUrl = "https://example.com/test.mp3" };
        _viewModel.Songs.Add(song);
        _mockPlaybackService.SetupGet(p => p.CurrentSong).Returns(song);
        _mockPlaybackService.SetupGet(p => p.PreviewLimitReached).Returns(false);

        await _viewModel.PlaySongCommand.ExecuteAsync(song);

        _mockPlaybackService.Verify(p => p.TogglePlayPause(), Times.Once);
        _mockPlaybackService.Verify(p => p.SetPlaylist(It.IsAny<List<SongDto>>(), It.IsAny<int>()), Times.Never);
        _mockMediaPlaybackOnboardingService.Verify(s => s.EnsureBackgroundPlaybackExplainedAsync(), Times.Never);
    }

    [Test]
    public async Task PlayVisibleQueueFromStartAsync_UsesCurrentFilteredSongs()
    {
        var songs = new List<SongDto>
        {
            new() { Id = 1, SongTitle = "Rock A", Genre = "Rock", ArtistName = "A" },
            new() { Id = 2, SongTitle = "Pop B", Genre = "Pop", ArtistName = "B" },
            new() { Id = 3, SongTitle = "Rock C", Genre = "Rock", ArtistName = "C" }
        };

        _mockMusicService.Setup(service => service.GetSongsAsync()).ReturnsAsync(songs);

        await _viewModel.LoadSongsCommand.ExecuteAsync(null);
        _viewModel.ToggleGenreFilterCommand.Execute("Rock");

        var started = await _viewModel.PlayVisibleQueueFromStartAsync();

        Assert.That(started, Is.True);
        _mockPlaybackService.Verify(service => service.SetPlaylist(
            It.Is<List<SongDto>>(playlist => playlist.Select(song => song.Id).SequenceEqual(new[] { 3, 1 })),
            0,
            "Filtered media library (Genres: Rock)"), Times.Once);
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
        Assert.That(_viewModel.Songs.Single(song => song.Id == 1).LikeCount, Is.EqualTo(5));
        Assert.That(_viewModel.Songs.Single(song => song.Id == 2).LikeCount, Is.EqualTo(10));
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
    public void MusicService_StreamCountRecorded_UpdatesSongDto()
    {
        var song = new SongDto { Id = 42, SongTitle = "Test", StreamCount = 10 };
        _viewModel.Songs.Add(song);

        _mockMusicService.Raise(s => s.OnStreamCountRecorded += null, 42, 15);

        Assert.That(song.StreamCount, Is.EqualTo(15));
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
        Assert.That(_viewModel.HasAnyActiveFilters, Is.False);
    }

    [Test]
    public async Task DownloadedFilter_IntersectsExistingFiltersAndClearRestoresAllSongs()
    {
        var songs = new List<SongDto>
        {
            new() { Id = 1, SongTitle = "Downloaded Rock", Genre = "Rock" },
            new() { Id = 2, SongTitle = "Online Rock", Genre = "Rock" },
            new() { Id = 3, SongTitle = "Downloaded Pop", Genre = "Pop" }
        };
        _mockMusicService.Setup(service => service.GetSongsAsync()).ReturnsAsync(songs);
        _mockAudioCacheService
            .Setup(service => service.GetCacheStatusesAsync(
                It.Is<IReadOnlyList<SongDto>>(items => items.Count == 3),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<int, TrackCacheStatus>
            {
                [1] = new(1, "song-1", "cached-1.mp3", true, false),
                [2] = new(2, "song-2", null, false, false),
                [3] = new(3, "song-3", "cached-3.mp3", true, false)
            });

        await _viewModel.LoadSongsCommand.ExecuteAsync(null);
        await _viewModel.ToggleDownloadedFilterCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.IsDownloadedFilterActive, Is.True);
            Assert.That(_viewModel.HasAnyActiveFilters, Is.True);
            Assert.That(_viewModel.Songs.Select(song => song.Id), Is.EqualTo(new[] { 3, 1 }));
        });

        _viewModel.ToggleGenreFilterCommand.Execute("Rock");
        Assert.That(_viewModel.Songs.Select(song => song.Id), Is.EqualTo(new[] { 1 }));

        _viewModel.ClearFiltersCommand.Execute(null);
        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.IsDownloadedFilterActive, Is.False);
            Assert.That(_viewModel.HasAnyActiveFilters, Is.False);
            Assert.That(_viewModel.Songs.Select(song => song.Id), Is.EqualTo(new[] { 3, 2, 1 }));
        });

        _mockAudioCacheService.Verify(service => service.GetCacheStatusesAsync(
            It.Is<IReadOnlyList<SongDto>>(items => items.Select(song => song.Id).Order().SequenceEqual(new[] { 1, 2, 3 })),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
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
    public async Task LikeSong_WhenNotLoggedInAndPromptAccepted_NavigatesToAnchoredLoginEntry()
    {
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(false);
        _mockAlertService.Setup(a => a.ShowConfirmAsync(
            "Login Required", It.IsAny<string>(), "Login", "Cancel"))
            .ReturnsAsync(true);
        var song = new SongDto { Id = 1, SongTitle = "Test" };

        await _viewModel.LikeSongCommand.ExecuteAsync(song);

        _mockNavigationService.Verify(n => n.GoToAsync(NavigationRoutes.LoginEntry), Times.Once);
        _mockNavigationService.Verify(n => n.GoToAsync(NavigationRoutes.Login), Times.Never);
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

    // --- Report Song Tests ---

    [Test]
    public async Task ReportSong_WhenNotLoggedIn_ShowsLoginPrompt()
    {
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(false);
        var song = new SongDto { Id = 1, SongTitle = "Test" };

        await _viewModel.ReportSongCommand.ExecuteAsync(song);

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
        var song = new SongDto { Id = 1, SongTitle = "Test" };

        await _viewModel.ReportSongCommand.ExecuteAsync(song);

        _mockAlertService.Verify(a => a.DisplayAlertAsync(
            "Not Authorized", It.IsAny<string>(), "OK"), Times.Once);
        _mockMusicService.Verify(s => s.ReportSongAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task ReportSong_WhenValidatedUser_ShowsActionSheet()
    {
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(true);
        _mockAuthService.Setup(a => a.EmailConfirmed).Returns(true);
        _mockAuthService.Setup(a => a.Roles).Returns(new List<string> { "User" });
        _mockAlertService.Setup(a => a.ShowActionSheetAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string[]>()))
            .ReturnsAsync("Copyright Violation");
        _mockMusicService.Setup(s => s.ReportSongAsync(42, "Copyright Violation")).ReturnsAsync(true);
        var song = new SongDto { Id = 42, SongTitle = "Test" };

        await _viewModel.ReportSongCommand.ExecuteAsync(song);

        _mockMusicService.Verify(s => s.ReportSongAsync(42, "Copyright Violation"), Times.Once);
        _mockAlertService.Verify(a => a.DisplayAlertAsync("Report Submitted", It.IsAny<string>(), "OK"), Times.Once);
    }

    [Test]
    public async Task ReportSong_WhenCancelled_DoesNotCallService()
    {
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(true);
        _mockAuthService.Setup(a => a.EmailConfirmed).Returns(true);
        _mockAuthService.Setup(a => a.Roles).Returns(new List<string> { "User" });
        _mockAlertService.Setup(a => a.ShowActionSheetAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string[]>()))
            .ReturnsAsync("Cancel");
        var song = new SongDto { Id = 42, SongTitle = "Test" };

        await _viewModel.ReportSongCommand.ExecuteAsync(song);

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
        var song = new SongDto { Id = 42, SongTitle = "Test" };

        await _viewModel.ReportSongCommand.ExecuteAsync(song);

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
    }

    [Test]
    public async Task NavigateToArtist_NavigatesToPlaylistPlayer()
    {
        await _viewModel.NavigateToArtistCommand.ExecuteAsync("Band A");

        _mockNavigationService.Verify(n =>
            n.GoToAsync("playlist-player", It.Is<IDictionary<string, object>>(d =>
                d.ContainsKey("ArtistName") && (string)d["ArtistName"] == "Band A")),
            Times.Once);
    }

    [Test]
    public async Task NavigateToGenre_Null_DoesNothing()
    {
        await _viewModel.NavigateToGenreCommand.ExecuteAsync(null);

        _mockNavigationService.Verify(n =>
            n.GoToAsync(It.IsAny<string>(), It.IsAny<IDictionary<string, object>>()),
            Times.Never);
    }

    // --- Play as Playlist ---

    [Test]
    public async Task PlaySong_SetsPlaylistWithUnfilteredLibrarySongs()
    {
        var songs = new List<SongDto>
        {
            new() { Id = 1, SongTitle = "Song A", Genre = "Rock", ArtistName = "A" },
            new() { Id = 2, SongTitle = "Song B", Genre = "Pop", ArtistName = "B" },
            new() { Id = 3, SongTitle = "Song C", Genre = "Rock", ArtistName = "C" },
        };

        // Simulate loading songs into the ViewModel
        _mockMusicService.Setup(s => s.GetSongsAsync()).ReturnsAsync(songs);
        _viewModel.LoadSongsCommand.Execute(null);

        // After loading, all 3 songs are in Songs collection
        // Play the second song
        await _viewModel.PlaySongCommand.ExecuteAsync(songs[1]);

        _mockPlaybackService.Verify(p =>
            p.SetPlaylist(
                It.Is<List<SongDto>>(l => l.Count == 3),
                1,
                "Unfiltered media library"), Times.Once);
        _mockMediaPlaybackOnboardingService.Verify(s => s.EnsureBackgroundPlaybackExplainedAsync(), Times.Once);
    }

    [Test]
    public async Task PlaySong_WithActiveGenreFilter_SetsPlaylistWithOnlyFilteredSongs()
    {
        var songs = new List<SongDto>
        {
            new() { Id = 1, SongTitle = "Song A", Genre = "Rock", ArtistName = "A" },
            new() { Id = 2, SongTitle = "Song B", Genre = "Pop", ArtistName = "B" },
            new() { Id = 3, SongTitle = "Song C", Genre = "Rock", ArtistName = "C" },
        };

        _mockMusicService.Setup(s => s.GetSongsAsync()).ReturnsAsync(songs);
        await _viewModel.LoadSongsCommand.ExecuteAsync(null);
        _viewModel.ToggleGenreFilterCommand.Execute("Rock");

        await _viewModel.PlaySongCommand.ExecuteAsync(songs[0]);

        _mockPlaybackService.Verify(p =>
            p.SetPlaylist(
                It.Is<List<SongDto>>(l => l.Select(song => song.Id).SequenceEqual(new[] { 3, 1 })),
                1,
                "Filtered media library (Genres: Rock)"), Times.Once);
        _mockPlaybackService.Verify(p =>
            p.SetPlaylist(
                It.Is<List<SongDto>>(l => l.Any(song => song.Id == 2)),
                It.IsAny<int>(),
                It.IsAny<string>()), Times.Never);
        _mockMediaPlaybackOnboardingService.Verify(s => s.EnsureBackgroundPlaybackExplainedAsync(), Times.Once);
    }

    [Test]
    public async Task LoadSongsAsync_WithActiveDifferentPlaylist_SetsQueueToAllLibrarySongs()
    {
        var songs = new List<SongDto>
        {
            new() { Id = 1, SongTitle = "Song A", Genre = "Rock", ArtistName = "A" },
            new() { Id = 2, SongTitle = "Song B", Genre = "Pop", ArtistName = "B" },
            new() { Id = 3, SongTitle = "Song C", Genre = "Jazz", ArtistName = "C" }
        };

        _mockMusicService.Setup(s => s.GetSongsAsync()).ReturnsAsync(songs);
        _mockPlaybackService.SetupGet(p => p.HasPlaylist).Returns(true);
        _mockPlaybackService.SetupGet(p => p.IsPlaying).Returns(true);
        _mockPlaybackService.SetupGet(p => p.CurrentSong).Returns(songs[1]);
        _mockPlaybackService.SetupGet(p => p.Playlist).Returns(new List<SongDto>
        {
            songs[1],
            new() { Id = 99, SongTitle = "Playlist Only Song" }
        });

        await _viewModel.LoadSongsCommand.ExecuteAsync(null);

        _mockPlaybackService.Verify(p =>
            p.SetPlaylist(
                It.Is<List<SongDto>>(l => l.Select(song => song.Id).SequenceEqual(new[] { 3, 2, 1 })),
                1,
                PlaybackQueueStartBehavior.PreserveCurrentSongIfPresent,
                "Unfiltered media library"),
            Times.Once);
    }

    [Test]
    public async Task Activate_WithFilteredLibrarySongs_SetsQueueToFilteredSongs()
    {
        var songs = new List<SongDto>
        {
            new() { Id = 1, SongTitle = "Song A", Genre = "Rock", ArtistName = "A" },
            new() { Id = 2, SongTitle = "Song B", Genre = "Pop", ArtistName = "B" },
            new() { Id = 3, SongTitle = "Song C", Genre = "Rock", ArtistName = "C" }
        };

        _mockMusicService.Setup(s => s.GetSongsAsync()).ReturnsAsync(songs);
        await _viewModel.LoadSongsCommand.ExecuteAsync(null);
        _mockPlaybackService.Invocations.Clear();

        _viewModel.ToggleGenreFilterCommand.Execute("Rock");

        _mockPlaybackService.SetupGet(p => p.HasPlaylist).Returns(true);
        _mockPlaybackService.SetupGet(p => p.IsPlaying).Returns(true);
        _mockPlaybackService.SetupGet(p => p.CurrentSong).Returns(songs[2]);
        _mockPlaybackService.SetupGet(p => p.Playlist).Returns(new List<SongDto>
        {
            songs[1],
            songs[2]
        });

        _viewModel.Activate();

        _mockPlaybackService.Verify(p =>
            p.SetPlaylist(
                It.Is<List<SongDto>>(l => l.Select(song => song.Id).SequenceEqual(new[] { 3, 1 })),
                0,
                PlaybackQueueStartBehavior.PreserveCurrentSongIfPresent,
                "Filtered media library (Genres: Rock)"),
            Times.Once);
    }

    [Test]
    public async Task Activate_WithEquivalentVisibleQueue_DoesNotResetPlaylist()
    {
        var songs = new List<SongDto>
        {
            new() { Id = 1, SongTitle = "Song A", Genre = "Rock", ArtistName = "A" },
            new() { Id = 2, SongTitle = "Song B", Genre = "Pop", ArtistName = "B" }
        };

        _mockMusicService.Setup(s => s.GetSongsAsync()).ReturnsAsync(songs);
        await _viewModel.LoadSongsCommand.ExecuteAsync(null);
        _mockPlaybackService.Invocations.Clear();

        _mockPlaybackService.SetupGet(p => p.HasPlaylist).Returns(true);
        _mockPlaybackService.SetupGet(p => p.IsPlaying).Returns(true);
        _mockPlaybackService.SetupGet(p => p.CurrentSong).Returns(songs[0]);
        _mockPlaybackService.SetupGet(p => p.Playlist).Returns(new List<SongDto>
        {
            new() { Id = 2, SongTitle = "Queue Song B" },
            new() { Id = 1, SongTitle = "Queue Song A" }
        });

        _viewModel.Activate();

        _mockPlaybackService.Verify(p => p.SetPlaylist(It.IsAny<List<SongDto>>(), It.IsAny<int>()), Times.Never);
        _mockPlaybackService.Verify(p => p.SetPlaylist(
            It.IsAny<List<SongDto>>(),
            It.IsAny<int>(),
            It.IsAny<PlaybackQueueStartBehavior>()), Times.Never);
    }
}
