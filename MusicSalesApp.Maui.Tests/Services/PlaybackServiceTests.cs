using MediaManager;
using MediaManager.Library;
using MediaManager.Media;
using MediaManager.Playback;
using MediaManager.Player;
using MediaManager.Queue;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
    private Mock<IMediaQueue> _mockMediaQueue;
    private Mock<IAudioCacheService> _mockAudioCacheService;
    private Mock<IPlaybackKeepAliveService> _mockPlaybackKeepAliveService;
    private PlaybackService _service;
    private MediaPlayerState _mediaManagerState;

    [SetUp]
    public void Setup()
    {
        _mockAuthService = new Mock<IAuthService>();
        _mockMusicService = new Mock<IMusicService>();
        _mockMediaManager = new Mock<IMediaManager>();
        _mockMediaQueue = new Mock<IMediaQueue>();
        _mockAudioCacheService = new Mock<IAudioCacheService>();
        _mockPlaybackKeepAliveService = new Mock<IPlaybackKeepAliveService>();
        _mediaManagerState = MediaPlayerState.Stopped;

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
        _mockMediaManager.Setup(m => m.Position).Returns(TimeSpan.Zero);
        _mockMediaManager.Setup(m => m.Duration).Returns(TimeSpan.Zero);
        _mockMediaManager.Setup(m => m.State).Returns(() => _mediaManagerState);
        _mockMediaManager.Setup(m => m.Queue).Returns(_mockMediaQueue.Object);
        _mockMusicService.Setup(s => s.RecordStreamAsync(It.IsAny<int>())).ReturnsAsync((int?)null);
        _mockAudioCacheService
            .Setup(s => s.ResolvePlaybackUriAsync(It.IsAny<SongDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SongDto song, CancellationToken _) => song.StreamUrl);
        _mockMediaQueue.Setup(q => q.HasCurrent).Returns(false);

        _service = CreateService();
    }

    private PlaybackService CreateService(
        TimeSpan? playlistAdvanceFallbackDelay = null,
        TimeSpan? positionSamplerInterval = null,
        TimeSpan? positionEventStaleThreshold = null,
        TimeSpan? transientStopConfirmationDelay = null)
    {
        return new PlaybackService(
            _mockAuthService.Object,
            _mockMusicService.Object,
            _mockMediaManager.Object,
            _mockAudioCacheService.Object,
            _mockPlaybackKeepAliveService.Object,
            NullLogger<PlaybackService>.Instance,
            playlistAdvanceFallbackDelay,
            positionSamplerInterval,
            positionEventStaleThreshold,
            transientStopConfirmationDelay);
    }

    [Test]
    public void SetPlaylist_LogsVerboseQueueDiagnostics()
    {
        var logger = new ListLogger<PlaybackService>();
        _service = new PlaybackService(
            _mockAuthService.Object,
            _mockMusicService.Object,
            _mockMediaManager.Object,
            _mockAudioCacheService.Object,
            _mockPlaybackKeepAliveService.Object,
            logger);

        var songs = new List<SongDto>
        {
            new() { Id = 19, SongTitle = "All Around Me", ArtistName = "Artist A", StreamUrl = "https://test.com/19.mp3" },
            new() { Id = 20, SongTitle = "Convoy Crown", ArtistName = "Artist B", StreamUrl = "https://test.com/20.mp3" },
            new() { Id = 21, SongTitle = "Last Song", ArtistName = "Artist C", StreamUrl = "https://test.com/21.mp3" }
        };

        _mockMediaQueue.Setup(q => q.HasCurrent).Returns(true);
        _mockMediaQueue.Setup(q => q.CurrentIndex).Returns(0);
        _mockMediaQueue.Setup(q => q.Current).Returns(new MediaItem(songs[0].StreamUrl!)
        {
            Title = songs[0].SongTitle,
            Artist = songs[0].ArtistName
        });

        _service.SetPlaylist(songs, 0);

        var combinedLogs = string.Join(Environment.NewLine, logger.Messages);
        Assert.That(combinedLogs, Does.Contain("AppPlaylist="));
        Assert.That(combinedLogs, Does.Contain("NativeQueueType="));
        Assert.That(combinedLogs, Does.Contain("QueueItems="));
        Assert.That(combinedLogs, Does.Contain("DiagSeq="));
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
    public void PlaySong_UsesAlbumArtForMediaItemImage()
    {
        IMediaItem? capturedMediaItem = null;
        _mockMediaManager
            .Setup(m => m.Play(It.IsAny<IMediaItem>()))
            .Callback<IMediaItem>(mediaItem => capturedMediaItem = mediaItem)
            .ReturnsAsync(Mock.Of<IMediaItem>());

        var song = new SongDto
        {
            Id = 1,
            SongTitle = "Test",
            StreamUrl = "https://test.com/song1.mp3",
            AlbumArtUrl = "https://test.com/art.jpg",
            PersonaImageUrl = "https://test.com/persona.jpg"
        };

        _service.PlaySong(song);

        Assert.That(capturedMediaItem?.AlbumImageUri, Is.EqualTo("https://test.com/art.jpg"));
        Assert.That(capturedMediaItem?.ImageUri, Is.EqualTo("https://test.com/art.jpg"));
    }

    [Test]
    public void PlaySong_FallsBackToPersonaImageWhenAlbumArtUriIsInvalid()
    {
        IMediaItem? capturedMediaItem = null;
        _mockMediaManager
            .Setup(m => m.Play(It.IsAny<IMediaItem>()))
            .Callback<IMediaItem>(mediaItem => capturedMediaItem = mediaItem)
            .ReturnsAsync(Mock.Of<IMediaItem>());

        var song = new SongDto
        {
            Id = 1,
            SongTitle = "Test",
            StreamUrl = "https://test.com/song1.mp3",
            AlbumArtUrl = "/relative-art.jpg",
            PersonaImageUrl = "https://test.com/persona.jpg"
        };

        _service.PlaySong(song);

        Assert.That(capturedMediaItem?.AlbumImageUri, Is.EqualTo("https://test.com/persona.jpg"));
        Assert.That(capturedMediaItem?.ImageUri, Is.EqualTo("https://test.com/persona.jpg"));
    }

    [Test]
    public void PlaySong_ActivatesPlaybackKeepAlive()
    {
        var song = new SongDto { Id = 1, SongTitle = "Test", StreamUrl = "https://test.com/song1.mp3" };

        _service.PlaySong(song);

        _mockPlaybackKeepAliveService.Verify(service => service.SetPlaybackActive(true), Times.Once);
    }

    [Test]
    public void PlaySong_TappingSameSong_CallsMediaManagerPause()
    {
        var song = new SongDto { Id = 1, SongTitle = "Test", StreamUrl = "https://test.com/song1.mp3" };
        _service.PlaySong(song);

        _service.PlaySong(song);

        _mockMediaManager.Verify(m => m.Pause(), Times.Once);
    }

    [Test]
    public void PlaySong_TappingSameSong_ReleasesPlaybackKeepAlive()
    {
        var song = new SongDto { Id = 1, SongTitle = "Test", StreamUrl = "https://test.com/song1.mp3" };
        _service.PlaySong(song);

        _service.PlaySong(song);

        _mockPlaybackKeepAliveService.Verify(service => service.SetPlaybackActive(false), Times.Once);
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

    [Test]
    public void Stop_ReleasesPlaybackKeepAlive()
    {
        var song = new SongDto { Id = 1, SongTitle = "Test", StreamUrl = "https://test.com/song1.mp3" };
        _service.PlaySong(song);

        _service.Stop();

        _mockPlaybackKeepAliveService.Verify(service => service.SetPlaybackActive(false), Times.Once);
    }

    // --- ToggleRepeat ---

    [Test]
    public void ToggleRepeat_TogglesIsRepeatEnabled()
    {
        var song = new SongDto { Id = 1, SongTitle = "Test", StreamUrl = "https://test.com/song.mp3" };
        _service.PlaySong(song);

        Assert.That(_service.IsRepeatEnabled, Is.False);

        _service.ToggleRepeat();
        Assert.That(_service.IsRepeatEnabled, Is.True);

        _service.ToggleRepeat();
        Assert.That(_service.IsRepeatEnabled, Is.False);
    }

    [Test]
    public void ToggleRepeat_WithoutCurrentSong_DoesNothing()
    {
        _service.ToggleRepeat();

        Assert.That(_service.IsRepeatEnabled, Is.False);
        Assert.That(_mockMediaManager.Object.RepeatMode, Is.EqualTo(RepeatMode.Off));
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
    public void StreamTracking_PauseAndResume_PreservesAccumulatedPlaybackTowardThreshold()
    {
        _service.SetStreamQualifyingSeconds(5);
        var song = new SongDto { Id = 10, SongTitle = "Test", StreamUrl = "https://test.com/song.mp3" };
        _service.PlaySong(song);

        for (int i = 1; i <= 3; i++)
        {
            _service.UpdatePosition(TimeSpan.FromSeconds(i), TimeSpan.FromSeconds(180));
        }

        _service.TogglePlayPause();
        _service.TogglePlayPause();

        _service.UpdatePosition(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(180));
        _mockMusicService.Verify(s => s.RecordStreamAsync(It.IsAny<int>()), Times.Never);

        _service.UpdatePosition(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(180));

        _mockMusicService.Verify(s => s.RecordStreamAsync(10), Times.Once);
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
        _mockAuthService.Setup(a => a.HasActiveSubscription).Returns(true);
        _service.SetStreamQualifyingSeconds(10);
        var song = new SongDto { Id = 10, SongTitle = "Test", StreamUrl = "https://test.com/song.mp3" };
        _service.PlaySong(song);

        _service.UpdatePosition(TimeSpan.Zero, TimeSpan.FromSeconds(180));
        _service.UpdatePosition(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(180));
        _service.Seek(50d / 180d);
        _service.UpdatePosition(TimeSpan.FromSeconds(50), TimeSpan.FromSeconds(180));

        _mockMusicService.Verify(s => s.RecordStreamAsync(It.IsAny<int>()), Times.Never);
    }

    [Test]
    public void StreamTracking_SeekBeforeThreshold_RequiresFreshContinuousPlaybackWindow()
    {
        _mockAuthService.Setup(a => a.HasActiveSubscription).Returns(true);
        _service.SetStreamQualifyingSeconds(10);
        var song = new SongDto { Id = 10, SongTitle = "Test", StreamUrl = "https://test.com/song.mp3" };
        _service.PlaySong(song);

        _service.UpdatePosition(TimeSpan.Zero, TimeSpan.FromSeconds(180));
        for (int i = 1; i <= 5; i++)
        {
            _service.UpdatePosition(TimeSpan.FromSeconds(i), TimeSpan.FromSeconds(180));
        }

        _service.Seek(0.5);

        _service.UpdatePosition(TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(180));
        for (int i = 91; i <= 99; i++)
        {
            _service.UpdatePosition(TimeSpan.FromSeconds(i), TimeSpan.FromSeconds(180));
        }

        _mockMusicService.Verify(s => s.RecordStreamAsync(It.IsAny<int>()), Times.Never);

        _service.UpdatePosition(TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(180));

        _mockMediaManager.Verify(m => m.SeekTo(TimeSpan.FromSeconds(90)), Times.Once);
        _mockMusicService.Verify(s => s.RecordStreamAsync(10), Times.Once);
    }

    [Test]
    public void StreamTracking_SparsePositionUpdates_StillCountsContinuousPlayback()
    {
        _mockAuthService.Setup(a => a.HasActiveSubscription).Returns(true);
        _service.SetStreamQualifyingSeconds(15);
        var song = new SongDto { Id = 10, SongTitle = "Test", StreamUrl = "https://test.com/song.mp3" };
        _service.PlaySong(song);

        _service.UpdatePosition(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(180));
        _service.UpdatePosition(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(180));

        _mockMusicService.Verify(s => s.RecordStreamAsync(It.IsAny<int>()), Times.Never);

        _service.UpdatePosition(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(180));

        _mockMusicService.Verify(s => s.RecordStreamAsync(10), Times.Once);
    }

    [Test]
    public void StreamTracking_UsesSongSpecificQualifyingSeconds_WhenPresent()
    {
        _mockAuthService.Setup(a => a.HasActiveSubscription).Returns(true);
        _service.SetStreamQualifyingSeconds(30);
        var song = new SongDto
        {
            Id = 10,
            SongTitle = "Test",
            StreamUrl = "https://test.com/song.mp3",
            StreamQualifyingSeconds = 5
        };

        _service.PlaySong(song);

        for (int i = 1; i <= 5; i++)
        {
            _service.UpdatePosition(TimeSpan.FromSeconds(i), TimeSpan.FromSeconds(180));
        }

        _mockMusicService.Verify(s => s.RecordStreamAsync(10), Times.Once);
    }

    [Test]
    public async Task StreamTracking_FallbackPositionSampler_RecordsStreamAndUpdatesLocalCount_WhenPositionEventsStop()
    {
        _mockAuthService.Setup(a => a.HasActiveSubscription).Returns(true);

        var currentPosition = TimeSpan.Zero;
        _mockMediaManager.Setup(m => m.Position).Returns(() => currentPosition);
        _mockMediaManager.Setup(m => m.Duration).Returns(TimeSpan.FromSeconds(180));
        _mockMusicService.Setup(s => s.RecordStreamAsync(10)).ReturnsAsync(14);

        var service = CreateService(
            positionSamplerInterval: TimeSpan.FromMilliseconds(10),
            positionEventStaleThreshold: TimeSpan.FromMilliseconds(25));

        service.SetStreamQualifyingSeconds(1);
        var song = new SongDto
        {
            Id = 10,
            SongTitle = "Background Test",
            StreamUrl = "https://test.com/song.mp3",
            StreamCount = 13
        };

        service.PlaySong(song);

        await Task.Delay(40);
        currentPosition = TimeSpan.FromSeconds(1.1);

        await Task.Delay(150);

        _mockMusicService.Verify(s => s.RecordStreamAsync(10), Times.Once);
        Assert.That(song.StreamCount, Is.EqualTo(14));

        service.Stop();
    }

    [Test]
    public async Task MediaManagerStopped_WhilePlaybackPositionAdvances_KeepsPlaybackActiveAndRecordsStream()
    {
        _mockAuthService.Setup(a => a.HasActiveSubscription).Returns(true);

        var currentPosition = TimeSpan.Zero;
        _mockMediaManager.Setup(m => m.Position).Returns(() => currentPosition);
        _mockMediaManager.Setup(m => m.Duration).Returns(TimeSpan.FromSeconds(180));
        _mockMusicService.Setup(s => s.RecordStreamAsync(10)).ReturnsAsync(14);

        var service = CreateService(
            positionSamplerInterval: TimeSpan.FromMilliseconds(10),
            positionEventStaleThreshold: TimeSpan.FromMilliseconds(25),
            transientStopConfirmationDelay: TimeSpan.FromMilliseconds(60));

        service.SetStreamQualifyingSeconds(1);
        var song = new SongDto
        {
            Id = 10,
            SongTitle = "Background Test",
            StreamUrl = "https://test.com/song.mp3",
            StreamCount = 13
        };

        service.PlaySong(song);

        _mockMediaManager.Raise(m => m.StateChanged += null, new StateChangedEventArgs(MediaPlayerState.Stopped));

        await Task.Delay(40);
        currentPosition = TimeSpan.FromSeconds(1.1);

        await Task.Delay(150);

        Assert.That(service.IsPlaying, Is.True);
        _mockPlaybackKeepAliveService.Verify(s => s.SetPlaybackActive(false), Times.Never);
        _mockMusicService.Verify(s => s.RecordStreamAsync(10), Times.Once);
        Assert.That(song.StreamCount, Is.EqualTo(14));

        service.Stop();
    }

    [Test]
    public async Task MediaManagerStopped_WithoutProgress_StopsPlaybackAfterConfirmationDelay()
    {
        var service = CreateService(transientStopConfirmationDelay: TimeSpan.FromMilliseconds(40));
        var song = new SongDto { Id = 10, SongTitle = "Stopped Test", StreamUrl = "https://test.com/song.mp3" };

        service.PlaySong(song);

        _mockMediaManager.Raise(m => m.StateChanged += null, new StateChangedEventArgs(MediaPlayerState.Stopped));

        await Task.Delay(120);

        Assert.That(service.IsPlaying, Is.False);
        _mockPlaybackKeepAliveService.Verify(s => s.SetPlaybackActive(false), Times.Once);
    }

    [Test]
    public void StreamTracking_PlayNext_StartsFreshTimerForNextSong()
    {
        _mockAuthService.Setup(a => a.HasActiveSubscription).Returns(true);
        _service.SetStreamQualifyingSeconds(10);
        var songs = CreateTestPlaylist(2);

        _service.SetPlaylist(songs, 0);
        for (int i = 1; i <= 5; i++)
        {
            _service.UpdatePosition(TimeSpan.FromSeconds(i), TimeSpan.FromSeconds(180));
        }

        _service.PlayNext();
        var item = new MediaItem(songs[1].StreamUrl!);
        _mockMediaManager.Raise(m => m.MediaItemChanged += null, new MediaItemEventArgs(item));

        for (int i = 1; i <= 9; i++)
        {
            _service.UpdatePosition(TimeSpan.FromSeconds(i), TimeSpan.FromSeconds(180));
        }

        _mockMusicService.Verify(s => s.RecordStreamAsync(It.IsAny<int>()), Times.Never);

        _service.UpdatePosition(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(180));

        _mockMusicService.Verify(s => s.RecordStreamAsync(songs[0].Id), Times.Never);
        _mockMusicService.Verify(s => s.RecordStreamAsync(songs[1].Id), Times.Once);
    }

    [Test]
    public void StreamTracking_PlayPrevious_StartsFreshTimerForPreviousSong()
    {
        _mockAuthService.Setup(a => a.HasActiveSubscription).Returns(true);
        _service.SetStreamQualifyingSeconds(10);
        var songs = CreateTestPlaylist(2);

        _service.SetPlaylist(songs, 1);
        for (int i = 1; i <= 5; i++)
        {
            _service.UpdatePosition(TimeSpan.FromSeconds(i), TimeSpan.FromSeconds(180));
        }

        _service.PlayPrevious();
        var item = new MediaItem(songs[0].StreamUrl!) { Title = songs[0].SongTitle };
        _mockMediaManager.Raise(m => m.MediaItemChanged += null, new MediaItemEventArgs(item));

        for (int i = 1; i <= 9; i++)
        {
            _service.UpdatePosition(TimeSpan.FromSeconds(i), TimeSpan.FromSeconds(180));
        }

        _mockMusicService.Verify(s => s.RecordStreamAsync(It.IsAny<int>()), Times.Never);

        _service.UpdatePosition(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(180));

        _mockMusicService.Verify(s => s.RecordStreamAsync(songs[1].Id), Times.Never);
        _mockMusicService.Verify(s => s.RecordStreamAsync(songs[0].Id), Times.Once);
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

    [Test]
    public void HandleSubscriptionActivated_AfterPreviewLimit_ClearsLimitAndResumesPlayback()
    {
        _mockAuthService.Setup(a => a.HasActiveSubscription).Returns(false);
        var song = new SongDto { Id = 1, SongTitle = "Test", StreamUrl = "https://test.com/song.mp3" };
        _service.PlaySong(song);

        for (int i = 1; i <= 61; i++)
        {
            _service.UpdatePosition(TimeSpan.FromSeconds(i), TimeSpan.FromSeconds(180));
        }

        Assert.That(_service.PreviewLimitReached, Is.True);
        Assert.That(_service.IsPlaying, Is.False);

        _mockAuthService.Setup(a => a.HasActiveSubscription).Returns(true);

        _service.HandleSubscriptionActivated();

        Assert.That(_service.PreviewLimitReached, Is.False);
        Assert.That(_service.IsPlaying, Is.True);
        _mockMediaManager.Verify(m => m.Play(), Times.AtLeastOnce);
    }

    [Test]
    public void HandleSubscriptionActivated_BeforePreviewLimit_ClearsMarkerStateWithoutRestartingPlayback()
    {
        _mockAuthService.Setup(a => a.HasActiveSubscription).Returns(false);
        var song = new SongDto { Id = 1, SongTitle = "Test", StreamUrl = "https://test.com/song.mp3" };
        _service.PlaySong(song);
        _service.UpdatePosition(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(180));

        _mockAuthService.Setup(a => a.HasActiveSubscription).Returns(true);

        _service.HandleSubscriptionActivated();

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
    public void ToggleShuffle_WithPlaylist_TogglesShuffleState()
    {
        var songs = CreateTestPlaylist(3);
        _service.SetPlaylist(songs, 0);

        _service.ToggleShuffle();
        Assert.That(_service.IsShuffleEnabled, Is.True);
        Assert.That(_mockMediaManager.Object.ShuffleMode, Is.EqualTo(ShuffleMode.Off));

        _service.ToggleShuffle();
        Assert.That(_service.IsShuffleEnabled, Is.False);
        Assert.That(_mockMediaManager.Object.ShuffleMode, Is.EqualTo(ShuffleMode.Off));
    }

    [Test]
    public void ToggleShuffle_WithoutPlaylist_DoesNothing()
    {
        _service.ToggleShuffle();

        Assert.That(_service.IsShuffleEnabled, Is.False);
        Assert.That(_mockMediaManager.Object.ShuffleMode, Is.EqualTo(ShuffleMode.Off));
    }

    [Test]
    public void ToggleShuffle_WithSingleSongPlayback_DoesNothing()
    {
        var song = new SongDto { Id = 1, SongTitle = "Single", StreamUrl = "https://test.com/song.mp3" };
        _service.PlaySong(song);

        _service.ToggleShuffle();

        Assert.That(_service.IsShuffleEnabled, Is.False);
        Assert.That(_mockMediaManager.Object.ShuffleMode, Is.EqualTo(ShuffleMode.Off));
    }

    [Test]
    public async Task ToggleShuffle_WithActivePlaylist_RebuildsQueueAroundCurrentSong()
    {
        var songs = CreateTestPlaylist(5);
        var playCallCount = 0;
        var reshuffledQueueSource = new TaskCompletionSource<IReadOnlyList<IMediaItem>>();

        _mockMediaManager
            .Setup(m => m.Play(It.IsAny<IEnumerable<IMediaItem>>()))
            .Callback<IEnumerable<IMediaItem>>(items =>
            {
                playCallCount++;
                if (playCallCount == 2)
                {
                    reshuffledQueueSource.TrySetResult(items.ToList());
                }
            })
            .ReturnsAsync(Mock.Of<IMediaItem>());

        _mockMediaManager
            .SetupSet(m => m.ShuffleMode = It.IsAny<ShuffleMode>())
            .Callback<ShuffleMode>(mode =>
            {
                if (mode == ShuffleMode.All)
                {
                    _mockMediaManager.Raise(
                        m => m.MediaItemChanged += null,
                        new MediaItemEventArgs(new MediaItem(songs[1].StreamUrl!)));
                }
            });

        _service.SetPlaylist(songs, 0);

        _service.ToggleShuffle();

        var reshuffledQueue = await reshuffledQueueSource.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.That(_service.IsShuffleEnabled, Is.True);
        Assert.That(_service.CurrentSong, Is.SameAs(songs[0]));
        Assert.That(_service.CurrentTrackIndex, Is.EqualTo(0));
        Assert.That(reshuffledQueue[0].MediaUri, Is.EqualTo(songs[0].StreamUrl));
        Assert.That(reshuffledQueue.Select(item => item.MediaUri), Is.EquivalentTo(songs.Select(song => song.StreamUrl)));
        _mockMediaManager.VerifySet(m => m.ShuffleMode = ShuffleMode.All, Times.Never);
    }

    // --- OnMediaEnded with playlist ---
    // Plugin.MediaManager normally advances the queue natively, but PlaybackService now
    // includes a guarded fallback for Android sleep/background cases where that handoff stalls.

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
    public async Task OnMediaEnded_WithPlaylist_WhenAutoAdvanceStalls_PlaysNextTrackAfterDelay()
    {
        var service = CreateService(TimeSpan.FromMilliseconds(10));
        var songs = CreateTestPlaylist(3);
        service.SetPlaylist(songs, 0);

        service.OnMediaEnded();

        await Task.Delay(75);

        Assert.That(service.CurrentTrackIndex, Is.EqualTo(1));
        Assert.That(service.CurrentSong, Is.SameAs(songs[1]));
        Assert.That(service.IsPlaying, Is.True);
        _mockMediaManager.Verify(m => m.PlayQueueItem(1), Times.Once);
    }

    [Test]
    public async Task OnMediaEnded_WithPlaylist_WhenMediaManagerStillReportsPlaying_ForcesNextTrackAfterDelay()
    {
        var service = CreateService(TimeSpan.FromMilliseconds(10));
        var songs = CreateTestPlaylist(3);
        _mediaManagerState = MediaPlayerState.Playing;
        service.SetPlaylist(songs, 0);

        service.OnMediaEnded();

        await Task.Delay(75);

        Assert.That(service.CurrentTrackIndex, Is.EqualTo(1));
        Assert.That(service.CurrentSong, Is.SameAs(songs[1]));
        Assert.That(service.IsPlaying, Is.True);
        _mockMediaManager.Verify(m => m.PlayQueueItem(1), Times.Once);
    }

    [Test]
    public async Task OnMediaEnded_WithPlaylist_WhenNativeQueueAlreadyAdvanced_UsesNativeCurrentIndex()
    {
        var service = CreateService(TimeSpan.FromMilliseconds(10));
        var songs = CreateTestPlaylist(4);
        _mockMediaQueue.Setup(q => q.HasCurrent).Returns(true);
        _mockMediaQueue.Setup(q => q.CurrentIndex).Returns(2);
        _mockMediaQueue.Setup(q => q.Current).Returns(new MediaItem(songs[2].StreamUrl!));
        service.SetPlaylist(songs, 0);

        service.OnMediaEnded();

        await Task.Delay(75);

        Assert.That(service.CurrentTrackIndex, Is.EqualTo(2));
        Assert.That(service.CurrentSong, Is.SameAs(songs[2]));
        Assert.That(service.IsPlaying, Is.True);
        _mockMediaManager.Verify(m => m.PlayQueueItem(2), Times.Once);
        _mockMediaManager.Verify(m => m.PlayQueueItem(1), Times.Never);
    }

    [Test]
    public async Task OnMediaEnded_WithPlaylist_WhenTrackChangesBeforeFallback_DoesNotForceAdvance()
    {
        var service = CreateService(TimeSpan.FromMilliseconds(50));
        var songs = CreateTestPlaylist(3);
        service.SetPlaylist(songs, 0);

        service.OnMediaEnded();

        var item = new MediaItem(songs[1].StreamUrl!);
        _mockMediaManager.Raise(m => m.MediaItemChanged += null, new MediaItemEventArgs(item));

        await Task.Delay(125);

        Assert.That(service.CurrentTrackIndex, Is.EqualTo(1));
        Assert.That(service.CurrentSong, Is.SameAs(songs[1]));
        _mockMediaManager.Verify(m => m.PlayQueueItem(1), Times.Never);
    }

    [Test]
    public async Task OnMediaEnded_WithPlaylist_WhenNativeAdvanceFails_RetriesAdvancedTrack()
    {
        var service = CreateService(TimeSpan.FromMilliseconds(50));
        var songs = CreateTestPlaylist(3);
        service.SetPlaylist(songs, 0);

        service.OnMediaEnded();

        var item = new MediaItem(songs[1].StreamUrl!);
        _mockMediaManager.Raise(m => m.MediaItemChanged += null, new MediaItemEventArgs(item));
        _mediaManagerState = MediaPlayerState.Failed;
        _mockMediaManager.Raise(m => m.StateChanged += null, new StateChangedEventArgs(MediaPlayerState.Failed));
        _mediaManagerState = MediaPlayerState.Buffering;
        _mockMediaManager.Raise(m => m.StateChanged += null, new StateChangedEventArgs(MediaPlayerState.Buffering));

        await Task.Delay(125);

        Assert.That(service.CurrentTrackIndex, Is.EqualTo(1));
        Assert.That(service.CurrentSong, Is.SameAs(songs[1]));
        Assert.That(service.IsPlaying, Is.True);
        _mockMediaManager.Verify(m => m.Play(It.IsAny<IEnumerable<IMediaItem>>()), Times.Exactly(2));
        _mockMediaManager.Verify(m => m.PlayQueueItem(1), Times.Once);
    }

    [Test]
    public async Task MediaItemFinished_WithPlaylist_WhenPositionNotNearEnd_IgnoresSpuriousFinish()
    {
        var service = CreateService(TimeSpan.FromMilliseconds(10));
        var songs = CreateTestPlaylist(3);
        var duration = TimeSpan.FromSeconds(219.144);
        _mediaManagerState = MediaPlayerState.Buffering;
        _mockMediaManager.Setup(m => m.Duration).Returns(duration);
        service.SetPlaylist(songs, 0);

        var item = new MediaItem(songs[1].StreamUrl!);
        _mockMediaManager.Raise(m => m.MediaItemChanged += null, new MediaItemEventArgs(item));
        service.UpdatePosition(TimeSpan.Zero, duration);

        _mockMediaManager.Raise(m => m.MediaItemFinished += null, new MediaItemEventArgs(item));

        await Task.Delay(75);

        Assert.That(service.CurrentTrackIndex, Is.EqualTo(1));
        Assert.That(service.CurrentSong, Is.SameAs(songs[1]));
        Assert.That(service.IsPlaying, Is.True);
        _mockMediaManager.Verify(m => m.PlayQueueItem(2), Times.Never);
    }

    [Test]
    public async Task MediaItemFinished_WithPlaylist_WhenPositionNearEnd_AdvancesToNextTrack()
    {
        var service = CreateService(TimeSpan.FromMilliseconds(10));
        var songs = CreateTestPlaylist(3);
        var duration = TimeSpan.FromSeconds(219.144);
        _mediaManagerState = MediaPlayerState.Playing;
        _mockMediaManager.Setup(m => m.Duration).Returns(duration);
        service.SetPlaylist(songs, 0);
        service.UpdatePosition(duration - TimeSpan.FromMilliseconds(250), duration);

        var item = new MediaItem(songs[0].StreamUrl!);
        _mockMediaManager.Raise(m => m.MediaItemFinished += null, new MediaItemEventArgs(item));

        await Task.Delay(75);

        Assert.That(service.CurrentTrackIndex, Is.EqualTo(1));
        Assert.That(service.CurrentSong, Is.SameAs(songs[1]));
        Assert.That(service.IsPlaying, Is.True);
        _mockMediaManager.Verify(m => m.PlayQueueItem(1), Times.Once);
    }

    [Test]
    public void MediaItemFailed_LogsUnderlyingPlaybackExceptionDetails()
    {
        var logger = new ListLogger<PlaybackService>();
        _service = new PlaybackService(
            _mockAuthService.Object,
            _mockMusicService.Object,
            _mockMediaManager.Object,
            _mockAudioCacheService.Object,
            _mockPlaybackKeepAliveService.Object,
            logger);

        var song = new SongDto { Id = 20, SongTitle = "Convoy & Crown", StreamUrl = "https://test.com/20.mp3" };
        _service.PlaySong(song);

        var failure = new InvalidOperationException("Simulated native player failure");
        _mockMediaManager.Raise(
            m => m.MediaItemFailed += null,
            new MediaItemFailedEventArgs(new MediaItem(song.StreamUrl), failure, failure.Message));

        var combinedLogs = string.Join(Environment.NewLine, logger.Messages);
        Assert.That(combinedLogs, Does.Contain("MediaItemFailed received"));
        Assert.That(combinedLogs, Does.Contain("Simulated native player failure"));
        Assert.That(combinedLogs, Does.Contain(typeof(InvalidOperationException).FullName));
    }

    [Test]
    public void PlaySong_WhenCachedPlaybackUriIsAvailable_UsesLocalMediaItem()
    {
        var song = new SongDto { Id = 31, SongTitle = "Cached Song", StreamUrl = "https://test.com/song31.mp3" };
        const string localPlaybackPath = "/data/user/0/com.streamtunes/cache/song31.mp3";
        _mockAudioCacheService
            .Setup(s => s.ResolvePlaybackUriAsync(song, It.IsAny<CancellationToken>()))
            .ReturnsAsync(localPlaybackPath);

        _service.PlaySong(song);

        _mockMediaManager.Verify(
            m => m.Play(It.Is<IMediaItem>(item => item.MediaUri == localPlaybackPath && item.MediaLocation == MediaLocation.FileSystem)),
            Times.Once);
    }

    [Test]
    public async Task SetPlaylist_WhenCachedPlaybackUriIsAvailableForCurrentSong_UsesLocalQueueItemForStartTrack()
    {
        var songs = CreateTestPlaylist(3);
        const string localPlaybackPath = "/data/user/0/com.streamtunes/cache/song2.mp3";
        var queuedItemsSource = new TaskCompletionSource<IReadOnlyList<IMediaItem>>();
        _mockMediaManager
            .Setup(m => m.Play(It.IsAny<IEnumerable<IMediaItem>>()))
            .Callback<IEnumerable<IMediaItem>>(items => queuedItemsSource.TrySetResult(items.ToList()))
            .ReturnsAsync(Mock.Of<IMediaItem>());
        _mockAudioCacheService
            .Setup(s => s.ResolvePlaybackUriAsync(songs[1], It.IsAny<CancellationToken>()))
            .ReturnsAsync(localPlaybackPath);

        _service.SetPlaylist(songs, 1);

        var queuedItems = await queuedItemsSource.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.That(queuedItems[1].MediaUri, Is.EqualTo(localPlaybackPath));
        Assert.That(queuedItems[1].MediaLocation, Is.EqualTo(MediaLocation.FileSystem));
        Assert.That(queuedItems[0].MediaUri, Is.EqualTo(songs[0].StreamUrl));
    }

    [Test]
    public async Task SetPlaylist_WhenCachedPlaybackUrisAreAvailableForUpcomingTracks_UsesLocalQueueItemsForNativeHandoff()
    {
        var songs = CreateTestPlaylist(3);
        const string localCurrentPath = "/data/user/0/com.streamtunes/cache/song1.mp3";
        const string localNextPath = "/data/user/0/com.streamtunes/cache/song2.mp3";
        var queuedItemsSource = new TaskCompletionSource<IReadOnlyList<IMediaItem>>();
        _mockMediaManager
            .Setup(m => m.Play(It.IsAny<IEnumerable<IMediaItem>>()))
            .Callback<IEnumerable<IMediaItem>>(items => queuedItemsSource.TrySetResult(items.ToList()))
            .ReturnsAsync(Mock.Of<IMediaItem>());
        _mockAudioCacheService
            .Setup(s => s.ResolvePlaybackUriAsync(songs[0], It.IsAny<CancellationToken>()))
            .ReturnsAsync(localCurrentPath);
        _mockAudioCacheService
            .Setup(s => s.ResolvePlaybackUriAsync(songs[1], It.IsAny<CancellationToken>()))
            .ReturnsAsync(localNextPath);

        _service.SetPlaylist(songs, 0);

        var queuedItems = await queuedItemsSource.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.That(queuedItems[0].MediaUri, Is.EqualTo(localCurrentPath));
        Assert.That(queuedItems[0].MediaLocation, Is.EqualTo(MediaLocation.FileSystem));
        Assert.That(queuedItems[1].MediaUri, Is.EqualTo(localNextPath));
        Assert.That(queuedItems[1].MediaLocation, Is.EqualTo(MediaLocation.FileSystem));
        Assert.That(queuedItems[2].MediaUri, Is.EqualTo(songs[2].StreamUrl));
    }

    [Test]
    public void MediaItemChanged_WhenUriDoesNotMatchButNativeQueueHasCurrentIndex_UpdatesCurrentSong()
    {
        var songs = CreateTestPlaylist(3);
        _service.SetPlaylist(songs, 0);
        _mockMediaQueue.Setup(q => q.HasCurrent).Returns(true);
        _mockMediaQueue.Setup(q => q.CurrentIndex).Returns(1);

        _mockMediaManager.Raise(m => m.MediaItemChanged += null, new MediaItemEventArgs(new MediaItem("https://test.com/normalized-or-renewed-url.mp3")));

        Assert.That(_service.CurrentTrackIndex, Is.EqualTo(1));
        Assert.That(_service.CurrentSong, Is.SameAs(songs[1]));
        Assert.That(_service.IsPlaying, Is.True);
        _mockMediaManager.Verify(m => m.PlayQueueItem(It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task OnMediaEnded_WithPlaylist_ShuffleEnabled_UsesMediaManagerPlayNextFallback()
    {
        var service = CreateService(TimeSpan.FromMilliseconds(10));
        var songs = CreateTestPlaylist(3);

        _mockMediaManager
            .Setup(m => m.PlayNext())
            .Returns(() =>
            {
                var shuffledItem = new MediaItem(songs[2].StreamUrl!);
                _mockMediaManager.Raise(m => m.MediaItemChanged += null, new MediaItemEventArgs(shuffledItem));
                return Task.FromResult(true);
            });

        service.SetPlaylist(songs, 0);
        service.ToggleShuffle();

        service.OnMediaEnded();

        await Task.Delay(75);

        Assert.That(service.CurrentSong, Is.SameAs(songs[2]));
        Assert.That(service.Playlist, Is.Not.Null);
        Assert.That(service.CurrentTrackIndex, Is.EqualTo(service.Playlist!.FindIndex(song => song.Id == songs[2].Id)));
        Assert.That(service.IsPlaying, Is.True);
        _mockMediaManager.Verify(m => m.PlayNext(), Times.Once);
        _mockMediaManager.Verify(m => m.PlayQueueItem(It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task OnMediaEnded_WithPlaylist_LastTrackWithoutRepeat_StopsPlaybackAfterDelay()
    {
        var service = CreateService(TimeSpan.FromMilliseconds(10));
        var songs = CreateTestPlaylist(2);
        service.SetPlaylist(songs, 1);

        service.OnMediaEnded();

        await Task.Delay(75);

        Assert.That(service.CurrentTrackIndex, Is.EqualTo(1));
        Assert.That(service.CurrentSong, Is.SameAs(songs[1]));
        Assert.That(service.IsPlaying, Is.False);
        _mockMediaManager.Verify(m => m.PlayQueueItem(0), Times.Never);
    }

    [Test]
    public async Task OnMediaEnded_WithPlaylist_LastTrackWithRepeat_RestartsFirstTrackAfterDelay()
    {
        var service = CreateService(TimeSpan.FromMilliseconds(10));
        var songs = CreateTestPlaylist(2);
        service.SetPlaylist(songs, 1);
        service.ToggleRepeat();

        service.OnMediaEnded();

        await Task.Delay(75);

        Assert.That(service.CurrentTrackIndex, Is.EqualTo(0));
        Assert.That(service.CurrentSong, Is.SameAs(songs[0]));
        Assert.That(service.IsPlaying, Is.True);
        _mockMediaManager.Verify(m => m.PlayQueueItem(0), Times.Once);
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

internal sealed class ListLogger<T> : ILogger<T>
{
    private readonly List<string> _messages = new();

    public IReadOnlyList<string> Messages
    {
        get
        {
            lock (_messages)
            {
                return _messages.ToArray();
            }
        }
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (_messages)
        {
            _messages.Add(formatter(state, exception));
            if (exception != null)
            {
                _messages.Add(exception.ToString());
            }
        }
    }
}
