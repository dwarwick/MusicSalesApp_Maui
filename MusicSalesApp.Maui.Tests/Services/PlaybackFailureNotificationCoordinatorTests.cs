using Moq;
using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.Tests.Services;

public class PlaybackFailureNotificationCoordinatorTests
{
    [Test]
    public void UnavailableOffline_ShowsFriendlyMessage()
    {
        var playback = new Mock<IPlaybackService>();
        var toast = new Mock<IToastService>();
        toast.Setup(service => service.ShowAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        using var coordinator = new PlaybackFailureNotificationCoordinator(playback.Object, toast.Object);

        playback.Raise(service => service.PlaybackRequestFailed += null,
            playback.Object,
            new PlaybackRequestFailedEventArgs(7, PlaybackRequestFailureReason.UnavailableOffline));

        toast.Verify(service => service.ShowAsync(
            "This song isn't downloaded and no internet connection is available. Use the Downloaded filter to find songs you can play offline.",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public void IdenticalFailures_AreDebouncedForTwoSeconds()
    {
        var playback = new Mock<IPlaybackService>();
        var toast = new Mock<IToastService>();
        toast.Setup(service => service.ShowAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 7, 18, 12, 0, 0, TimeSpan.Zero));
        using var coordinator = new PlaybackFailureNotificationCoordinator(playback.Object, toast.Object, clock);

        RaiseUnavailableOffline(playback, 1);
        RaiseUnavailableOffline(playback, 2);
        clock.Advance(TimeSpan.FromSeconds(2));
        RaiseUnavailableOffline(playback, 2);

        toast.Verify(service => service.ShowAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Test]
    public void Dispose_UnsubscribesFromPlaybackFailures()
    {
        var playback = new Mock<IPlaybackService>();
        var toast = new Mock<IToastService>();
        var coordinator = new PlaybackFailureNotificationCoordinator(playback.Object, toast.Object);
        coordinator.Dispose();

        RaiseUnavailableOffline(playback, 1);

        toast.Verify(service => service.ShowAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static void RaiseUnavailableOffline(Mock<IPlaybackService> playback, int songId) =>
        playback.Raise(service => service.PlaybackRequestFailed += null,
            playback.Object,
            new PlaybackRequestFailedEventArgs(songId, PlaybackRequestFailureReason.UnavailableOffline));

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan duration) => _now += duration;
    }
}
