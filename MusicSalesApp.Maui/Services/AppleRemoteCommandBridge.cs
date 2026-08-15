#if IOS || MACCATALYST
using MediaManager;
using MediaPlayer;
using Microsoft.Extensions.Logging;

namespace MusicSalesApp.Maui.Services;

/// <summary>
/// Configures the iOS lock screen / Control Center transport controls, and reports pause and stop
/// presses so the runtime can mark the terminal state that follows as user-requested.
/// </summary>
public sealed class AppleRemoteCommandBridge : IPlaybackRemoteCommandBridge
{
    private readonly IMediaManager _mediaManager;
    private readonly ILogger<AppleRemoteCommandBridge> _logger;
    private readonly object _sync = new();
    private bool _started;

    public AppleRemoteCommandBridge(IMediaManager mediaManager, ILogger<AppleRemoteCommandBridge> logger)
    {
        _mediaManager = mediaManager;
        _logger = logger;
    }

    public event EventHandler? UserTerminalCommandRequested;

    public void Start()
    {
        lock (_sync)
        {
            if (_started)
            {
                return;
            }

            _started = true;
        }

        var commandCenter = MPRemoteCommandCenter.Shared;

        // ORDER IS LOAD-BEARING, and both halves of it were learned by getting it wrong on device.
        //
        // These targets are additions, not replacements: Plugin.MediaManager's own handlers stay
        // registered and are what actually perform the pause. They must be registered *before* the
        // library's, because its handler calls MediaManager.Pause() synchronously and the resulting
        // StateChanged - where ResolveStateChangeReason reads this timestamp - is raised before any
        // later-registered target runs. Register after the library and the stamp lands too late, the
        // pause looks like a stall, and PlaybackService restarts the queue two seconds later.
        commandCenter.PauseCommand.AddTarget(OnUserTerminalCommand);
        commandCenter.StopCommand.AddTarget(OnUserTerminalCommand);
        commandCenter.TogglePlayPauseCommand.AddTarget(OnUserTerminalCommand);

        // ...and only now force the library's NotificationManager into existence. Its constructor sets
        // Enabled = true, which through the ShowNavigationControls setter registers its own targets
        // (after ours, as required above) and enables the skip commands. It is otherwise created
        // lazily on the first UpdateNotification() call, which does not happen until playback starts -
        // so disabling the skip commands without forcing construction first is silently overwritten a
        // moment later, and the lock screen keeps showing "jump 10 seconds".
        _ = _mediaManager.Notification;

        RefreshTransportControls();

        _logger.LogInformation(
            "Apple remote command bridge started. NextTrackEnabled={NextTrackEnabled}; PreviousTrackEnabled={PreviousTrackEnabled}; SkipForwardEnabled={SkipForwardEnabled}; SkipBackwardEnabled={SkipBackwardEnabled}",
            commandCenter.NextTrackCommand.Enabled,
            commandCenter.PreviousTrackCommand.Enabled,
            commandCenter.SkipForwardCommand.Enabled,
            commandCenter.SkipBackwardCommand.Enabled);
    }

    /// <summary>
    /// Plugin.MediaManager's NotificationManager enables NextTrack/PreviousTrack *and*
    /// SkipForward/SkipBackward together, the latter with PreferredIntervals taken from
    /// MediaManagerBase's 10-second StepSize defaults. When both families are enabled iOS renders the
    /// skip-interval buttons and hides the track buttons, so the lock screen offered "jump 10 seconds"
    /// where Android offers previous/next. Turning the skip commands off puts the track buttons back.
    ///
    /// <para>
    /// Re-asserted on every state change rather than set once, because anything that assigns
    /// <c>Notification.Enabled</c> or <c>ShowNavigationControls</c> again re-runs the setter that
    /// turned them on.
    /// </para>
    /// </summary>
    public void RefreshTransportControls()
    {
        var commandCenter = MPRemoteCommandCenter.Shared;
        if (!commandCenter.SkipForwardCommand.Enabled && !commandCenter.SkipBackwardCommand.Enabled)
        {
            return;
        }

        commandCenter.SkipForwardCommand.Enabled = false;
        commandCenter.SkipBackwardCommand.Enabled = false;
    }

    private MPRemoteCommandHandlerStatus OnUserTerminalCommand(MPRemoteCommandEvent commandEvent)
    {
        // TogglePlayPause is stamped too. If the toggle meant "play", no terminal state follows and
        // the timestamp simply expires unused.
        try
        {
            UserTerminalCommandRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record a remote transport command.");
        }

        return MPRemoteCommandHandlerStatus.Success;
    }
}
#endif
