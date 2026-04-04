using Moq;
using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Tests.ViewModels;

[TestFixture]
public class MusicLibraryViewModelTests
{
    private Mock<IMusicService> _mockMusicService;
    private MusicLibraryViewModel _viewModel;

    [SetUp]
    public void Setup()
    {
        _mockMusicService = new Mock<IMusicService>();
        _viewModel = new MusicLibraryViewModel(_mockMusicService.Object);
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
        Assert.That(_viewModel.FormatDuration(null), Is.EqualTo("--:--"));
    }
}
