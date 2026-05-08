using Moq;
using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.ViewModels;
using MusicSalesApp.Maui.Views;

namespace MusicSalesApp.Maui.Tests.Views;

[TestFixture]
public class NowPlayingEmptyStateActionRunnerTests
{
    private NowPlayingEmptyStateActionRunner _runner;
    private Mock<IPlaybackService> _mockPlaybackService;

    [SetUp]
    public void SetUp()
    {
        _runner = new NowPlayingEmptyStateActionRunner();
        _mockPlaybackService = new Mock<IPlaybackService>();
    }

    [Test]
    public async Task ToggleShuffleAsync_WhenSongAlreadyQueued_TogglesImmediately()
    {
        _mockPlaybackService.SetupGet(service => service.CurrentSong).Returns(new SongDto { Id = 1, SongTitle = "Queued" });

        await _runner.ToggleShuffleAsync(_mockPlaybackService.Object, () => Task.FromResult(true));

        _mockPlaybackService.Verify(service => service.ToggleShuffle(), Times.Once);
    }

    [Test]
    public async Task ToggleShuffleAsync_WhenEmptyAndQueueLoads_TogglesAfterQueueing()
    {
        var currentSong = (SongDto?)null;
        _mockPlaybackService.SetupGet(service => service.CurrentSong).Returns(() => currentSong);

        var queueCalls = 0;
        Task<bool> QueueAsync()
        {
            queueCalls++;
            currentSong = new SongDto { Id = 2, SongTitle = "Queued" };
            return Task.FromResult(true);
        }

        await _runner.ToggleShuffleAsync(_mockPlaybackService.Object, QueueAsync);

        Assert.That(queueCalls, Is.EqualTo(1));
        _mockPlaybackService.Verify(service => service.ToggleShuffle(), Times.Once);
    }

    [Test]
    public async Task ToggleRepeatAsync_WhenEmptyAndQueueFails_DoesNothing()
    {
        _mockPlaybackService.SetupGet(service => service.CurrentSong).Returns((SongDto?)null);

        await _runner.ToggleRepeatAsync(_mockPlaybackService.Object, () => Task.FromResult(false));

        _mockPlaybackService.Verify(service => service.ToggleRepeat(), Times.Never);
    }

    [Test]
    public async Task ToggleRepeatAsync_WhenEmptyAndQueueLoads_TogglesAfterQueueing()
    {
        var currentSong = (SongDto?)null;
        _mockPlaybackService.SetupGet(service => service.CurrentSong).Returns(() => currentSong);

        Task<bool> QueueAsync()
        {
            currentSong = new SongDto { Id = 3, SongTitle = "Queued" };
            return Task.FromResult(true);
        }

        await _runner.ToggleRepeatAsync(_mockPlaybackService.Object, QueueAsync);

        _mockPlaybackService.Verify(service => service.ToggleRepeat(), Times.Once);
    }
}