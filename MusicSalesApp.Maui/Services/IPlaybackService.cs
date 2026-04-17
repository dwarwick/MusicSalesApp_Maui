using CommunityToolkit.Mvvm.ComponentModel;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Services;

/// <summary>
/// Singleton service managing all audio playback state, shared between
/// MusicLibraryPage and SongPlayerPage. Does NOT own the MediaElement —
/// the page code-behind bridges MediaElement events ↔ this service via events.
/// </summary>
public interface IPlaybackService
{
    // --- Observable state (UI binds to these) ---
    SongDto? CurrentSong { get; }
    bool IsPlaying { get; }
    double PlaybackProgress { get; }
    string FormattedPosition { get; }
    string FormattedDuration { get; }
    bool IsRepeatEnabled { get; }
    bool PreviewLimitReached { get; }

    // --- Commands / actions ---
    void PlaySong(SongDto song);
    void TogglePlayPause();
    void Stop();
    void ToggleRepeat();

    /// <summary>Called by MediaElement timer tick to update position/duration.</summary>
    void UpdatePosition(TimeSpan position, TimeSpan duration);

    /// <summary>Returns the seek TimeSpan for a given slider progress (0..1).</summary>
    TimeSpan GetSeekPosition(double progress);

    /// <summary>Seeks to the given slider progress (0..1), fires SeekRequested.</summary>
    void Seek(double progress);

    /// <summary>Called by MediaElement when media ends.</summary>
    void OnMediaEnded();

    /// <summary>Called once at startup with the server's qualifying-seconds threshold.</summary>
    void SetStreamQualifyingSeconds(int seconds);

    // --- Events to drive MediaElement (code-behind subscribes) ---

    /// <summary>Fired when a new song should be loaded and played.</summary>
    event Action<SongDto>? PlayRequested;

    /// <summary>Fired when playback should resume (from paused).</summary>
    event Action? ResumeRequested;

    /// <summary>Fired when playback should pause.</summary>
    event Action? PauseRequested;

    /// <summary>Fired when playback should stop and source cleared.</summary>
    event Action? StopRequested;

    /// <summary>Fired when a seek is needed (e.g. repeat restart).</summary>
    event Action<TimeSpan>? SeekRequested;

    /// <summary>Fired when a subscribe CTA should be shown.</summary>
    event Func<Task>? ShowSubscribeCtaRequested;

    /// <summary>Fired when any observable property changes.</summary>
    event Action<string>? StateChanged;

    /// <summary>Formats seconds to m:ss or h:mm:ss.</summary>
    string FormatDuration(double? seconds);
}
