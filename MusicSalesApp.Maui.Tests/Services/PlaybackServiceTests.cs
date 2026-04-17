using Moq;
using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Tests.Services;

[TestFixture]
public class PlaybackServiceTests
{
    private Mock<IAuthService> _mockAuthService;
    private Mock<IMusicService> _mockMusicService;
    private PlaybackService _service;

    [SetUp]
    public void Setup()
    {
        _mockAuthService = new Mock<IAuthService>();
        _mockMusicService = new Mock<IMusicService>();
        _service = new PlaybackService(_mockAuthService.Object, _mockMusicService.Object);
    }

    // --- PlaySong ---

    [Test]
    public void PlaySong_SetsCurrentSongAndIsPlaying()
    {
        var song = new SongDto { Id = 1, SongTitle = "Test" };

        _service.PlaySong(song);

        Assert.That(_service.CurrentSong, Is.SameAs(song));
        Assert.That(_service.IsPlaying, Is.True);
    }

    [Test]
    public void PlaySong_TappingSameSongPauses()
    {
        var song = new SongDto { Id = 1, SongTitle = "Test" };
        _service.PlaySong(song);
        Assert.That(_service.IsPlaying, Is.True);

        _service.PlaySong(song);

        Assert.That(_service.IsPlaying, Is.False);
    }

    [Test]
    public void PlaySong_SwitchingToNewSongStartsPlaying()
    {
        var song1 = new SongDto { Id = 1, SongTitle = "First" };
        var song2 = new SongDto { Id = 2, SongTitle = "Second" };
        _service.PlaySong(song1);

        _service.PlaySong(song2);

        Assert.That(_service.CurrentSong, Is.SameAs(song2));
        Assert.That(_service.IsPlaying, Is.True);
    }

    [Test]
    public void PlaySong_FiresPlayRequestedEvent()
    {
        SongDto? firedSong = null;
        _service.PlayRequested += s => firedSong = s;
        var song = new SongDto { Id = 1, SongTitle = "Test" };

        _service.PlaySong(song);

        Assert.That(firedSong, Is.SameAs(song));
    }

    [Test]
    public void PlaySong_TappingSameSong_FiresPauseRequested()
    {
        bool pauseFired = false;
        _service.PauseRequested += () => pauseFired = true;
        var song = new SongDto { Id = 1, SongTitle = "Test" };
        _service.PlaySong(song);

        _service.PlaySong(song);

        Assert.That(pauseFired, Is.True);
    }

    // --- TogglePlayPause ---

    [Test]
    public void TogglePlayPause_TogglesIsPlaying()
    {
        var song = new SongDto { Id = 1, SongTitle = "Test" };
        _service.PlaySong(song);
        Assert.That(_service.IsPlaying, Is.True);

        _service.TogglePlayPause();
        Assert.That(_service.IsPlaying, Is.False);

        _service.TogglePlayPause();
        Assert.That(_service.IsPlaying, Is.True);
    }

    [Test]
    public void TogglePlayPause_DoesNothingWhenNoSong()
    {
        _service.TogglePlayPause();

        Assert.That(_service.IsPlaying, Is.False);
        Assert.That(_service.CurrentSong, Is.Null);
    }

    [Test]
    public void TogglePlayPause_FiresResumeAndPauseEvents()
    {
        bool resumeFired = false;
        bool pauseFired = false;
        _service.ResumeRequested += () => resumeFired = true;
        _service.PauseRequested += () => pauseFired = true;

        var song = new SongDto { Id = 1, SongTitle = "Test" };
        _service.PlaySong(song);

        // Pause
        _service.TogglePlayPause();
        Assert.That(pauseFired, Is.True);

        // Resume
        _service.TogglePlayPause();
        Assert.That(resumeFired, Is.True);
    }

    // --- Stop ---

    [Test]
    public void Stop_ClearsPlaybackState()
    {
        var song = new SongDto { Id = 1, SongTitle = "Test" };
        _service.PlaySong(song);

        _service.Stop();

        Assert.That(_service.IsPlaying, Is.False);
        Assert.That(_service.CurrentSong, Is.SameAs(song));
        Assert.That(_service.PlaybackProgress, Is.EqualTo(0));
        Assert.That(_service.FormattedPosition, Is.EqualTo("0:00"));
        Assert.That(_service.FormattedDuration, Is.EqualTo("0:00"));
    }

    [Test]
    public void Stop_ThenTogglePlayPause_ResumesPlayback()
    {
        bool resumeFired = false;
        _service.ResumeRequested += () => resumeFired = true;
        var song = new SongDto { Id = 1, SongTitle = "Test" };
        _service.PlaySong(song);

        _service.Stop();
        Assert.That(_service.IsPlaying, Is.False);

        _service.TogglePlayPause();
        Assert.That(_service.IsPlaying, Is.True);
        Assert.That(resumeFired, Is.True);
    }

    [Test]
    public void Stop_FiresStopRequestedEvent()
    {
        bool stopFired = false;
        _service.StopRequested += () => stopFired = true;
        var song = new SongDto { Id = 1, SongTitle = "Test" };
        _service.PlaySong(song);

        _service.Stop();

        Assert.That(stopFired, Is.True);
    }

    // --- ToggleRepeat ---

    [Test]
    public void ToggleRepeat_TogglesIsRepeatEnabled()
    {
        Assert.That(_service.IsRepeatEnabled, Is.False);

        _service.ToggleRepeat();
        Assert.That(_service.IsRepeatEnabled, Is.True);

        _service.ToggleRepeat();
        Assert.That(_service.IsRepeatEnabled, Is.False);
    }

    // --- UpdatePosition ---

    [Test]
    public void UpdatePosition_SetsProgressAndFormattedStrings()
    {
        _service.UpdatePosition(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(120));

        Assert.That(_service.PlaybackProgress, Is.EqualTo(0.25));
        Assert.That(_service.FormattedPosition, Is.EqualTo("0:30"));
        Assert.That(_service.FormattedDuration, Is.EqualTo("2:00"));
    }

    [Test]
    public void UpdatePosition_WithZeroDuration_SetsProgressToZero()
    {
        _service.UpdatePosition(TimeSpan.Zero, TimeSpan.Zero);

        Assert.That(_service.PlaybackProgress, Is.EqualTo(0));
    }

    // --- GetSeekPosition ---

    [Test]
    public void GetSeekPosition_ReturnsCorrectTimeSpan()
    {
        _service.UpdatePosition(TimeSpan.Zero, TimeSpan.FromSeconds(200));

        var seekPos = _service.GetSeekPosition(0.5);

        Assert.That(seekPos.TotalSeconds, Is.EqualTo(100));
    }

    // --- Seek ---

    [Test]
    public void Seek_FiresSeekRequestedEvent()
    {
        TimeSpan? seekPosition = null;
        _service.SeekRequested += pos => seekPosition = pos;
        _service.UpdatePosition(TimeSpan.Zero, TimeSpan.FromSeconds(200));

        _service.Seek(0.5);

        Assert.That(seekPosition, Is.Not.Null);
        Assert.That(seekPosition!.Value.TotalSeconds, Is.EqualTo(100));
    }

    // --- FormatDuration ---

    [Test]
    public void FormatDuration_FormatsMinutesAndSeconds()
    {
        Assert.That(_service.FormatDuration(65), Is.EqualTo("1:05"));
        Assert.That(_service.FormatDuration(180.5), Is.EqualTo("3:00"));
        Assert.That(_service.FormatDuration(3661), Is.EqualTo("1:01:01"));
    }

    [Test]
    public void FormatDuration_ReturnsPlaceholderForNull()
    {
        Assert.That(_service.FormatDuration(null), Is.EqualTo("0:00"));
    }

    [Test]
    public void FormatDuration_ReturnsZeroForNaN()
    {
        Assert.That(_service.FormatDuration(double.NaN), Is.EqualTo("0:00"));
    }

    [Test]
    public void FormatDuration_ReturnsZeroForInfinity()
    {
        Assert.That(_service.FormatDuration(double.PositiveInfinity), Is.EqualTo("0:00"));
        Assert.That(_service.FormatDuration(double.NegativeInfinity), Is.EqualTo("0:00"));
    }

    // --- OnMediaEnded ---

    [Test]
    public void OnMediaEnded_SetsIsPlayingFalse()
    {
        var song = new SongDto { Id = 1, SongTitle = "Test" };
        _service.PlaySong(song);

        _service.OnMediaEnded();

        Assert.That(_service.IsPlaying, Is.False);
    }

    [Test]
    public void OnMediaEnded_WithRepeat_RestartsPlayback()
    {
        TimeSpan? seekPosition = null;
        bool resumeFired = false;
        _service.SeekRequested += pos => seekPosition = pos;
        _service.ResumeRequested += () => resumeFired = true;

        var song = new SongDto { Id = 1, SongTitle = "Test" };
        _service.PlaySong(song);
        _service.ToggleRepeat();

        _service.OnMediaEnded();

        Assert.That(_service.IsPlaying, Is.True);
        Assert.That(seekPosition, Is.EqualTo(TimeSpan.Zero));
        Assert.That(resumeFired, Is.True);
    }

    // --- Stream tracking ---

    [Test]
    public void StreamTracking_RecordsStreamAfterQualifyingSeconds()
    {
        _service.SetStreamQualifyingSeconds(5);
        var song = new SongDto { Id = 10, SongTitle = "Test" };
        _service.PlaySong(song);

        for (int i = 1; i <= 6; i++)
        {
            _service.UpdatePosition(TimeSpan.FromSeconds(i), TimeSpan.FromSeconds(180));
        }

        _mockMusicService.Verify(s => s.RecordStreamAsync(10), Times.Once);
    }

    [Test]
    public void StreamTracking_DoesNotRecordBeforeThreshold()
    {
        _service.SetStreamQualifyingSeconds(30);
        var song = new SongDto { Id = 10, SongTitle = "Test" };
        _service.PlaySong(song);

        for (int i = 1; i <= 10; i++)
        {
            _service.UpdatePosition(TimeSpan.FromSeconds(i), TimeSpan.FromSeconds(180));
        }

        _mockMusicService.Verify(s => s.RecordStreamAsync(It.IsAny<int>()), Times.Never);
    }

    [Test]
    public void StreamTracking_RecordsOnlyOncePerSong()
    {
        _service.SetStreamQualifyingSeconds(3);
        var song = new SongDto { Id = 10, SongTitle = "Test" };
        _service.PlaySong(song);

        for (int i = 1; i <= 10; i++)
        {
            _service.UpdatePosition(TimeSpan.FromSeconds(i), TimeSpan.FromSeconds(180));
        }

        _mockMusicService.Verify(s => s.RecordStreamAsync(10), Times.Once);
    }

    [Test]
    public void StreamTracking_ResetsOnSongChange()
    {
        _service.SetStreamQualifyingSeconds(5);
        var song1 = new SongDto { Id = 10, SongTitle = "First" };
        var song2 = new SongDto { Id = 20, SongTitle = "Second" };

        _service.PlaySong(song1);
        for (int i = 1; i <= 3; i++)
        {
            _service.UpdatePosition(TimeSpan.FromSeconds(i), TimeSpan.FromSeconds(180));
        }

        _service.PlaySong(song2);
        for (int i = 1; i <= 6; i++)
        {
            _service.UpdatePosition(TimeSpan.FromSeconds(i), TimeSpan.FromSeconds(180));
        }

        _mockMusicService.Verify(s => s.RecordStreamAsync(10), Times.Never);
        _mockMusicService.Verify(s => s.RecordStreamAsync(20), Times.Once);
    }

    [Test]
    public void StreamTracking_IgnoresSeeks()
    {
        _service.SetStreamQualifyingSeconds(10);
        var song = new SongDto { Id = 10, SongTitle = "Test" };
        _service.PlaySong(song);

        _service.UpdatePosition(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(180));
        _service.UpdatePosition(TimeSpan.FromSeconds(50), TimeSpan.FromSeconds(180));

        _mockMusicService.Verify(s => s.RecordStreamAsync(It.IsAny<int>()), Times.Never);
    }

    [Test]
    public void StreamTracking_ResetsOnStop()
    {
        _service.SetStreamQualifyingSeconds(5);
        var song = new SongDto { Id = 10, SongTitle = "Test" };

        _service.PlaySong(song);
        for (int i = 1; i <= 3; i++)
        {
            _service.UpdatePosition(TimeSpan.FromSeconds(i), TimeSpan.FromSeconds(180));
        }

        _service.Stop();

        _service.PlaySong(song);
        for (int i = 1; i <= 3; i++)
        {
            _service.UpdatePosition(TimeSpan.FromSeconds(i), TimeSpan.FromSeconds(180));
        }

        _mockMusicService.Verify(s => s.RecordStreamAsync(It.IsAny<int>()), Times.Never);
    }

    [Test]
    public void StreamTracking_CreatorOwnSong_DoesNotRecordStream()
    {
        _mockAuthService.Setup(a => a.IsCreator).Returns(true);
        _mockAuthService.Setup(a => a.UserId).Returns(100);
        _service.SetStreamQualifyingSeconds(5);

        var song = new SongDto { Id = 1, SongTitle = "My Song", CreatorUserId = 100 };
        _service.PlaySong(song);

        for (int i = 1; i <= 10; i++)
        {
            _service.UpdatePosition(TimeSpan.FromSeconds(i), TimeSpan.FromSeconds(180));
        }

        _mockMusicService.Verify(s => s.RecordStreamAsync(It.IsAny<int>()), Times.Never);
    }

    // --- Preview limit ---

    [Test]
    public void PreviewLimit_NonSubscriber_PausesAt60Seconds()
    {
        _mockAuthService.Setup(a => a.HasActiveSubscription).Returns(false);
        var song = new SongDto { Id = 1, SongTitle = "Test" };
        _service.PlaySong(song);

        for (int i = 1; i <= 61; i++)
        {
            _service.UpdatePosition(TimeSpan.FromSeconds(i), TimeSpan.FromSeconds(180));
        }

        Assert.That(_service.IsPlaying, Is.False);
        Assert.That(_service.PreviewLimitReached, Is.True);
    }

    [Test]
    public void PreviewLimit_Subscriber_PlaysFullSong()
    {
        _mockAuthService.Setup(a => a.HasActiveSubscription).Returns(true);
        var song = new SongDto { Id = 1, SongTitle = "Test" };
        _service.PlaySong(song);

        for (int i = 1; i <= 120; i++)
        {
            _service.UpdatePosition(TimeSpan.FromSeconds(i), TimeSpan.FromSeconds(180));
        }

        Assert.That(_service.IsPlaying, Is.True);
        Assert.That(_service.PreviewLimitReached, Is.False);
    }

    [Test]
    public void PreviewLimit_CreatorOwnSong_PlaysFullSong()
    {
        _mockAuthService.Setup(a => a.HasActiveSubscription).Returns(false);
        _mockAuthService.Setup(a => a.IsCreator).Returns(true);
        _mockAuthService.Setup(a => a.UserId).Returns(100);

        var song = new SongDto { Id = 1, SongTitle = "My Song", CreatorUserId = 100 };
        _service.PlaySong(song);

        for (int i = 1; i <= 120; i++)
        {
            _service.UpdatePosition(TimeSpan.FromSeconds(i), TimeSpan.FromSeconds(180));
        }

        Assert.That(_service.IsPlaying, Is.True);
        Assert.That(_service.PreviewLimitReached, Is.False);
    }

    [Test]
    public void PreviewLimit_CreatorOtherSong_LimitedAt60s()
    {
        _mockAuthService.Setup(a => a.HasActiveSubscription).Returns(false);
        _mockAuthService.Setup(a => a.IsCreator).Returns(true);
        _mockAuthService.Setup(a => a.UserId).Returns(100);

        var song = new SongDto { Id = 1, SongTitle = "Other Song", CreatorUserId = 200 };
        _service.PlaySong(song);

        for (int i = 1; i <= 61; i++)
        {
            _service.UpdatePosition(TimeSpan.FromSeconds(i), TimeSpan.FromSeconds(180));
        }

        Assert.That(_service.IsPlaying, Is.False);
        Assert.That(_service.PreviewLimitReached, Is.True);
    }

    [Test]
    public void PreviewLimit_ResetsOnNewSong()
    {
        _mockAuthService.Setup(a => a.HasActiveSubscription).Returns(false);
        var song1 = new SongDto { Id = 1, SongTitle = "Song 1" };
        var song2 = new SongDto { Id = 2, SongTitle = "Song 2" };

        _service.PlaySong(song1);
        for (int i = 1; i <= 61; i++)
        {
            _service.UpdatePosition(TimeSpan.FromSeconds(i), TimeSpan.FromSeconds(180));
        }
        Assert.That(_service.PreviewLimitReached, Is.True);

        _service.PlaySong(song2);
        Assert.That(_service.PreviewLimitReached, Is.False);
        Assert.That(_service.IsPlaying, Is.True);
    }

    // --- StateChanged event ---

    [Test]
    public void StateChanged_FiredOnPropertyChange()
    {
        var changedProperties = new List<string>();
        _service.StateChanged += name => changedProperties.Add(name);

        var song = new SongDto { Id = 1, SongTitle = "Test" };
        _service.PlaySong(song);

        Assert.That(changedProperties, Does.Contain(nameof(IPlaybackService.CurrentSong)));
        Assert.That(changedProperties, Does.Contain(nameof(IPlaybackService.IsPlaying)));
    }
}
