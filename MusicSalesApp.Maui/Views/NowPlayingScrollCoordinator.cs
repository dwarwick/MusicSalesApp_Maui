namespace MusicSalesApp.Maui.Views;

/// <summary>
/// Decides WHETHER a song list should scroll to the playing song. Holds no view and touches no MAUI
/// type, so the rules below are unit-tested rather than inferred from behaviour on a device - the
/// same split as <see cref="NowPlayingDrawerController"/>.
/// </summary>
internal sealed class NowPlayingScrollCoordinator
{
    /// <summary>
    /// How long a manual scroll suppresses automatic scrolling. Matches the lyrics panel's window
    /// (<c>LyricsView.ManualScrollGrace</c>) so the two auto-following surfaces feel the same.
    /// </summary>
    public static readonly TimeSpan DefaultManualScrollGrace = TimeSpan.FromSeconds(4);

    /// <summary>
    /// How long after issuing a scroll its own Scrolled callbacks are disregarded.
    /// </summary>
    /// <remarks>
    /// A window rather than a bool flag because <c>CollectionView.ScrollTo</c> is fire-and-forget:
    /// it returns before the animation starts, so a flag cleared on the next line is already false
    /// by the time the scroll it describes reports itself. The lyrics panel can use a plain flag
    /// only because <c>ScrollView.ScrollToAsync</c> is awaitable and this is not.
    /// </remarks>
    public static readonly TimeSpan DefaultProgrammaticScrollWindow = TimeSpan.FromMilliseconds(700);

    private readonly Func<bool> _isAutoScrollEnabled;
    private readonly Func<DateTime> _utcNow;
    private readonly TimeSpan _manualScrollGrace;
    private readonly TimeSpan _programmaticScrollWindow;
    private DateTime _lastManualScrollUtc = DateTime.MinValue;
    private DateTime _programmaticScrollUntilUtc = DateTime.MinValue;
    private int _lastScrolledSongId;

    public NowPlayingScrollCoordinator(
        Func<bool> isAutoScrollEnabled,
        Func<DateTime>? utcNow = null,
        TimeSpan? manualScrollGrace = null,
        TimeSpan? programmaticScrollWindow = null)
    {
        _isAutoScrollEnabled = isAutoScrollEnabled;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _manualScrollGrace = manualScrollGrace ?? DefaultManualScrollGrace;
        _programmaticScrollWindow = programmaticScrollWindow ?? DefaultProgrammaticScrollWindow;
    }

    /// <summary>
    /// Call immediately before issuing a scroll, so the callbacks it causes are not mistaken for
    /// the user's - which would stop the list following the moment it first followed.
    /// </summary>
    public void BeginProgrammaticScroll() =>
        _programmaticScrollUntilUtc = _utcNow() + _programmaticScrollWindow;

    /// <summary>
    /// Whether an automatic scroll should follow a reported current-song change.
    /// </summary>
    /// <remarks>
    /// The song-id check is what makes this safe to drive from the playback service.
    /// <c>PlaybackService.CurrentSong</c>'s setter has no equality guard, so it raises a change on
    /// every assignment - including re-assigning the SAME song, which the music library does after
    /// every filter change when it pushes the visible list into the queue. Without this, typing in
    /// the title filter would yank the list back on each keystroke.
    /// </remarks>
    public bool ShouldScrollOnTrackChange(int songId)
    {
        if (songId <= 0 || !_isAutoScrollEnabled())
        {
            return false;
        }

        if (songId == _lastScrolledSongId)
        {
            return false;
        }

        if (_utcNow() - _lastManualScrollUtc < _manualScrollGrace)
        {
            return false;
        }

        _lastScrolledSongId = songId;
        return true;
    }

    /// <summary>
    /// Whether an explicit request - a tap on the player bar, or the user ticking Auto-scroll -
    /// should scroll. It always should: the setting governs UNPROMPTED scrolling only, and asking
    /// to be shown the song is not unprompted. Clearing the grace window is the point, so a tap
    /// during a browse is obeyed instead of being swallowed as if it were the queue advancing.
    /// </summary>
    public bool ShouldScrollOnRequest(int songId)
    {
        if (songId <= 0)
        {
            return false;
        }

        _lastManualScrollUtc = DateTime.MinValue;
        _lastScrolledSongId = songId;
        return true;
    }

    /// <summary>
    /// Report that the list scrolled. Only a scroll outside the window opened by
    /// <see cref="BeginProgrammaticScroll"/> counts as the user's.
    /// </summary>
    public void NotifyScrolled()
    {
        var now = _utcNow();
        if (now < _programmaticScrollUntilUtc)
        {
            return;
        }

        _lastManualScrollUtc = now;
    }

    /// <summary>
    /// Forget which song was last scrolled to, so leaving and returning to a page scrolls again.
    /// </summary>
    public void Reset()
    {
        _lastScrolledSongId = 0;
        _lastManualScrollUtc = DateTime.MinValue;
        _programmaticScrollUntilUtc = DateTime.MinValue;
    }
}
