using MusicSalesApp.Maui.Views;

namespace MusicSalesApp.Maui.Tests.Views;

[TestFixture]
public class NowPlayingScrollCoordinatorTests
{
    private const int SongA = 11;
    private const int SongB = 22;

    private bool _autoScrollEnabled;
    private DateTime _now;
    private NowPlayingScrollCoordinator _coordinator = null!;

    [SetUp]
    public void SetUp()
    {
        _autoScrollEnabled = true;
        _now = new DateTime(2026, 8, 29, 12, 0, 0, DateTimeKind.Utc);
        _coordinator = new NowPlayingScrollCoordinator(() => _autoScrollEnabled, () => _now);
    }

    [Test]
    public void ShouldScrollOnTrackChange_WhenAutoScrollIsOn_Scrolls()
    {
        Assert.That(_coordinator.ShouldScrollOnTrackChange(SongA), Is.True);
    }

    [Test]
    public void ShouldScrollOnTrackChange_WhenAutoScrollIsOff_DoesNot()
    {
        _autoScrollEnabled = false;

        Assert.That(_coordinator.ShouldScrollOnTrackChange(SongA), Is.False);
    }

    [Test]
    public void ShouldScrollOnTrackChange_WithNothingPlaying_DoesNot()
    {
        Assert.That(_coordinator.ShouldScrollOnTrackChange(0), Is.False);
    }

    [Test]
    public void ShouldScrollOnTrackChange_ForTheSameSongTwice_ScrollsOnce()
    {
        // PlaybackService.CurrentSong has no equality guard, so it re-raises for the SAME song every
        // time the music library pushes its filtered list into the queue. Without this, typing in
        // the title filter would yank the list on each keystroke.
        Assert.That(_coordinator.ShouldScrollOnTrackChange(SongA), Is.True);
        Assert.That(_coordinator.ShouldScrollOnTrackChange(SongA), Is.False);
    }

    [Test]
    public void ShouldScrollOnTrackChange_WhenTheTrackActuallyAdvances_ScrollsAgain()
    {
        _coordinator.ShouldScrollOnTrackChange(SongA);

        Assert.That(_coordinator.ShouldScrollOnTrackChange(SongB), Is.True);
    }

    [Test]
    public void ShouldScrollOnTrackChange_WithinTheGraceAfterAManualScroll_DoesNot()
    {
        _coordinator.NotifyScrolled();
        _now += TimeSpan.FromSeconds(1);

        Assert.That(_coordinator.ShouldScrollOnTrackChange(SongA), Is.False);
    }

    [Test]
    public void ShouldScrollOnTrackChange_OnceTheGraceHasElapsed_ResumesFollowing()
    {
        _coordinator.NotifyScrolled();
        _now += NowPlayingScrollCoordinator.DefaultManualScrollGrace + TimeSpan.FromSeconds(1);

        Assert.That(_coordinator.ShouldScrollOnTrackChange(SongA), Is.True);
    }

    [Test]
    public void NotifyScrolled_DuringOurOwnScroll_DoesNotStartAGraceWindow()
    {
        // ScrollTo is fire-and-forget, so the scroll we just asked for reports itself moments later.
        // Counting that as the user's would stop the list following the moment it first followed.
        _coordinator.BeginProgrammaticScroll();
        _coordinator.NotifyScrolled();
        _now += TimeSpan.FromMilliseconds(100);

        Assert.That(_coordinator.ShouldScrollOnTrackChange(SongA), Is.True);
    }

    [Test]
    public void NotifyScrolled_AfterOurScrollWindowHasClosed_CountsAsTheUsers()
    {
        _coordinator.BeginProgrammaticScroll();
        _now += NowPlayingScrollCoordinator.DefaultProgrammaticScrollWindow + TimeSpan.FromMilliseconds(1);
        _coordinator.NotifyScrolled();

        Assert.That(_coordinator.ShouldScrollOnTrackChange(SongA), Is.False);
    }

    [Test]
    public void ShouldScrollOnRequest_WhenAutoScrollIsOff_StillScrolls()
    {
        // Tapping the player bar is not unprompted scrolling - it is the listener asking.
        _autoScrollEnabled = false;

        Assert.That(_coordinator.ShouldScrollOnRequest(SongA), Is.True);
    }

    [Test]
    public void ShouldScrollOnRequest_WithinAManualScrollGrace_StillScrolls()
    {
        _coordinator.NotifyScrolled();

        Assert.That(_coordinator.ShouldScrollOnRequest(SongA), Is.True);
    }

    [Test]
    public void ShouldScrollOnRequest_ClearsTheGraceSoFollowingResumesImmediately()
    {
        _coordinator.NotifyScrolled();
        _coordinator.ShouldScrollOnRequest(SongA);

        Assert.That(_coordinator.ShouldScrollOnTrackChange(SongB), Is.True);
    }

    [Test]
    public void ShouldScrollOnRequest_ForTheSameSongTwice_ScrollsBothTimes()
    {
        // Unlike a track change, an explicit tap is never de-duped: the listener may have scrolled
        // away and be asking to be brought back to the very same song.
        Assert.That(_coordinator.ShouldScrollOnRequest(SongA), Is.True);
        Assert.That(_coordinator.ShouldScrollOnRequest(SongA), Is.True);
    }

    [Test]
    public void ShouldScrollOnRequest_WithNothingPlaying_DoesNot()
    {
        Assert.That(_coordinator.ShouldScrollOnRequest(0), Is.False);
    }

    [Test]
    public void Reset_LetsTheSameSongScrollAgainOnTheNextVisit()
    {
        _coordinator.ShouldScrollOnTrackChange(SongA);

        _coordinator.Reset();

        Assert.That(_coordinator.ShouldScrollOnTrackChange(SongA), Is.True);
    }
}
