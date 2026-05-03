using Moq;
using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Tests.ViewModels;

[TestFixture]
public class PlaybackQueueBootstrapperTests
{
    private Mock<IMediaPlaybackOnboardingService> _mockMediaPlaybackOnboardingService;
    private Mock<IPlaybackService> _mockPlaybackService;

    [SetUp]
    public void Setup()
    {
        _mockMediaPlaybackOnboardingService = new Mock<IMediaPlaybackOnboardingService>();
        _mockPlaybackService = new Mock<IPlaybackService>();
        _mockMediaPlaybackOnboardingService
            .Setup(service => service.EnsureBackgroundPlaybackExplainedAsync())
            .Returns(Task.CompletedTask);
    }

    [Test]
    public async Task StartQueueAsync_WhenSongsEmpty_ReturnsFalse()
    {
        var result = await PlaybackQueueBootstrapper.StartQueueAsync(
            [],
            _mockMediaPlaybackOnboardingService.Object,
            _mockPlaybackService.Object);

        Assert.That(result, Is.False);
        _mockMediaPlaybackOnboardingService.Verify(service => service.EnsureBackgroundPlaybackExplainedAsync(), Times.Never);
        _mockPlaybackService.Verify(service => service.SetPlaylist(It.IsAny<List<SongDto>>(), It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task StartQueueAsync_WhenStartSongProvided_QueuesMatchingIndex()
    {
        var songs = new List<SongDto>
        {
            new() { Id = 10, SongTitle = "First" },
            new() { Id = 11, SongTitle = "Second" },
            new() { Id = 12, SongTitle = "Third" }
        };

        var result = await PlaybackQueueBootstrapper.StartQueueAsync(
            songs,
            _mockMediaPlaybackOnboardingService.Object,
            _mockPlaybackService.Object,
            songs[1]);

        Assert.That(result, Is.True);
        _mockMediaPlaybackOnboardingService.Verify(service => service.EnsureBackgroundPlaybackExplainedAsync(), Times.Once);
        _mockPlaybackService.Verify(service => service.SetPlaylist(
            It.Is<List<SongDto>>(playlist => playlist.Select(song => song.Id).SequenceEqual(new[] { 10, 11, 12 })),
            1), Times.Once);
    }

    [Test]
    public async Task StartQueueAsync_WhenStartSongMissing_StartsFromBeginning()
    {
        var songs = new List<SongDto>
        {
            new() { Id = 10, SongTitle = "First" },
            new() { Id = 11, SongTitle = "Second" }
        };

        await PlaybackQueueBootstrapper.StartQueueAsync(
            songs,
            _mockMediaPlaybackOnboardingService.Object,
            _mockPlaybackService.Object,
            new SongDto { Id = 99, SongTitle = "Not Present" });

        _mockPlaybackService.Verify(service => service.SetPlaylist(
            It.Is<List<SongDto>>(playlist => playlist.Select(song => song.Id).SequenceEqual(new[] { 10, 11 })),
            0), Times.Once);
    }
}