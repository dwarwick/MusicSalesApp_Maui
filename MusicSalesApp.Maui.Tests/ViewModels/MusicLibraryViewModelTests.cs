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
    private MusicLibraryViewModel _viewModel;

    [SetUp]
    public void Setup()
    {
        _mockMusicService = new Mock<IMusicService>();
        _mockAlertService = new Mock<IAlertService>();
        _mockSignalRService = new Mock<ISignalRService>();
        _viewModel = new MusicLibraryViewModel(
            _mockMusicService.Object, _mockAlertService.Object, _mockSignalRService.Object);
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
    public void PlaySong_SetsCurrentlyPlayingSongAndIsPlaying()
    {
        // Arrange
        var song = new SongDto { Id = 1, SongTitle = "Test" };

        // Act
        _viewModel.PlaySongCommand.Execute(song);

        // Assert
        Assert.That(_viewModel.CurrentlyPlayingSong, Is.SameAs(song));
        Assert.That(_viewModel.IsPlaying, Is.True);
    }

    [Test]
    public void PlaySong_TappingSameSongPauses()
    {
        // Arrange
        var song = new SongDto { Id = 1, SongTitle = "Test" };
        _viewModel.PlaySongCommand.Execute(song);
        Assert.That(_viewModel.IsPlaying, Is.True);

        // Act — tap same song again
        _viewModel.PlaySongCommand.Execute(song);

        // Assert
        Assert.That(_viewModel.IsPlaying, Is.False);
    }

    [Test]
    public void PlaySong_SwitchingToNewSongStartsPlaying()
    {
        // Arrange
        var song1 = new SongDto { Id = 1, SongTitle = "First" };
        var song2 = new SongDto { Id = 2, SongTitle = "Second" };
        _viewModel.PlaySongCommand.Execute(song1);

        // Act
        _viewModel.PlaySongCommand.Execute(song2);

        // Assert
        Assert.That(_viewModel.CurrentlyPlayingSong, Is.SameAs(song2));
        Assert.That(_viewModel.IsPlaying, Is.True);
    }

    [Test]
    public void TogglePlayPause_TogglesIsPlaying()
    {
        // Arrange
        var song = new SongDto { Id = 1, SongTitle = "Test" };
        _viewModel.PlaySongCommand.Execute(song);
        Assert.That(_viewModel.IsPlaying, Is.True);

        // Act
        _viewModel.TogglePlayPauseCommand.Execute(null);

        // Assert
        Assert.That(_viewModel.IsPlaying, Is.False);

        // Act again
        _viewModel.TogglePlayPauseCommand.Execute(null);

        // Assert
        Assert.That(_viewModel.IsPlaying, Is.True);
    }

    [Test]
    public void TogglePlayPause_DoesNothingWhenNoSong()
    {
        // Act
        _viewModel.TogglePlayPauseCommand.Execute(null);

        // Assert
        Assert.That(_viewModel.IsPlaying, Is.False);
        Assert.That(_viewModel.CurrentlyPlayingSong, Is.Null);
    }

    [Test]
    public void Stop_ClearsPlaybackState()
    {
        // Arrange
        var song = new SongDto { Id = 1, SongTitle = "Test" };
        _viewModel.PlaySongCommand.Execute(song);

        // Act
        _viewModel.StopCommand.Execute(null);

        // Assert
        Assert.That(_viewModel.IsPlaying, Is.False);
        Assert.That(_viewModel.CurrentlyPlayingSong, Is.Null);
    }

    [Test]
    public void FormatDuration_FormatsMinutesAndSeconds()
    {
        Assert.That(_viewModel.FormatDuration(65), Is.EqualTo("1:05"));
        Assert.That(_viewModel.FormatDuration(180.5), Is.EqualTo("3:00"));
        Assert.That(_viewModel.FormatDuration(3661), Is.EqualTo("1:01:01"));
    }

    [Test]
    public void FormatDuration_ReturnsPlaceholderForNull()
    {
        Assert.That(_viewModel.FormatDuration(null), Is.EqualTo("0:00"));
    }

    [Test]
    public void FormatDuration_ReturnsZeroForNaN()
    {
        Assert.That(_viewModel.FormatDuration(double.NaN), Is.EqualTo("0:00"));
    }

    [Test]
    public void FormatDuration_ReturnsZeroForInfinity()
    {
        Assert.That(_viewModel.FormatDuration(double.PositiveInfinity), Is.EqualTo("0:00"));
        Assert.That(_viewModel.FormatDuration(double.NegativeInfinity), Is.EqualTo("0:00"));
    }

    // --- Playback progress ---

    [Test]
    public void UpdatePlaybackPosition_SetsProgressAndFormattedStrings()
    {
        // Act
        _viewModel.UpdatePlaybackPosition(
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(120));

        // Assert
        Assert.That(_viewModel.PlaybackProgress, Is.EqualTo(0.25));
        Assert.That(_viewModel.FormattedPosition, Is.EqualTo("0:30"));
        Assert.That(_viewModel.FormattedDuration, Is.EqualTo("2:00"));
    }

    [Test]
    public void UpdatePlaybackPosition_WithZeroDuration_SetsProgressToZero()
    {
        // Act
        _viewModel.UpdatePlaybackPosition(TimeSpan.Zero, TimeSpan.Zero);

        // Assert
        Assert.That(_viewModel.PlaybackProgress, Is.EqualTo(0));
    }

    [Test]
    public void GetSeekPosition_ReturnsCorrectTimeSpan()
    {
        // Arrange - set duration to 200 seconds
        _viewModel.UpdatePlaybackPosition(TimeSpan.Zero, TimeSpan.FromSeconds(200));

        // Act
        var seekPos = _viewModel.GetSeekPosition(0.5);

        // Assert
        Assert.That(seekPos.TotalSeconds, Is.EqualTo(100));
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
        // Arrange
        var song = new SongDto { Id = 1, SongTitle = "Test" };
        _viewModel.PlaySongCommand.Execute(song);
        _viewModel.UpdatePlaybackPosition(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(180));

        // Act
        _viewModel.StopCommand.Execute(null);

        // Assert
        Assert.That(_viewModel.PlaybackProgress, Is.EqualTo(0));
        Assert.That(_viewModel.FormattedPosition, Is.EqualTo("0:00"));
        Assert.That(_viewModel.FormattedDuration, Is.EqualTo("0:00"));
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
    public async Task LoadStreamQualifyingSecondsAsync_FetchesFromService()
    {
        // Arrange
        _mockMusicService.Setup(s => s.GetStreamQualifyingSecondsAsync()).ReturnsAsync(45);

        // Act
        await _viewModel.LoadStreamQualifyingSecondsAsync();

        // Assert
        _mockMusicService.Verify(s => s.GetStreamQualifyingSecondsAsync(), Times.Once);
    }

    [Test]
    public void StreamTracking_RecordsStreamAfterQualifyingSeconds()
    {
        // Arrange - set qualifying seconds to 5 for fast test
        _mockMusicService.Setup(s => s.GetStreamQualifyingSecondsAsync()).ReturnsAsync(5);
        _viewModel.LoadStreamQualifyingSecondsAsync().GetAwaiter().GetResult();

        var song = new SongDto { Id = 10, SongTitle = "Test" };
        _viewModel.Songs.Add(song);
        _viewModel.PlaySongCommand.Execute(song);

        // Act - simulate 6 ticks of ~1 second each (exceeds 5s threshold)
        for (int i = 1; i <= 6; i++)
        {
            _viewModel.UpdatePlaybackPosition(
                TimeSpan.FromSeconds(i),
                TimeSpan.FromSeconds(180));
        }

        // Assert - RecordStreamAsync should have been called once
        _mockMusicService.Verify(s => s.RecordStreamAsync(10), Times.Once);
    }

    [Test]
    public void StreamTracking_DoesNotRecordBeforeThreshold()
    {
        // Arrange - set qualifying seconds to 30
        _mockMusicService.Setup(s => s.GetStreamQualifyingSecondsAsync()).ReturnsAsync(30);
        _viewModel.LoadStreamQualifyingSecondsAsync().GetAwaiter().GetResult();

        var song = new SongDto { Id = 10, SongTitle = "Test" };
        _viewModel.Songs.Add(song);
        _viewModel.PlaySongCommand.Execute(song);

        // Act - simulate 10 seconds of playback (below 30s threshold)
        for (int i = 1; i <= 10; i++)
        {
            _viewModel.UpdatePlaybackPosition(
                TimeSpan.FromSeconds(i),
                TimeSpan.FromSeconds(180));
        }

        // Assert - RecordStreamAsync should NOT have been called
        _mockMusicService.Verify(s => s.RecordStreamAsync(It.IsAny<int>()), Times.Never);
    }

    [Test]
    public void StreamTracking_RecordsOnlyOncePerSong()
    {
        // Arrange
        _mockMusicService.Setup(s => s.GetStreamQualifyingSecondsAsync()).ReturnsAsync(3);
        _viewModel.LoadStreamQualifyingSecondsAsync().GetAwaiter().GetResult();

        var song = new SongDto { Id = 10, SongTitle = "Test" };
        _viewModel.Songs.Add(song);
        _viewModel.PlaySongCommand.Execute(song);

        // Act - simulate 10 seconds (well past 3s threshold)
        for (int i = 1; i <= 10; i++)
        {
            _viewModel.UpdatePlaybackPosition(
                TimeSpan.FromSeconds(i),
                TimeSpan.FromSeconds(180));
        }

        // Assert - only called once despite exceeding threshold many ticks ago
        _mockMusicService.Verify(s => s.RecordStreamAsync(10), Times.Once);
    }

    [Test]
    public void StreamTracking_ResetsOnSongChange()
    {
        // Arrange
        _mockMusicService.Setup(s => s.GetStreamQualifyingSecondsAsync()).ReturnsAsync(5);
        _viewModel.LoadStreamQualifyingSecondsAsync().GetAwaiter().GetResult();

        var song1 = new SongDto { Id = 10, SongTitle = "First" };
        var song2 = new SongDto { Id = 20, SongTitle = "Second" };
        _viewModel.Songs.Add(song1);
        _viewModel.Songs.Add(song2);

        // Play song1 for 3 seconds (under threshold)
        _viewModel.PlaySongCommand.Execute(song1);
        for (int i = 1; i <= 3; i++)
        {
            _viewModel.UpdatePlaybackPosition(
                TimeSpan.FromSeconds(i),
                TimeSpan.FromSeconds(180));
        }

        // Switch to song2 - play for 6 seconds (over threshold)
        _viewModel.PlaySongCommand.Execute(song2);
        for (int i = 1; i <= 6; i++)
        {
            _viewModel.UpdatePlaybackPosition(
                TimeSpan.FromSeconds(i),
                TimeSpan.FromSeconds(180));
        }

        // Assert - only song2 exceeded threshold
        _mockMusicService.Verify(s => s.RecordStreamAsync(10), Times.Never);
        _mockMusicService.Verify(s => s.RecordStreamAsync(20), Times.Once);
    }

    [Test]
    public void StreamTracking_IgnoresSeeks()
    {
        // Arrange
        _mockMusicService.Setup(s => s.GetStreamQualifyingSecondsAsync()).ReturnsAsync(10);
        _viewModel.LoadStreamQualifyingSecondsAsync().GetAwaiter().GetResult();

        var song = new SongDto { Id = 10, SongTitle = "Test" };
        _viewModel.Songs.Add(song);
        _viewModel.PlaySongCommand.Execute(song);

        // Act - simulate a jump from 2s to 50s (seek, not continuous)
        _viewModel.UpdatePlaybackPosition(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(180));
        _viewModel.UpdatePlaybackPosition(TimeSpan.FromSeconds(50), TimeSpan.FromSeconds(180));

        // Assert - should not record (48-second jump is a seek, not continuous play)
        _mockMusicService.Verify(s => s.RecordStreamAsync(It.IsAny<int>()), Times.Never);
    }

    [Test]
    public void StreamTracking_ResetsOnStop()
    {
        // Arrange
        _mockMusicService.Setup(s => s.GetStreamQualifyingSecondsAsync()).ReturnsAsync(5);
        _viewModel.LoadStreamQualifyingSecondsAsync().GetAwaiter().GetResult();

        var song = new SongDto { Id = 10, SongTitle = "Test" };
        _viewModel.Songs.Add(song);
        _viewModel.PlaySongCommand.Execute(song);

        // Play for 3 seconds
        for (int i = 1; i <= 3; i++)
        {
            _viewModel.UpdatePlaybackPosition(
                TimeSpan.FromSeconds(i),
                TimeSpan.FromSeconds(180));
        }

        // Stop
        _viewModel.StopCommand.Execute(null);

        // Play same song again, 3 more seconds - total would be 6 but should be 3 (reset)
        _viewModel.PlaySongCommand.Execute(song);
        for (int i = 1; i <= 3; i++)
        {
            _viewModel.UpdatePlaybackPosition(
                TimeSpan.FromSeconds(i),
                TimeSpan.FromSeconds(180));
        }

        // Assert - never reached 5s threshold continuously
        _mockMusicService.Verify(s => s.RecordStreamAsync(It.IsAny<int>()), Times.Never);
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
        _viewModel.SelectedGenre = "Rock";

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
        _viewModel.SelectedArtist = "Bob";

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
        _viewModel.SelectedGenre = "Rock";
        _viewModel.SelectedArtist = "Bob";

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

        _viewModel.SelectedGenre = "Jazz";
        Assert.That(_viewModel.Songs, Has.Count.EqualTo(1));

        // Act
        _viewModel.ClearFiltersCommand.Execute(null);

        // Assert
        Assert.That(_viewModel.Songs, Has.Count.EqualTo(5));
        Assert.That(_viewModel.SelectedGenre, Is.Null);
        Assert.That(_viewModel.SelectedArtist, Is.Null);
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
        _viewModel.SelectedGenre = "Jazz";

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
        _viewModel.SelectedArtist = "Alice";

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
        _viewModel.SelectedGenre = "Rock";

        // Assert - both match
        Assert.That(_viewModel.Songs, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task LoadSongs_ResetsFilters()
    {
        LoadTestSongsDirectly();
        await _viewModel.LoadSongsCommand.ExecuteAsync(null);

        _viewModel.SelectedGenre = "Jazz";
        Assert.That(_viewModel.Songs, Has.Count.EqualTo(1));

        // Act - reload songs
        await _viewModel.LoadSongsCommand.ExecuteAsync(null);

        // Assert - filters reset, all songs shown
        Assert.That(_viewModel.SelectedGenre, Is.Null);
        Assert.That(_viewModel.SelectedArtist, Is.Null);
        Assert.That(_viewModel.Songs, Has.Count.EqualTo(5));
    }

    [Test]
    public async Task Filter_NoMatch_ShowsEmptyCollection()
    {
        LoadTestSongsDirectly();
        await _viewModel.LoadSongsCommand.ExecuteAsync(null);

        // Act - select genre that won't combine with artist
        _viewModel.SelectedGenre = "Jazz";
        _viewModel.SelectedArtist = "Alice";

        // Assert - Alice has no Jazz songs
        Assert.That(_viewModel.Songs, Has.Count.EqualTo(0));
    }
}
