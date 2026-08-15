#if !ANDROID
namespace MusicSalesApp.Maui.Services;

/// <summary>
/// Reports what the OS transport controls - the iOS lock screen, Control Center, headset buttons -
/// asked for, and configures which of those controls are offered.
///
/// <para>
/// Android needs no equivalent. Media3 hands every state change a <c>PlayWhenReadyChangeReason</c>, so
/// <c>AndroidMedia3PlaybackRuntime</c> can already tell a user pause from an unexpected stall.
/// Plugin.MediaManager reports no reason at all, and its remote-command handlers call
/// <c>MediaManager.Pause()</c> directly - bypassing <c>PlaybackService</c> entirely - so without this
/// bridge a lock-screen pause is indistinguishable from a stall and gets "recovered" by restarting the
/// queue. That is the exact behaviour PLAYBACK_CACHE_ARCHITECTURE.md forbids:
/// "User-requested pause/stop must never be interpreted as an unexpected playback failure that should
/// restart the queue."
/// </para>
/// </summary>
public interface IPlaybackRemoteCommandBridge
{
    /// <summary>
    /// Raised when the OS transport controls request a pause or stop, just before the media library
    /// acts on it. Consumers use it to mark the terminal state that follows as user-requested.
    /// </summary>
    event EventHandler? UserTerminalCommandRequested;

    /// <summary>
    /// Hooks the OS transport controls. Safe to call more than once; only the first call takes effect.
    /// </summary>
    void Start();

    /// <summary>
    /// Re-asserts which transport controls are offered. Cheap and idempotent, and called on every
    /// playback state change because the media library rebuilds its notification manager lazily and
    /// resets the command set when it does.
    /// </summary>
    void RefreshTransportControls();
}

/// <summary>Windows, and anywhere else with no OS transport controls to observe.</summary>
public sealed class NoOpPlaybackRemoteCommandBridge : IPlaybackRemoteCommandBridge
{
    public event EventHandler? UserTerminalCommandRequested
    {
        add { }
        remove { }
    }

    public void Start()
    {
    }

    public void RefreshTransportControls()
    {
    }
}
#endif
