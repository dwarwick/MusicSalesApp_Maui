using CommunityToolkit.Mvvm.ComponentModel;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Services;

public enum PlaybackRequestFailureReason
{
    UnavailableOffline,
    UnplayableTrackSkipped,
    UnexpectedError
}

public sealed record PlaybackRequestFailedEventArgs(
    int SongId,
    PlaybackRequestFailureReason Reason);

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
    PlaybackPreparationState PreparationState { get; }
    QueuePreparationResult? LastQueuePreparationResult { get; }

    // --- Playlist state ---
    List<SongDto>? Playlist { get; }
    int CurrentTrackIndex { get; }
    bool HasPlaylist { get; }
    bool IsShuffleEnabled { get; }

    // --- Commands / actions ---
    void PlaySong(SongDto song);
    void TogglePlayPause();
    void Stop();
    void ToggleRepeat();

    /// <summary>Loads a playlist and starts playing at the given index.</summary>
    void SetPlaylist(List<SongDto> songs, int startIndex);

    /// <summary>Loads a playlist and starts playing at the given index with a log source description.</summary>
    void SetPlaylist(List<SongDto> songs, int startIndex, string queueSourceDescription);

    /// <summary>Loads a playlist using the requested start behavior.</summary>
    void SetPlaylist(List<SongDto> songs, int startIndex, PlaybackQueueStartBehavior startBehavior);

    /// <summary>Loads a playlist using the requested start behavior with a log source description.</summary>
    void SetPlaylist(List<SongDto> songs, int startIndex, PlaybackQueueStartBehavior startBehavior, string queueSourceDescription);

    /// <summary>Exits playlist mode without stopping playback.</summary>
    void ClearPlaylist();

    /// <summary>Advances to the next track (respects shuffle/repeat).</summary>
    void PlayNext();

    /// <summary>Goes to the previous track.</summary>
    void PlayPrevious();

    /// <summary>Jumps to a specific track in the playlist by index.</summary>
    void PlayTrackAtIndex(int index);

    /// <summary>Toggles shuffle on/off, regenerating shuffle order as needed.</summary>
    void ToggleShuffle();

    /// <summary>Returns the seek TimeSpan for a given slider progress (0..1).</summary>
    TimeSpan GetSeekPosition(double progress);

    /// <summary>Seeks to the given slider progress (0..1).</summary>
    void Seek(double progress);

    /// <summary>Called once at startup with the server's qualifying-seconds threshold.</summary>
    void SetStreamQualifyingSeconds(int seconds);

    /// <summary>Clears any active preview restriction after a successful subscription upgrade.</summary>
    void HandleSubscriptionActivated();

    // --- Events ---

    /// <summary>Fired when a subscribe CTA should be shown.</summary>
    event Func<Task>? ShowSubscribeCtaRequested;

    /// <summary>Fired when any observable property changes.</summary>
    event Action<string>? StateChanged;

    /// <summary>Fired when a requested song cannot be started without an internet connection.</summary>
    event EventHandler<PlaybackRequestFailedEventArgs>? PlaybackRequestFailed;

    /// <summary>Formats seconds to m:ss or h:mm:ss.</summary>
    string FormatDuration(double? seconds);
}
