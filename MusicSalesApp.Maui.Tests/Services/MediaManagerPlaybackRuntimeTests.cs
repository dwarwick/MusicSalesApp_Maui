using MediaManager;
using MediaManager.Library;
using MediaManager.Media;
using MediaManager.Playback;
using MediaManager.Player;
using MediaManager.Queue;
using Moq;
using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.Tests.Services;

/// <summary>
/// The non-Android runtime's terminal-state classification and now-playing artwork wiring.
///
/// <para>
/// Guards the invariant in PLAYBACK_CACHE_ARCHITECTURE.md: "User-requested pause/stop must never be
/// interpreted as an unexpected playback failure that should restart the queue." A lock-screen pause
/// reaches PlaybackService as a bare state change - Plugin.MediaManager's remote-command handler calls
/// MediaManager.Pause() directly and never tells PlaybackService - so if the reason is not resolved
/// here, PlaybackService reads it as a stall and restarts the queue.
/// </para>
/// </summary>
[TestFixture]
public class MediaManagerPlaybackRuntimeTests
{
    private Mock<IMediaManager> _mediaManager = null!;
    private FakeRemoteCommandBridge _remoteCommands = null!;
    private ManualTimeProvider _time = null!;
    private MediaManagerPlaybackRuntime _runtime = null!;

    [SetUp]
    public void SetUp()
    {
        _mediaManager = new Mock<IMediaManager>();
        _remoteCommands = new FakeRemoteCommandBridge();
        _time = new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        _runtime = new MediaManagerPlaybackRuntime(
            _mediaManager.Object,
            artworkCoordinator: null,
            remoteCommandBridge: _remoteCommands,
            timeProvider: _time);
    }

    [Test]
    public void Constructor_StartsTheRemoteCommandBridge()
    {
        // Start() is what disables the 10-second skip commands so previous/next track are shown.
        Assert.That(_remoteCommands.StartCount, Is.EqualTo(1));
    }

    [Test]
    public void StateChanged_ReAssertsTheTransportControlPolicy()
    {
        // Plugin.MediaManager constructs its NotificationManager lazily, and that constructor turns
        // the 10-second skip commands back on - which is what masked previous/next on the first
        // attempt at this fix. Setting the policy once at startup is not enough.
        _mediaManager.Raise(m => m.StateChanged += null, this, new StateChangedEventArgs(MediaPlayerState.Playing));
        _mediaManager.Raise(m => m.StateChanged += null, this, new StateChangedEventArgs(MediaPlayerState.Paused));

        Assert.That(_remoteCommands.RefreshCount, Is.EqualTo(2));
    }

    [Test]
    public void ResolveStateChangeReason_WithNoTransportCommand_IsUnknown()
    {
        Assert.That(
            _runtime.ResolveStateChangeReason(PlaybackRuntimeState.Paused),
            Is.EqualTo(PlaybackRuntimeStateChangeReason.Unknown));
    }

    [TestCase(PlaybackRuntimeState.Paused)]
    [TestCase(PlaybackRuntimeState.Stopped)]
    public void ResolveStateChangeReason_JustAfterATransportCommand_IsUserRequest(PlaybackRuntimeState state)
    {
        _remoteCommands.RaiseUserTerminalCommand();

        Assert.That(
            _runtime.ResolveStateChangeReason(state),
            Is.EqualTo(PlaybackRuntimeStateChangeReason.UserRequest));
    }

    [Test]
    public void ResolveStateChangeReason_WithinTheWindow_IsStillUserRequest()
    {
        // The pause arrives as more than one state change, so a one-shot flag would not survive.
        _remoteCommands.RaiseUserTerminalCommand();
        _time.Advance(MediaManagerPlaybackRuntime.UserTerminalStateReasonWindow - TimeSpan.FromMilliseconds(100));

        Assert.That(
            _runtime.ResolveStateChangeReason(PlaybackRuntimeState.Paused),
            Is.EqualTo(PlaybackRuntimeStateChangeReason.UserRequest));
    }

    [Test]
    public void ResolveStateChangeReason_AfterTheWindowExpires_IsUnknown()
    {
        // A genuine stall minutes later must still be recoverable.
        _remoteCommands.RaiseUserTerminalCommand();
        _time.Advance(MediaManagerPlaybackRuntime.UserTerminalStateReasonWindow + TimeSpan.FromSeconds(1));

        Assert.That(
            _runtime.ResolveStateChangeReason(PlaybackRuntimeState.Paused),
            Is.EqualTo(PlaybackRuntimeStateChangeReason.Unknown));
    }

    [TestCase(PlaybackRuntimeState.Playing)]
    [TestCase(PlaybackRuntimeState.Buffering)]
    [TestCase(PlaybackRuntimeState.Failed)]
    public void ResolveStateChangeReason_ForNonTerminalStates_IsAlwaysUnknown(PlaybackRuntimeState state)
    {
        _remoteCommands.RaiseUserTerminalCommand();

        Assert.That(
            _runtime.ResolveStateChangeReason(state),
            Is.EqualTo(PlaybackRuntimeStateChangeReason.Unknown));
    }

    [Test]
    public void StateChanged_AfterATransportPause_SurfacesTheUserRequestReason()
    {
        PlaybackRuntimeStateChangedEventArgs? captured = null;
        _runtime.StateChanged += (_, args) => captured = args;

        _remoteCommands.RaiseUserTerminalCommand();
        _mediaManager.Raise(m => m.StateChanged += null, this, new StateChangedEventArgs(MediaPlayerState.Paused));

        Assert.That(captured, Is.Not.Null);
        Assert.That(captured!.State, Is.EqualTo(PlaybackRuntimeState.Paused));
        Assert.That(captured.IsUserRequest, Is.True);
    }

    [Test]
    public void StateChanged_WithoutATransportCommand_LeavesTheReasonUnknown()
    {
        // An unexplained pause must stay recoverable; this is the branch stall recovery depends on.
        PlaybackRuntimeStateChangedEventArgs? captured = null;
        _runtime.StateChanged += (_, args) => captured = args;

        _mediaManager.Raise(m => m.StateChanged += null, this, new StateChangedEventArgs(MediaPlayerState.Paused));

        Assert.That(captured, Is.Not.Null);
        Assert.That(captured!.IsUserRequest, Is.False);
    }

    [Test]
    public void PauseAsync_DoesNotMarkTheStateAsUserRequested()
    {
        // Deliberate: the app calls PauseAsync/StopAsync during failure recovery too, and marking
        // those as user requests would suppress the recovery they are part of.
        _mediaManager.Setup(m => m.Pause()).Returns(Task.CompletedTask);

        _runtime.PauseAsync();

        Assert.That(
            _runtime.ResolveStateChangeReason(PlaybackRuntimeState.Paused),
            Is.EqualTo(PlaybackRuntimeStateChangeReason.Unknown));
    }

    [Test]
    public async Task ObserveNowPlayingArtwork_UsesQueueCurrentRatherThanTheEventItem()
    {
        // UpdateNotification() reads Queue.Current, so an image set on any other instance is invisible.
        var loader = new RecordingArtworkLoader();
        using var coordinator = new NowPlayingArtworkCoordinator(
            loader,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<NowPlayingArtworkCoordinator>.Instance);

        var queueItem = new MediaItem("file:///audio/current.mp3")
        {
            AlbumImageUri = "file:///art/hero.jpg"
        };
        var eventItem = new MediaItem("file:///audio/other.mp3")
        {
            AlbumImageUri = "file:///art/wrong.jpg"
        };

        var queue = new Mock<IMediaQueue>();
        queue.SetupGet(q => q.Current).Returns(queueItem);
        _mediaManager.SetupGet(m => m.Queue).Returns(queue.Object);

        _ = new MediaManagerPlaybackRuntime(_mediaManager.Object, coordinator, _remoteCommands, _time);

        _mediaManager.Raise(m => m.MediaItemChanged += null, this, new MediaItemEventArgs(eventItem));
        if (coordinator.InFlightLoad is { } load)
        {
            await load;
        }

        Assert.That(loader.RequestedUris, Does.Contain("file:///art/hero.jpg"));
        Assert.That(loader.RequestedUris, Does.Not.Contain("file:///art/wrong.jpg"));

        // DisplayImage, not Image: that is the property the notification manager actually reads, and
        // writing it directly cannot be shadowed by anything that assigns DisplayImage later.
        Assert.That(queueItem.DisplayImage, Is.Not.Null);
    }

    private sealed class FakeRemoteCommandBridge : IPlaybackRemoteCommandBridge
    {
        public event EventHandler? UserTerminalCommandRequested;

        public int StartCount { get; private set; }

        public int RefreshCount { get; private set; }

        public void Start() => StartCount++;

        public void RefreshTransportControls() => RefreshCount++;

        public void RaiseUserTerminalCommand() =>
            UserTerminalCommandRequested?.Invoke(this, EventArgs.Empty);
    }

    private sealed class RecordingArtworkLoader : INowPlayingArtworkLoader
    {
        public List<string> RequestedUris { get; } = [];

        public List<int> RequestedVersions { get; } = [];

        public Task<NowPlayingArtworkLoadResult> LoadAsync(
            string artworkUri,
            int contentVersion = 0,
            CancellationToken cancellationToken = default)
        {
            lock (RequestedUris)
            {
                RequestedUris.Add(artworkUri);
                RequestedVersions.Add(contentVersion);
            }

            return Task.FromResult(NowPlayingArtworkLoadResult.Loaded(new object()));
        }
    }
}
