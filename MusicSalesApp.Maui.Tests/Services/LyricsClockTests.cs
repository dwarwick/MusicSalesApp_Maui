using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.Tests.Services;

/// <summary>
/// The clock that fills the gaps between the player's once-a-second position samples.
/// </summary>
/// <remarks>
/// Every failure mode here is obvious on a device and invisible in a screenshot - a highlight
/// that twitches once a second, walks backwards, or sticks after a scrub. The host clock is
/// injected precisely so those can be asserted instead of watched for.
/// </remarks>
[TestFixture]
public class LyricsClockTests
{
    private long _hostMs;
    private LyricsClock _clock = null!;

    [SetUp]
    public void SetUp()
    {
        _hostMs = 0;
        _clock = new LyricsClock(() => _hostMs);
    }

    private void AdvanceHost(long ms) => _hostMs += ms;

    [Test]
    public void ReportsTheAnchor_BeforeItStarts()
    {
        _clock.Reset(TimeSpan.FromSeconds(5));

        Assert.That(_clock.CurrentMs, Is.EqualTo(5_000));
    }

    [Test]
    public void DoesNotAdvance_WhileStopped()
    {
        _clock.Reset(TimeSpan.FromSeconds(5));
        AdvanceHost(3_000);

        Assert.That(_clock.CurrentMs, Is.EqualTo(5_000), "A paused song does not move.");
    }

    [Test]
    public void InterpolatesBetweenSamples()
    {
        // The whole point: the player says nothing for a second, and the highlight still moves.
        _clock.Reset(TimeSpan.FromSeconds(10));
        _clock.Start();

        AdvanceHost(250);
        Assert.That(_clock.CurrentMs, Is.EqualTo(10_250));

        AdvanceHost(250);
        Assert.That(_clock.CurrentMs, Is.EqualTo(10_500));
    }

    [Test]
    public void PausingHoldsThePosition_AndResumingDoesNotCountThePause()
    {
        _clock.Reset(TimeSpan.FromSeconds(10));
        _clock.Start();
        AdvanceHost(400);

        _clock.Stop();
        var atPause = _clock.CurrentMs;
        AdvanceHost(30_000);   // a long pause

        Assert.That(_clock.CurrentMs, Is.EqualTo(atPause), "Held while paused.");

        _clock.Start();
        AdvanceHost(100);

        Assert.That(_clock.CurrentMs, Is.EqualTo(atPause + 100),
            "Resumed from where it stopped - the pause is not replayed as elapsed playback.");
    }

    [Test]
    public void NeverRunsBackwards_WhenASampleArrivesSlightlyBehind()
    {
        // The failure this guards: sampling jitter drags the highlight back a word, which reads
        // as a glitch even though the sample is the more authoritative number.
        _clock.Reset(TimeSpan.FromSeconds(10));
        _clock.Start();
        AdvanceHost(900);

        var before = _clock.CurrentMs;      // ~10_900
        _clock.Anchor(TimeSpan.FromMilliseconds(10_600));   // 300ms behind

        Assert.That(_clock.CurrentMs, Is.GreaterThanOrEqualTo(before));
    }

    [Test]
    public void AcceptsASampleThatIsAhead_WithoutWaiting()
    {
        _clock.Reset(TimeSpan.FromSeconds(10));
        _clock.Start();
        AdvanceHost(100);

        _clock.Anchor(TimeSpan.FromMilliseconds(10_800));

        Assert.That(_clock.CurrentMs, Is.EqualTo(10_800),
            "Being behind the audio is the one error worth correcting immediately.");
    }

    [Test]
    public void ResyncsHard_WhenTheSampleIsFurtherOffThanJitterExplains()
    {
        // A stall, or a seek whose corrected sample arrives late. Holding the old estimate here
        // would leave the lyrics somewhere else in the song entirely.
        _clock.Reset(TimeSpan.FromSeconds(10));
        _clock.Start();
        AdvanceHost(200);

        _clock.Anchor(TimeSpan.FromSeconds(45));

        Assert.That(_clock.CurrentMs, Is.EqualTo(45_000));
    }

    [Test]
    public void ResyncsHard_WhenTheSampleIsFarBehind()
    {
        // The monotonic guard must not outrank a genuine backwards jump, or a seek to an earlier
        // point in the song would leave the lyrics stuck at the old position forever.
        _clock.Reset(TimeSpan.FromSeconds(40));
        _clock.Start();
        AdvanceHost(200);

        _clock.Anchor(TimeSpan.FromSeconds(5));

        Assert.That(_clock.CurrentMs, Is.EqualTo(5_000));
    }

    [Test]
    public void ResetJumps_EvenWhenTheDifferenceIsSmall()
    {
        // Seeking is not sampling jitter. A scrub of half a second is still a scrub, and the
        // player will not report the corrected position for up to a second.
        _clock.Reset(TimeSpan.FromSeconds(10));
        _clock.Start();
        AdvanceHost(100);

        _clock.Reset(TimeSpan.FromMilliseconds(9_800));

        Assert.That(_clock.CurrentMs, Is.EqualTo(9_800));
    }

    [Test]
    public void KeepsRunningAfterAHardResync()
    {
        _clock.Reset(TimeSpan.FromSeconds(10));
        _clock.Start();
        AdvanceHost(200);
        _clock.Anchor(TimeSpan.FromSeconds(45));

        AdvanceHost(300);

        Assert.Multiple(() =>
        {
            Assert.That(_clock.IsRunning, Is.True);
            Assert.That(_clock.CurrentMs, Is.EqualTo(45_300), "Still interpolating afterwards.");
        });
    }

    [Test]
    public void IsMonotonicAcrossARealisticRun()
    {
        // Ten seconds of 1 Hz samples with a little jitter each way, read at ~60Hz. The reported
        // time must never step backwards at any point.
        _clock.Reset(TimeSpan.Zero);
        _clock.Start();

        var jitter = new[] { 40L, -30L, 15L, -55L, 0L, 25L, -20L, 60L, -10L, 5L };
        var last = 0L;

        for (var second = 0; second < 10; second++)
        {
            for (var frame = 0; frame < 60; frame++)
            {
                AdvanceHost(16);
                var now = _clock.CurrentMs;
                Assert.That(now, Is.GreaterThanOrEqualTo(last), $"Went backwards at {second}s frame {frame}.");
                last = now;
            }

            _clock.Anchor(TimeSpan.FromMilliseconds((second + 1) * 1_000 + jitter[second]));
            var afterAnchor = _clock.CurrentMs;
            Assert.That(afterAnchor, Is.GreaterThanOrEqualTo(last), $"Went backwards on the {second}s anchor.");
            last = afterAnchor;
        }
    }

    [Test]
    public void ClampsNegativeInput()
    {
        _clock.Reset(TimeSpan.FromSeconds(-5));

        Assert.That(_clock.CurrentMs, Is.Zero);
    }
}
