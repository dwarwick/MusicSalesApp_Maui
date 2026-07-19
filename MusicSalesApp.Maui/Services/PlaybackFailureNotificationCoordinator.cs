namespace MusicSalesApp.Maui.Services;

public sealed class PlaybackFailureNotificationCoordinator : IDisposable
{
    internal const string UnavailableOfflineMessage =
        "This song isn't downloaded and no internet connection is available. Use the Downloaded filter to find songs you can play offline.";

    private static readonly TimeSpan DuplicateFailureWindow = TimeSpan.FromSeconds(2);
    private readonly IPlaybackService _playbackService;
    private readonly IToastService _toastService;
    private readonly TimeProvider _timeProvider;
    private readonly object _sync = new();
    private DateTimeOffset _lastUnavailableOfflineNotification = DateTimeOffset.MinValue;
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
        if (args.Reason == PlaybackRequestFailureReason.UnavailableOffline && ShouldNotify())
        {
            _ = ShowUnavailableOfflineAsync();
        }
    }

    private bool ShouldNotify()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return false;
            }

            var now = _timeProvider.GetUtcNow();
            if (now - _lastUnavailableOfflineNotification < DuplicateFailureWindow)
            {
                return false;
            }

            _lastUnavailableOfflineNotification = now;
            return true;
        }
    }

    private async Task ShowUnavailableOfflineAsync()
    {
        try
        {
            await _toastService.ShowAsync(UnavailableOfflineMessage).ConfigureAwait(false);
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
