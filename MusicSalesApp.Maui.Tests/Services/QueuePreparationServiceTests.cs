using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Tests.Services;

[TestFixture]
public class QueuePreparationServiceTests
{
    private Mock<ITrackCacheService> _trackCacheService = null!;
    private QueuePreparationService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _trackCacheService = new Mock<ITrackCacheService>();
        _trackCacheService
            .Setup(s => s.GetStableCacheKey(It.IsAny<SongDto>()))
            .Returns((SongDto song) => $"song-{song.Id}");
        _trackCacheService
            .Setup(s => s.GetCacheStatus(It.IsAny<SongDto>()))
            .Returns((SongDto song) => CreateStatus(song, isReady: false));
        _trackCacheService
            .Setup(s => s.EnsureCachedAsync(It.IsAny<SongDto>(), It.IsAny<CachePinScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SongDto song, CachePinScope _, CancellationToken _) => CreateStatus(song, isReady: false));

        _service = new QueuePreparationService(_trackCacheService.Object, NullLogger<QueuePreparationService>.Instance);
    }

    [Test]
    public async Task QueuePreparationResult_ReportsReadyThroughIndexAndDuration()
    {
        var songs = CreateSongs();
        SetupReady(songs[0]);
        SetupReady(songs[1]);

        var result = await _service.PrepareAsync(
            songs,
            0,
            QueuePreparationMode.SleepSafe,
            TimeSpan.FromMinutes(10));

        Assert.Multiple(() =>
        {
            Assert.That(result.CurrentTrackReady, Is.True);
            Assert.That(result.ReadyThroughQueueIndex, Is.EqualTo(1));
            Assert.That(result.ReadyThroughDuration, Is.EqualTo(TimeSpan.FromMinutes(7)));
            Assert.That(result.NotReadyItems.Select(item => item.SongId), Is.EqualTo(new[] { 3 }));
            Assert.That(result.Mode, Is.EqualTo(QueuePreparationMode.SleepSafe));
            Assert.That(result.FailureReason, Is.EqualTo(QueuePreparationFailureReason.DownloadFailed));
        });
    }

    [Test]
    public async Task DownloadFailure_PreservesCurrentReadinessAndReportsNotReadyItems()
    {
        var songs = CreateSongs();
        SetupReady(songs[0]);

        var result = await _service.PrepareAsync(
            songs,
            0,
            QueuePreparationMode.SleepSafe,
            TimeSpan.FromMinutes(10));

        Assert.Multiple(() =>
        {
            Assert.That(result.CurrentTrackReady, Is.True);
            Assert.That(result.ReadyThroughQueueIndex, Is.EqualTo(0));
            Assert.That(result.NotReadyItems, Has.Count.EqualTo(1));
            Assert.That(result.NotReadyItems[0].SongId, Is.EqualTo(songs[1].Id));
            Assert.That(result.FailureReason, Is.EqualTo(QueuePreparationFailureReason.DownloadFailed));
        });
    }

    [Test]
    public async Task SleepSafe_WithZeroContinuityWindow_PreparesThroughQueueEnd()
    {
        var songs = CreateSongs();
        foreach (var song in songs)
        {
            SetupReady(song);
        }

        var result = await _service.PrepareAsync(
            songs,
            0,
            QueuePreparationMode.SleepSafe,
            TimeSpan.Zero);

        Assert.Multiple(() =>
        {
            Assert.That(result.CurrentTrackReady, Is.True);
            Assert.That(result.ReadyThroughQueueIndex, Is.EqualTo(songs.Count - 1));
            Assert.That(result.ReadyThroughDuration, Is.EqualTo(TimeSpan.FromMinutes(12)));
            Assert.That(result.NotReadyItems, Is.Empty);
            Assert.That(result.FailureReason, Is.Null);
        });
    }

    private void SetupReady(SongDto song)
    {
        _trackCacheService
            .Setup(s => s.GetCacheStatus(It.Is<SongDto>(candidate => candidate.Id == song.Id)))
            .Returns(CreateStatus(song, isReady: true));
        _trackCacheService
            .Setup(s => s.EnsureCachedAsync(It.Is<SongDto>(candidate => candidate.Id == song.Id), It.IsAny<CachePinScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateStatus(song, isReady: true));
    }

    private static TrackCacheStatus CreateStatus(SongDto song, bool isReady) =>
        new(
            song.Id,
            $"song-{song.Id}",
            isReady ? $"/cache/song{song.Id}.mp3" : null,
            isReady,
            isReady);

    private static List<SongDto> CreateSongs() =>
    [
        new() { Id = 1, SongTitle = "Song 1", StreamUrl = "https://test.com/song1.mp3", TrackLengthSeconds = 180 },
        new() { Id = 2, SongTitle = "Song 2", StreamUrl = "https://test.com/song2.mp3", TrackLengthSeconds = 240 },
        new() { Id = 3, SongTitle = "Song 3", StreamUrl = "https://test.com/song3.mp3", TrackLengthSeconds = 300 }
    ];
}
