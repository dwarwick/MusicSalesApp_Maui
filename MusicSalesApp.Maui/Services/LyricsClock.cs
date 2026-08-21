#nullable enable
namespace MusicSalesApp.Maui.Services;

/// <summary>
/// Turns the player's once-a-second position samples into a continuously readable clock.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> Every platform here reports position about once per second - Android
/// polls ExoPlayer on a one-second loop, Apple rides a similar heartbeat - and there is no faster
/// source. That is fine for a progress bar and useless for lyrics: a second is two to four sung
/// words, so highlighting straight off the samples lights words up late and in visible jumps.
/// </para>
/// <para>
/// So each sample becomes an <em>anchor</em>, and the time between anchors is filled in from a
/// monotonic host clock. The player stays the source of truth; this only fills the gaps.
/// </para>
/// <para>
/// Pure and time-injectable so its behaviour can be asserted rather than eyeballed - the failure
/// modes here (running backwards, twitching once a second, sticking after a seek) are exactly the
/// kind that are obvious on a device and invisible in a screenshot.
/// </para>
/// </remarks>
internal sealed class LyricsClock
{
    /// <summary>
    /// How far the estimate may be from a fresh anchor before we stop easing and just take it.
    /// </summary>
    /// <remarks>
    /// Below this, a gap is ordinary sampling jitter and correcting sharply would be the visible
    /// artefact. Above it, something really moved - a seek, a stall, a track change - and holding
    /// the old estimate would be worse than a jump. One second is the sampling interval itself:
    /// anything larger cannot be explained by jitter.
    /// </remarks>
    private const long HardResyncThresholdMs = 1000;

    private readonly Func<long> _hostClockMs;

    private long _anchorPositionMs;
    private long _anchorHostMs;
    private long _lastReportedMs;
    private bool _running;

    /// <param name="hostClockMs">
    /// A monotonic millisecond source. Production passes a Stopwatch; tests pass a counter they
    /// control, which is the whole point of it being a parameter.
    /// </param>
    public LyricsClock(Func<long> hostClockMs)
    {
        _hostClockMs = hostClockMs;
    }

    /// <summary>Whether the clock is currently advancing.</summary>
    public bool IsRunning => _running;

    /// <summary>
    /// The current playback position in milliseconds, interpolated when running.
    /// </summary>
    /// <remarks>
    /// Monotonic while running. A lyric highlight that steps backwards reads as a glitch even
    /// when the underlying number is more accurate, so the estimate is never allowed to retreat
    /// except through an explicit <see cref="Reset"/>.
    /// </remarks>
    public long CurrentMs
    {
        get
        {
            if (!_running)
            {
                return _lastReportedMs;
            }

            var elapsed = _hostClockMs() - _anchorHostMs;
            var estimate = _anchorPositionMs + (elapsed < 0 ? 0 : elapsed);

            if (estimate < _lastReportedMs)
            {
                return _lastReportedMs;
            }

            _lastReportedMs = estimate;
            return estimate;
        }
    }

    /// <summary>
    /// Take a fresh sample from the player.
    /// </summary>
    /// <remarks>
    /// Small differences are absorbed by re-anchoring without letting the reported value move
    /// backwards; a large one is taken at face value, because at that size the sample is right
    /// and the estimate is stale.
    /// </remarks>
    public void Anchor(TimeSpan position)
    {
        var sampleMs = (long)position.TotalMilliseconds;
        if (sampleMs < 0)
        {
            sampleMs = 0;
        }

        if (!_running)
        {
            // Not advancing, so there is no estimate to protect - take the sample as-is.
            _anchorPositionMs = sampleMs;
            _anchorHostMs = _hostClockMs();
            _lastReportedMs = sampleMs;
            return;
        }

        var estimate = CurrentMs;
        var drift = sampleMs - estimate;

        if (drift > HardResyncThresholdMs || drift < -HardResyncThresholdMs)
        {
            Reset(position);
            Start();
            return;
        }

        // Re-anchor on whichever is further along. Taking the sample when it is behind would drag
        // the highlight backwards over a difference too small to be worth seeing.
        _anchorPositionMs = sampleMs > estimate ? sampleMs : estimate;
        _anchorHostMs = _hostClockMs();
        _lastReportedMs = _anchorPositionMs;
    }

    /// <summary>
    /// Jump to a known position, discarding the current estimate.
    /// </summary>
    /// <remarks>
    /// For seeks and track changes, where the old estimate is not merely stale but wrong. Note a
    /// seek does not produce a corrected sample immediately - the player reports the new position
    /// on its next tick, up to a second later - so callers should reset with the position they
    /// asked for rather than waiting to be told.
    /// </remarks>
    public void Reset(TimeSpan position)
    {
        var ms = (long)position.TotalMilliseconds;
        _anchorPositionMs = ms < 0 ? 0 : ms;
        _anchorHostMs = _hostClockMs();
        _lastReportedMs = _anchorPositionMs;
    }

    /// <summary>Begin advancing from the current anchor.</summary>
    public void Start()
    {
        if (_running)
        {
            return;
        }

        // Re-stamp the host time, or the pause would be counted as elapsed playback.
        _anchorPositionMs = _lastReportedMs;
        _anchorHostMs = _hostClockMs();
        _running = true;
    }

    /// <summary>Stop advancing, holding the position reached.</summary>
    public void Stop()
    {
        if (!_running)
        {
            return;
        }

        _lastReportedMs = CurrentMs;
        _running = false;
    }
}
