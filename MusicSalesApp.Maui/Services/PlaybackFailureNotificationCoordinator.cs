namespace MusicSalesApp.Maui.Services;

public sealed class PlaybackFailureNotificationCoordinator : IDisposable
{
    internal const string UnavailableOfflineMessage =
        "This song isn't downloaded and no internet connection is available. Use the Downloaded filter to find songs you can play offline.";

    internal const string UnplayableTrackSkippedMessage =
        "Skipped a song that can't be played.";

    internal const string UnexpectedErrorMessage =
        "Something went wrong starting playback. Please try again.";

    private static readonly TimeSpan DuplicateFailureWindow = TimeSpan.FromSeconds(2);
    private readonly IPlaybackService _playbackService;
    private readonly IToastService _toastService;
    private readonly TimeProvider _timeProvider;
    private readonly object _sync = new();
    private DateTimeOffset _lastUnavailableOfflineNotification = DateTimeOffset.MinValue;
    private DateTimeOffset _lastUnplayableTrackSkippedNotification = DateTimeOffset.MinValue;
    private DateTimeOffset _lastUnexpectedErrorNotification = DateTimeOffset.MinValue;
    private bool _disposed;

    public PlaybackFailureNotificationCoordinator(
        IPlaybackService playbackService,
        IToastService toastService)
        : this(playbackService, toastService, TimeProvider.System)
    {
    }

    internal PlaybackFailureNotificationCoordinator(
        IPlaybackService playbackService,
        IToastService toastService,
        TimeProvider timeProvider)
    {
        _playbackService = playbackService;
        _toastService = toastService;
        _timeProvider = timeProvider;
        _playbackService.PlaybackRequestFailed += OnPlaybackRequestFailed;
    }

    private void OnPlaybackRequestFailed(object? sender, PlaybackRequestFailedEventArgs args)
    {
        if (args.Reason == PlaybackRequestFailureReason.UnavailableOffline &&
            ShouldNotify(ref _lastUnavailableOfflineNotification))
        {
            _ = ShowToastAsync(UnavailableOfflineMessage);
        }
        else if (args.Reason == PlaybackRequestFailureReason.UnplayableTrackSkipped &&
            ShouldNotify(ref _lastUnplayableTrackSkippedNotification))
        {
            _ = ShowToastAsync(UnplayableTrackSkippedMessage);
        }
        else if (args.Reason == PlaybackRequestFailureReason.UnexpectedError &&
            ShouldNotify(ref _lastUnexpectedErrorNotification))
        {
            _ = ShowToastAsync(UnexpectedErrorMessage);
        }
    }

    private bool ShouldNotify(ref DateTimeOffset lastNotification)
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return false;
            }

            var now = _timeProvider.GetUtcNow();
            if (now - lastNotification < DuplicateFailureWindow)
            {
                return false;
            }

            lastNotification = now;
            return true;
        }
    }

    private async Task ShowToastAsync(string message)
    {
        try
        {
            await _toastService.ShowAsync(message).ConfigureAwait(false);
        }
        catch
        {
            // A transient notification failure must never affect playback.
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _playbackService.PlaybackRequestFailed -= OnPlaybackRequestFailed;
        }
    }
}
