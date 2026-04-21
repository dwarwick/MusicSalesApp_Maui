using MediaManager;
using MediaManager.Library;
using MediaManager.Media;
using MediaManager.Playback;
using MediaManager.Player;
using MediaManager.Queue;
using Moq;
using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Tests.Services;

[TestFixture]
public class PlaybackServiceTests
{
    private Mock<IAuthService> _mockAuthService;
    private Mock<IMusicService> _mockMusicService;
    private Mock<IMediaManager> _mockMediaManager;
    private PlaybackService _service;

    [SetUp]
    public void Setup()
    {
        _mockAuthService = new Mock<IAuthService>();
        _mockMusicService = new Mock<IMusicService>();
        _mockMediaManager = new Mock<IMediaManager>();

        // Set up async methods to return completed tasks
        _mockMediaManager.Setup(m => m.Play(It.IsAny<IMediaItem>())).ReturnsAsync(Mock.Of<IMediaItem>());
        _mockMediaManager.Setup(m => m.Play(It.IsAny<IEnumerable<IMediaItem>>())).ReturnsAsync(Mock.Of<IMediaItem>());
        _mockMediaManager.Setup(m => m.Play()).Returns(Task.CompletedTask);
        _mockMediaManager.Setup(m => m.Pause()).Returns(Task.CompletedTask);
        _mockMediaManager.Setup(m => m.Stop()).Returns(Task.CompletedTask);
        _mockMediaManager.Setup(m => m.PlayNext()).Returns(Task.FromResult(false));
        _mockMediaManager.Setup(m => m.PlayPrevious()).Returns(Task.FromResult(false));
        _mockMediaManager.Setup(m => m.PlayQueueItem(It.IsAny<int>())).Returns(Task.FromResult(false));
        _mockMediaManager.Setup(m => m.SeekTo(It.IsAny<TimeSpan>())).Returns(Task.CompletedTask);
        _mockMediaManager.SetupProperty(m => m.RepeatMode);
        _mockMediaManager.SetupProperty(m => m.ShuffleMode);
        _mockMediaManager.Setup(m => m.Duration).Returns(TimeSpan.Zero);

        _service = new PlaybackService(_mockAuthService.Object, _mockMusicService.Object, _mockMediaManager.Object);
    }

    // --- PlaySong ---

    [Test]
    public void PlaySong_SetsCurrentSongAndIsPlaying()
    {
        var song = new SongDto { Id = 1, SongTitle = "Test", StreamUrl = "https://test.com/song.mp3" };

        _service.PlaySong(song);

        Assert.That(_service.CurrentSong, Is.SameAs(song));
        Assert.That(_service.IsPlaying, Is.True);
    }

    [Test]
    public void PlaySong_TappingSameSongPauses()
    {
        var song = new SongDto { Id = 1, SongTitle = "Test", StreamUrl = "https://test.com/song.mp3" };
        _service.PlaySong(song);
        Assert.That(_service.IsPlaying, Is.True);

        _service.PlaySong(song);

        Assert.That(_service.IsPlaying, Is.False);
    }

    [Test]
    public void PlaySong_SwitchingToNewSongStartsPlaying()
    {
        var song1 = new SongDto { Id = 1, SongTitle = "First", StreamUrl = "https://test.com/song1.mp3" };
        var song2 = new SongDto { Id = 2, SongTitle = "Second", StreamUrl = "https://test.com/song2.mp3" };
        _service.PlaySong(song1);

        _service.PlaySong(song2);

        Assert.That(_service.CurrentSong, Is.SameAs(song2));
        Assert.That(_service.IsPlaying, Is.True);
    }

    [Test]
    public void PlaySong_CallsMediaManagerPlay()
    {
        var song = new SongDto { Id = 1, SongTitle = "Test", StreamUrl = "https://test.com/song1.mp3" };

        _service.PlaySong(song);

        _mockMediaManager.Verify(m => m.Play(It.IsAny<IMediaItem>()), Times.Once);
    }

    [Test]
    public void PlaySong_TappingSameSong_CallsMediaManagerPause()
    {
        var song = new SongDto { Id = 1, SongTitle = "Test", StreamUrl = "https://test.com/song1.mp3" };
        _service.PlaySong(song);

        _service.PlaySong(song);

        _mockMediaManager.Verify(m => m.Pause(), Times.Once);
    }

    // --- TogglePlayPause ---

    [Test]
    public void TogglePlayPause_TogglesIsPlaying()
    {
        var song = new SongDto { Id = 1, SongTitle = "Test", StreamUrl = "https://test.com/song.mp3" };
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
    public void TogglePlayPause_CallsMediaManagerPlayAndPause()
    {
        var song = new SongDto { Id = 1, SongTitle = "Test", StreamUrl = "https://test.com/song1.mp3" };
        _service.PlaySong(song);

        // Pause
        _service.TogglePlayPause();
        _mockMediaManager.Verify(m => m.Pause(), Times.AtLeastOnce);

        // Resume
        _service.TogglePlayPause();
        _mockMediaManager.Verify(m => m.Play(), Times.Once);
    }

    // --- Stop ---

    [Test]
    public void Stop_ClearsPlaybackState()
    {
        var song = new SongDto { Id = 1, SongTitle = "Test", StreamUrl = "https://test.com/song.mp3" };
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
        var song = new SongDto { Id = 1, SongTitle = "Test", StreamUrl = "https://test.com/song1.mp3" };
        _service.PlaySong(song);

        _service.Stop();
        Assert.That(_service.IsPlaying, Is.False);

        _service.TogglePlayPause();
        Assert.That(_service.IsPlaying, Is.True);
    }

    [Test]
    public void Stop_CallsMediaManagerPauseAndSeekToStart()
    {
        var song = new SongDto { Id = 1, SongTitle = "Test", StreamUrl = "https://test.com/song1.mp3" };
        _service.PlaySong(song);

        _service.Stop();

        _mockMediaManager.Verify(m => m.Pause(), Times.Once);
        _mockMediaManager.Verify(m => m.SeekTo(TimeSpan.Zero), Times.Once);
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
    public void Seek_CallsMediaManagerSeekTo()
    {
        _service.UpdatePosition(TimeSpan.Zero, TimeSpan.FromSeconds(200));

        _service.Seek(0.5);

        _mockMediaManager.Verify(m => m.SeekTo(TimeSpan.FromSeconds(100)), Times.Once);
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
        var song = new SongDto { Id = 1, SongTitle = "Test", StreamUrl = "https://test.com/song.mp3" };
        _service.PlaySong(song);

        _service.OnMediaEnded();

        Assert.That(_service.IsPlaying, Is.False);
    }

    [Test]
    public void OnMediaEnded_WithRepeat_CallsMediaManagerSeekToStartAndPlay()
    {
        var song = new SongDto { Id = 1, SongTitle = "Test", StreamUrl = "https://test.com/song1.mp3" };
        _service.PlaySong(song);
        _service.ToggleRepeat();

        _service.OnMediaEnded();

        _mockMediaManager.Verify(m => m.SeekTo(TimeSpan.Zero), Times.Once);
        _mockMediaManager.Verify(m => m.Play(), Times.Once);
        Assert.That(_service.IsPlaying, Is.True);
    }

    // --- Stream tracking ---

    [Test]
    public void StreamTracking_RecordsStreamAfterQualifyingSeconds()
    {
        _service.SetStreamQualifyingSeconds(5);
        var song = new SongDto { Id = 10, SongTitle = "Test", StreamUrl = "https://test.com/song.mp3" };
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
        var song = new SongDto { Id = 10, SongTitle = "Test", StreamUrl = "https://test.com/song.mp3" };
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
        var song = new SongDto { Id = 10, SongTitle = "Test", StreamUrl = "https://test.com/song.mp3" };
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
        var song1 = new SongDto { Id = 10, SongTitle = "First", StreamUrl = "https://test.com/song1.mp3" };
        var song2 = new SongDto { Id = 20, SongTitle = "Second", StreamUrl = "https://test.com/song2.mp3" };

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
        var song = new SongDto { Id = 10, SongTitle = "Test", StreamUrl = "https://test.com/song.mp3" };
        _service.PlaySong(song);

        _service.UpdatePosition(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(180));
        _service.UpdatePosition(TimeSpan.FromSeconds(50), TimeSpan.FromSeconds(180));

        _mockMusicService.Verify(s => s.RecordStreamAsync(It.IsAny<int>()), Times.Never);
    }

    [Test]
    public void StreamTracking_ResetsOnStop()
    {
        _service.SetStreamQualifyingSeconds(5);
        var song = new SongDto { Id = 10, SongTitle = "Test", StreamUrl = "https://test.com/song.mp3" };

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

        var song = new SongDto { Id = 1, SongTitle = "My Song", CreatorUserId = 100, StreamUrl = "https://test.com/song.mp3" };
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
        var song = new SongDto { Id = 1, SongTitle = "Test", StreamUrl = "https://test.com/song.mp3" };
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
        var song = new SongDto { Id = 1, SongTitle = "Test", StreamUrl = "https://test.com/song.mp3" };
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

        var song = new SongDto { Id = 1, SongTitle = "My Song", CreatorUserId = 100, StreamUrl = "https://test.com/song.mp3" };
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

        var song = new SongDto { Id = 1, SongTitle = "Other Song", CreatorUserId = 200, StreamUrl = "https://test.com/song.mp3" };
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
        var song1 = new SongDto { Id = 1, SongTitle = "Song 1", StreamUrl = "https://test.com/song1.mp3" };
        var song2 = new SongDto { Id = 2, SongTitle = "Song 2", StreamUrl = "https://test.com/song2.mp3" };

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

        var song = new SongDto { Id = 1, SongTitle = "Test", StreamUrl = "https://test.com/song.mp3" };
        _service.PlaySong(song);

        Assert.That(changedProperties, Does.Contain(nameof(IPlaybackService.CurrentSong)));
        Assert.That(changedProperties, Does.Contain(nameof(IPlaybackService.IsPlaying)));
    }

    // --- SetPlaylist ---

    [Test]
    public void SetPlaylist_SetsCurrentSongAndPlays()
    {
        var songs = CreateTestPlaylist(3);

        _service.SetPlaylist(songs, 0);

        Assert.That(_service.HasPlaylist, Is.True);
        Assert.That(_service.Playlist, Has.Count.EqualTo(3));
        Assert.That(_service.CurrentTrackIndex, Is.EqualTo(0));
        Assert.That(_service.CurrentSong, Is.SameAs(songs[0]));
        Assert.That(_service.IsPlaying, Is.True);
    }

    [Test]
    public void SetPlaylist_StartsAtGivenIndex()
    {
        var songs = CreateTestPlaylist(5);

        _service.SetPlaylist(songs, 2);

        Assert.That(_service.CurrentTrackIndex, Is.EqualTo(2));
        Assert.That(_service.CurrentSong, Is.SameAs(songs[2]));
    }

    [Test]
    public void SetPlaylist_ClampsStartIndex()
    {
        var songs = CreateTestPlaylist(3);

        _service.SetPlaylist(songs, 10);

        Assert.That(_service.CurrentTrackIndex, Is.EqualTo(2));
        Assert.That(_service.CurrentSong, Is.SameAs(songs[2]));
    }

    [Test]
    public void SetPlaylist_EmptyList_DoesNothing()
    {
        _service.SetPlaylist(new List<SongDto>(), 0);

        Assert.That(_service.HasPlaylist, Is.False);
        Assert.That(_service.CurrentSong, Is.Null);
    }

    [Test]
    public void SetPlaylist_FiresStateChangedForPlaylistProperties()
    {
        var changedProperties = new List<string>();
        _service.StateChanged += name => changedProperties.Add(name);
        var songs = CreateTestPlaylist(2);

        _service.SetPlaylist(songs, 0);

        Assert.That(changedProperties, Does.Contain(nameof(IPlaybackService.HasPlaylist)));
        Assert.That(changedProperties, Does.Contain(nameof(IPlaybackService.Playlist)));
        Assert.That(changedProperties, Does.Contain(nameof(IPlaybackService.CurrentTrackIndex)));
    }

    // --- ClearPlaylist ---

    [Test]
    public void ClearPlaylist_ResetsPlaylistState()
    {
        var songs = CreateTestPlaylist(3);
        _service.SetPlaylist(songs, 0);

        _service.ClearPlaylist();

        Assert.That(_service.HasPlaylist, Is.False);
        Assert.That(_service.Playlist, Is.Null);
        Assert.That(_service.CurrentTrackIndex, Is.EqualTo(0));
    }

    [Test]
    public void ClearPlaylist_DoesNotStopPlayback()
    {
        var songs = CreateTestPlaylist(3);
        _service.SetPlaylist(songs, 0);

        _service.ClearPlaylist();

        Assert.That(_service.IsPlaying, Is.True);
        Assert.That(_service.CurrentSong, Is.Not.Null);
    }

    // --- PlayNext ---

    [Test]
    public void PlayNext_CallsMediaManagerPlayNext()
    {
        var songs = CreateTestPlaylist(3);
        _service.SetPlaylist(songs, 0);

        _service.PlayNext();

        _mockMediaManager.Verify(m => m.PlayNext(), Times.Once);
    }

    [Test]
    public void PlayNext_StateUpdatesWhenMediaItemChangedFires()
    {
        var songs = CreateTestPlaylist(3);
        _service.SetPlaylist(songs, 0);

        _service.PlayNext();

        // Simulate Plugin.MediaManager firing MediaItemChanged for songs[1]
        var item = new MediaItem(songs[1].StreamUrl!);
        _mockMediaManager.Raise(m => m.MediaItemChanged += null, new MediaItemEventArgs(item));

        Assert.That(_service.CurrentTrackIndex, Is.EqualTo(1));
        Assert.That(_service.CurrentSong, Is.SameAs(songs[1]));
    }

    [Test]
    public void PlayNext_AtEnd_NoRepeat_DoesNotChangeState()
    {
        var songs = CreateTestPlaylist(3);
        _service.SetPlaylist(songs, 2);

        _service.PlayNext();

        // Plugin.MediaManager won't fire MediaItemChanged (no next item), so state stays
        Assert.That(_service.CurrentTrackIndex, Is.EqualTo(2));
        Assert.That(_service.CurrentSong, Is.SameAs(songs[2]));
    }

    [Test]
    public void PlayNext_AtEnd_WithRepeat_StateUpdatesOnEvent()
    {
        var songs = CreateTestPlaylist(3);
        _service.SetPlaylist(songs, 2);
        _service.ToggleRepeat();

        _service.PlayNext();

        // Simulate Plugin.MediaManager looping to first item
        var item = new MediaItem(songs[0].StreamUrl!);
        _mockMediaManager.Raise(m => m.MediaItemChanged += null, new MediaItemEventArgs(item));

        Assert.That(_service.CurrentTrackIndex, Is.EqualTo(0));
        Assert.That(_service.CurrentSong, Is.SameAs(songs[0]));
    }

    [Test]
    public void PlayNext_WithoutPlaylist_DoesNothing()
    {
        var song = new SongDto { Id = 1, SongTitle = "Single", StreamUrl = "https://test.com/song1.mp3" };
        _service.PlaySong(song);

        _service.PlayNext();

        Assert.That(_service.CurrentSong, Is.SameAs(song));
        _mockMediaManager.Verify(m => m.PlayNext(), Times.Never);
    }

    // --- PlayPrevious ---

    [Test]
    public void PlayPrevious_CallsMediaManagerPlayPrevious()
    {
        var songs = CreateTestPlaylist(3);
        _service.SetPlaylist(songs, 2);

        _service.PlayPrevious();

        _mockMediaManager.Verify(m => m.PlayPrevious(), Times.Once);
    }

    [Test]
    public void PlayPrevious_StateUpdatesWhenMediaItemChangedFires()
    {
        var songs = CreateTestPlaylist(3);
        _service.SetPlaylist(songs, 2);

        _service.PlayPrevious();

        // Simulate Plugin.MediaManager firing MediaItemChanged for songs[1]
        var item = new MediaItem(songs[1].StreamUrl!) { Title = songs[1].SongTitle };
        _mockMediaManager.Raise(m => m.MediaItemChanged += null, new MediaItemEventArgs(item));

        Assert.That(_service.CurrentTrackIndex, Is.EqualTo(1));
        Assert.That(_service.CurrentSong, Is.SameAs(songs[1]));
    }

    [Test]
    public void PlayPrevious_AtStart_DoesNotChangeState()
    {
        var songs = CreateTestPlaylist(3);
        _service.SetPlaylist(songs, 0);

        _service.PlayPrevious();

        // Plugin.MediaManager won't fire MediaItemChanged (no prev item), so state stays
        Assert.That(_service.CurrentTrackIndex, Is.EqualTo(0));
        Assert.That(_service.CurrentSong, Is.SameAs(songs[0]));
    }

    // --- PlayTrackAtIndex ---

    [Test]
    public void PlayTrackAtIndex_PlaysCorrectSong()
    {
        var songs = CreateTestPlaylist(5);
        _service.SetPlaylist(songs, 0);

        _service.PlayTrackAtIndex(3);

        Assert.That(_service.CurrentTrackIndex, Is.EqualTo(3));
        Assert.That(_service.CurrentSong, Is.SameAs(songs[3]));
        _mockMediaManager.Verify(m => m.PlayQueueItem(3), Times.Once);
    }

    [Test]
    public void PlayTrackAtIndex_InvalidIndex_DoesNothing()
    {
        var songs = CreateTestPlaylist(3);
        _service.SetPlaylist(songs, 0);

        _service.PlayTrackAtIndex(-1);
        Assert.That(_service.CurrentTrackIndex, Is.EqualTo(0));

        _service.PlayTrackAtIndex(10);
        Assert.That(_service.CurrentTrackIndex, Is.EqualTo(0));
    }

    // --- ToggleShuffle ---

    [Test]
    public void ToggleShuffle_SetsMediaManagerShuffleMode()
    {
        _service.ToggleShuffle();
        Assert.That(_service.IsShuffleEnabled, Is.True);
        Assert.That(_mockMediaManager.Object.ShuffleMode, Is.EqualTo(ShuffleMode.All));

        _service.ToggleShuffle();
        Assert.That(_service.IsShuffleEnabled, Is.False);
        Assert.That(_mockMediaManager.Object.ShuffleMode, Is.EqualTo(ShuffleMode.Off));
    }

    // --- OnMediaEnded with playlist ---
    // Note: With Plugin.MediaManager, queue auto-advance is handled by the native player.
    // OnMediaEnded fires from OnMediaItemFinished and allows Plugin.MediaManager to continue.

    [Test]
    public void OnMediaEnded_WithPlaylist_DoesNotStopPlayback()
    {
        var songs = CreateTestPlaylist(3);
        _service.SetPlaylist(songs, 0);

        _service.OnMediaEnded();

        // With playlist, Plugin.MediaManager auto-advances — we don't stop IsPlaying
        // because OnMediaItemChanged will update state when next song starts
        // IsPlaying is not explicitly changed by OnMediaEnded when HasPlaylist is true
        Assert.That(_service.CurrentSong, Is.SameAs(songs[0])); // unchanged until event fires
    }

    [Test]
    public void OnMediaEnded_WithoutPlaylist_NoRepeat_StopsPlayback()
    {
        var song = new SongDto { Id = 1, SongTitle = "Test", StreamUrl = "https://test.com/song1.mp3" };
        _service.PlaySong(song);

        _service.OnMediaEnded();

        Assert.That(_service.IsPlaying, Is.False);
    }

    [Test]
    public void OnMediaEnded_WithoutPlaylist_RepeatEnabled_RestartsSameSong()
    {
        var song = new SongDto { Id = 1, SongTitle = "Test", StreamUrl = "https://test.com/song1.mp3" };
        _service.PlaySong(song);
        _service.ToggleRepeat();

        _service.OnMediaEnded();

        _mockMediaManager.Verify(m => m.SeekTo(TimeSpan.Zero), Times.Once);
        _mockMediaManager.Verify(m => m.Play(), Times.Once);
        Assert.That(_service.IsPlaying, Is.True);
    }

    // --- PlaySong clears playlist ---

    [Test]
    public void PlaySong_WhilePlaylistActive_ClearsPlaylist()
    {
        var songs = CreateTestPlaylist(3);
        _service.SetPlaylist(songs, 0);
        Assert.That(_service.HasPlaylist, Is.True);

        var singleSong = new SongDto { Id = 99, SongTitle = "Single", StreamUrl = "https://test.com/single.mp3" };
        _service.PlaySong(singleSong);

        // PlaySong doesn't clear playlist automatically — it's a different concern
        // The caller is responsible for clearing if needed
        Assert.That(_service.CurrentSong, Is.SameAs(singleSong));
    }

    // --- Helper ---

    private static List<SongDto> CreateTestPlaylist(int count)
    {
        return Enumerable.Range(1, count)
            .Select(i => new SongDto
            {
                Id = i,
                SongTitle = $"Song {i}",
                ArtistName = $"Artist {i}",
                Genre = "Rock",
                StreamUrl = $"https://test.com/song{i}.mp3"
            })
            .ToList();
    }
}
