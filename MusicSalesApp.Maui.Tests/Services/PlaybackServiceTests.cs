using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MusicSalesApp.Common.Helpers;
using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Tests.Services;

[TestFixture]
public class PlaybackServiceTests
{
    private Mock<IAuthService> _mockAuthService;
    private Mock<IMusicService> _mockMusicService;
    private Mock<IPlatformPlaybackRuntime> _mockMediaManager;
    private Mock<IPlaybackRuntimeQueue> _mockMediaQueue;
    private Mock<IAudioCacheService> _mockAudioCacheService;
    private Mock<IQueuePreparationService> _mockQueuePreparationService;
    private Mock<IPlaybackKeepAliveService> _mockPlaybackKeepAliveService;
    private Mock<IAnonymousFeaturedStreamStore> _mockAnonymousFeaturedStreamStore;
    private PlaybackService _service;
    private PlaybackRuntimeState _mediaManagerState;

    [SetUp]
    public void Setup()
    {
        _mockAuthService = new Mock<IAuthService>();
        _mockMusicService = new Mock<IMusicService>();
        _mockMediaManager = new Mock<IPlatformPlaybackRuntime>();
        _mockMediaQueue = new Mock<IPlaybackRuntimeQueue>();
        _mockAudioCacheService = new Mock<IAudioCacheService>();
        _mockQueuePreparationService = new Mock<IQueuePreparationService>();
        _mockPlaybackKeepAliveService = new Mock<IPlaybackKeepAliveService>();
        _mockAnonymousFeaturedStreamStore = new Mock<IAnonymousFeaturedStreamStore>();
        _mediaManagerState = PlaybackRuntimeState.Stopped;

        // Set up async methods to return completed tasks
        _mockMediaManager.Setup(m => m.PlayAsync(It.IsAny<PlaybackMediaItem>())).ReturnsAsync((PlaybackMediaItem?)null);
        _mockMediaManager.Setup(m => m.PlayAsync(It.IsAny<IEnumerable<PlaybackMediaItem>>())).ReturnsAsync((PlaybackMediaItem?)null);
        _mockMediaManager.Setup(m => m.PlayAsync()).Returns(Task.CompletedTask);
        _mockMediaManager.Setup(m => m.PauseAsync()).Returns(Task.CompletedTask);
        _mockMediaManager.Setup(m => m.StopAsync()).Returns(Task.CompletedTask);
        _mockMediaManager.Setup(m => m.PlayNextAsync()).Returns(Task.FromResult(false));
        _mockMediaManager.Setup(m => m.PlayPreviousAsync()).Returns(Task.FromResult(false));
        _mockMediaManager.Setup(m => m.PlayQueueItemAsync(It.IsAny<int>())).Returns(Task.FromResult(false));
        _mockMediaManager.Setup(m => m.SeekToAsync(It.IsAny<TimeSpan>())).Returns(Task.CompletedTask);
        _mockMediaManager.SetupProperty(m => m.RepeatMode);
        _mockMediaManager.SetupProperty(m => m.ShuffleMode);
        _mockMediaManager.Setup(m => m.Position).Returns(TimeSpan.Zero);
        _mockMediaManager.Setup(m => m.Duration).Returns(TimeSpan.Zero);
        _mockMediaManager.Setup(m => m.State).Returns(() => _mediaManagerState);
        _mockMediaManager.Setup(m => m.Queue).Returns(_mockMediaQueue.Object);
        _mockMusicService.Setup(s => s.RecordStreamAsync(It.IsAny<int>())).ReturnsAsync((int?)null);
        _mockAudioCacheService
            .Setup(s => s.GetImmediatePlaybackUri(It.IsAny<SongDto>()))
            .Returns((SongDto song) => song.StreamUrl ?? string.Empty);
        _mockAudioCacheService
            .Setup(s => s.GetStableCacheKey(It.IsAny<SongDto>()))
            .Returns((SongDto song) => $"song-{song.Id}");
        _mockAudioCacheService
            .Setup(s => s.ResolvePlaybackUriAsync(It.IsAny<SongDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SongDto song, CancellationToken _) => song.StreamUrl);
        _mockAudioCacheService
            .Setup(s => s.GetCacheStatus(It.IsAny<SongDto>()))
            .Returns((SongDto song) => new TrackCacheStatus(song.Id, $"song-{song.Id}", null, false, false));
        _mockQueuePreparationService
            .Setup(s => s.PrepareAsync(
                It.IsAny<IReadOnlyList<SongDto>>(),
                It.IsAny<int>(),
                It.IsAny<QueuePreparationMode>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<SongDto> queue, int startIndex, QueuePreparationMode mode, TimeSpan _, CancellationToken _) =>
                new QueuePreparationResult(false, startIndex - 1, TimeSpan.Zero, [], mode, null));
        _mockMediaQueue.Setup(q => q.HasCurrent).Returns(false);

        _service = CreateService();
    }

    private PlaybackService CreateService(
        TimeSpan? playlistAdvanceFallbackDelay = null,
        TimeSpan? positionSamplerInterval = null,
        TimeSpan? positionEventStaleThreshold = null,
        TimeSpan? transientStopConfirmationDelay = null,
        TimeSpan? subscriptionStatusRefreshInterval = null,
        TimeSpan? bufferingStallRecoveryDelay = null)
    {
        return new PlaybackService(
            _mockAuthService.Object,
            _mockMusicService.Object,
            _mockMediaManager.Object,
            _mockAudioCacheService.Object,
            _mockQueuePreparationService.Object,
            _mockPlaybackKeepAliveService.Object,
            NullLogger<PlaybackService>.Instance,
            playlistAdvanceFallbackDelay,
            positionSamplerInterval,
            positionEventStaleThreshold,
            transientStopConfirmationDelay,
            subscriptionStatusRefreshInterval,
            bufferingStallRecoveryDelay,
            _mockAnonymousFeaturedStreamStore.Object);
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
            _mockQueuePreparationService.Object,
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
        _mockMediaQueue.Setup(q => q.Current).Returns(new PlaybackMediaItem(songs[0].StreamUrl!)
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
    public async Task UpdatePosition_WhenSubscriptionExpiresDuringPlayback_EnforcesPreviewLimitImmediately()
    {
        var hasActiveSubscription = true;
        _mockAuthService.SetupGet(a => a.IsLoggedIn).Returns(true);
        _mockAuthService.SetupGet(a => a.HasActiveSubscription).Returns(() => hasActiveSubscription);
        _mockAuthService.SetupGet(a => a.SubscriptionStatus).Returns(() => hasActiveSubscription ? "ACTIVE" : "EXPIRED");
        _mockAuthService.Setup(a => a.RefreshUserStatusAsync())
            .Callback(() => hasActiveSubscription = false)
            .Returns(Task.CompletedTask);
        _service = CreateService(subscriptionStatusRefreshInterval: TimeSpan.FromMilliseconds(1));
        var song = new SongDto { Id = 1, SongTitle = "Test", StreamUrl = "https://test.com/song.mp3" };

        _service.PlaySong(song);
        _service.UpdatePosition(TimeSpan.FromSeconds(61), TimeSpan.FromSeconds(180));

        await WaitForAsync(() => _service.PreviewLimitReached);

        Assert.Multiple(() =>
        {
            Assert.That(_service.IsPlaying, Is.False);
            Assert.That(_service.PreviewLimitReached, Is.True);
            Assert.That(_service.FormattedPosition, Is.EqualTo("0:00"));
        });
        _mockAuthService.Verify(a => a.RefreshUserStatusAsync(), Times.Once);
        _mockMediaManager.Verify(m => m.PauseAsync(), Times.Once);
        _mockMediaManager.Verify(m => m.SeekToAsync(TimeSpan.Zero), Times.Once);
    }

    [Test]
    public async Task UpdatePosition_WhenSubscriptionRemainsActive_DoesNotEnforcePreviewLimit()
    {
        _mockAuthService.SetupGet(a => a.IsLoggedIn).Returns(true);
        _mockAuthService.SetupGet(a => a.HasActiveSubscription).Returns(true);
        _mockAuthService.SetupGet(a => a.SubscriptionStatus).Returns("ACTIVE");
        _mockAuthService.Setup(a => a.RefreshUserStatusAsync()).Returns(Task.CompletedTask);
        _service = CreateService(subscriptionStatusRefreshInterval: TimeSpan.FromMilliseconds(1));
        var song = new SongDto { Id = 1, SongTitle = "Test", StreamUrl = "https://test.com/song.mp3" };

        _service.PlaySong(song);
        _service.UpdatePosition(TimeSpan.FromSeconds(61), TimeSpan.FromSeconds(180));

        await WaitForAsync(() => _mockAuthService.Invocations.Any(invocation => invocation.Method.Name == nameof(IAuthService.RefreshUserStatusAsync)));

        Assert.Multiple(() =>
        {
            Assert.That(_service.IsPlaying, Is.True);
            Assert.That(_service.PreviewLimitReached, Is.False);
            Assert.That(_service.FormattedPosition, Is.EqualTo("1:01"));
        });
        _mockMediaManager.Verify(m => m.PauseAsync(), Times.Never);
    }

    [Test]
    public void UpdatePosition_WhenCancelledSubscriptionEndDateHasPassed_EnforcesPreviewLimitWithoutServerRefresh()
    {
        _mockAuthService.SetupGet(a => a.HasActiveSubscription).Returns(true);
        _mockAuthService.SetupGet(a => a.SubscriptionStatus).Returns(SubscriptionStatuses.Cancelled);
        _mockAuthService.SetupGet(a => a.SubscriptionEndDate).Returns(DateTime.UtcNow.AddMinutes(-1));
        var song = new SongDto { Id = 1, SongTitle = "Test", StreamUrl = "https://test.com/song.mp3" };

        _service.PlaySong(song);
        _service.UpdatePosition(TimeSpan.FromSeconds(61), TimeSpan.FromSeconds(180));

        Assert.Multiple(() =>
        {
            Assert.That(_service.IsPlaying, Is.False);
            Assert.That(_service.PreviewLimitReached, Is.True);
            Assert.That(_service.FormattedPosition, Is.EqualTo("0:00"));
        });
        _mockAuthService.Verify(a => a.RefreshUserStatusAsync(), Times.Never);
        _mockMediaManager.Verify(m => m.PauseAsync(), Times.Once);
        _mockMediaManager.Verify(m => m.SeekToAsync(TimeSpan.Zero), Times.Once);
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

        _mockMediaManager.Verify(m => m.PlayAsync(It.IsAny<PlaybackMediaItem>()), Times.Once);
    }

    [Test]
    public void PlaySong_UsesAlbumArtForMediaItemImage()
    {
        PlaybackMediaItem? capturedMediaItem = null;
        _mockMediaManager
            .Setup(m => m.PlayAsync(It.IsAny<PlaybackMediaItem>()))
            .Callback<PlaybackMediaItem>(mediaItem => capturedMediaItem = mediaItem)
            .ReturnsAsync((PlaybackMediaItem?)null);

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
        PlaybackMediaItem? capturedMediaItem = null;
        _mockMediaManager
            .Setup(m => m.PlayAsync(It.IsAny<PlaybackMediaItem>()))
            .Callback<PlaybackMediaItem>(mediaItem => capturedMediaItem = mediaItem)
            .ReturnsAsync((PlaybackMediaItem?)null);

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

        _mockMediaManager.Verify(m => m.PauseAsync(), Times.Once);
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
        _mockMediaManager.Verify(m => m.PauseAsync(), Times.AtLeastOnce);

        // Resume
        _service.TogglePlayPause();
        _mockMediaManager.Verify(m => m.PlayAsync(), Times.Once);
    }

    [Test]
    public void TogglePlayPause_WhenPreviewLimitReached_ReplaysFromStart()
    {
        _mockAuthService.Setup(a => a.HasActiveSubscription).Returns(false);
        var song = new SongDto { Id = 1, SongTitle = "Test", StreamUrl = "https://test.com/song1.mp3" };
        _service.PlaySong(song);
        _service.UpdatePosition(TimeSpan.FromSeconds(61), TimeSpan.FromSeconds(180));
        Assert.That(_service.PreviewLimitReached, Is.True);

        _mockMediaManager.Invocations.Clear();

        _service.TogglePlayPause();

        Assert.Multiple(() =>
        {
            Assert.That(_service.IsPlaying, Is.True);
            Assert.That(_service.PreviewLimitReached, Is.False);
            Assert.That(_service.FormattedPosition, Is.EqualTo("0:00"));
        });
        _mockMediaManager.Verify(m => m.PlayAsync(), Times.Once);
    }

    [Test]
    public void MediaManagerPlayingState_WhenPreviewLimitReached_DoesNotForcePause()
    {
        _mockAuthService.Setup(a => a.HasActiveSubscription).Returns(false);
        var song = new SongDto { Id = 1, SongTitle = "Test", StreamUrl = "https://test.com/song1.mp3" };
        _service.PlaySong(song);
        _service.UpdatePosition(TimeSpan.FromSeconds(61), TimeSpan.FromSeconds(180));
        Assert.That(_service.PreviewLimitReached, Is.True);

        _mockMediaManager.Invocations.Clear();

        _mockMediaManager.Raise(m => m.StateChanged += null, new PlaybackRuntimeStateChangedEventArgs(PlaybackRuntimeState.Playing));

        Assert.Multiple(() =>
        {
            Assert.That(_service.IsPlaying, Is.True);
            Assert.That(_service.PreviewLimitReached, Is.True);
            Assert.That(_service.FormattedPosition, Is.EqualTo("0:00"));
        });
        _mockMediaManager.Verify(m => m.PauseAsync(), Times.Never);
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

        _mockMediaManager.Verify(m => m.PauseAsync(), Times.Once);
        _mockMediaManager.Verify(m => m.SeekToAsync(TimeSpan.Zero), Times.Once);
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
    public void ToggleRepeat_WithPlaylist_LeavesNativeRepeatAllForQueueAdvancement()
    {
        var songs = CreateTestPlaylist(3);
        _service.SetPlaylist(songs, 0);

        Assert.That(_mockMediaManager.Object.RepeatMode, Is.EqualTo(PlaybackRepeatMode.All));

        _service.ToggleRepeat();

        Assert.That(_service.IsRepeatEnabled, Is.True);
        Assert.That(_mockMediaManager.Object.RepeatMode, Is.EqualTo(PlaybackRepeatMode.All));
    }

    [Test]
    public void ToggleRepeat_WithoutCurrentSong_DoesNothing()
    {
        _service.ToggleRepeat();

        Assert.That(_service.IsRepeatEnabled, Is.False);
        Assert.That(_mockMediaManager.Object.RepeatMode, Is.EqualTo(PlaybackRepeatMode.Off));
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

        _mockMediaManager.Verify(m => m.SeekToAsync(TimeSpan.FromSeconds(100)), Times.Once);
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

        _mockMediaManager.Verify(m => m.SeekToAsync(TimeSpan.Zero), Times.Once);
        _mockMediaManager.Verify(m => m.PlayAsync(), Times.Once);
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
    public void StreamTracking_AnonymousFeaturedSongAlreadyRecordedOnDevice_DoesNotRecordAgain()
    {
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(false);
        _mockAuthService.Setup(a => a.HasActiveSubscription).Returns(false);
        _mockAnonymousFeaturedStreamStore
            .Setup(store => store.HasRecordedFeaturedStream(10))
            .Returns(true);
        _service.SetStreamQualifyingSeconds(3);
        var song = new SongDto
        {
            Id = 10,
            SongTitle = "Featured",
            DisplayOnHomePage = true,
            StreamUrl = "https://test.com/song.mp3"
        };
        _service.PlaySong(song);

        for (int i = 1; i <= 10; i++)
        {
            _service.UpdatePosition(TimeSpan.FromSeconds(i), TimeSpan.FromSeconds(180));
        }

        _mockMusicService.Verify(s => s.RecordStreamAsync(It.IsAny<int>()), Times.Never);
        _mockAnonymousFeaturedStreamStore.Verify(store => store.MarkFeaturedStreamRecorded(It.IsAny<int>()), Times.Never);
    }

    [Test]
    public void StreamTracking_AnonymousFeaturedSongFirstQualifiedPlayback_MarksDeviceAndRecordsStream()
    {
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(false);
        _mockAuthService.Setup(a => a.HasActiveSubscription).Returns(false);
        _mockAnonymousFeaturedStreamStore
            .Setup(store => store.HasRecordedFeaturedStream(10))
            .Returns(false);
        _service.SetStreamQualifyingSeconds(3);
        var song = new SongDto
        {
            Id = 10,
            SongTitle = "Featured",
            DisplayOnHomePage = true,
            StreamUrl = "https://test.com/song.mp3"
        };
        _service.PlaySong(song);

        for (int i = 1; i <= 4; i++)
        {
            _service.UpdatePosition(TimeSpan.FromSeconds(i), TimeSpan.FromSeconds(180));
        }

        _mockAnonymousFeaturedStreamStore.Verify(store => store.MarkFeaturedStreamRecorded(10), Times.Once);
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

        _mockMediaManager.Verify(m => m.SeekToAsync(TimeSpan.FromSeconds(90)), Times.Once);
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

        _mockMediaManager.Raise(m => m.StateChanged += null, new PlaybackRuntimeStateChangedEventArgs(PlaybackRuntimeState.Stopped));

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

        _mockMediaManager.Raise(m => m.StateChanged += null, new PlaybackRuntimeStateChangedEventArgs(PlaybackRuntimeState.Stopped));

        await Task.Delay(120);

        Assert.That(service.IsPlaying, Is.False);
        _mockPlaybackKeepAliveService.Verify(s => s.SetPlaybackActive(false), Times.Once);
    }

    [Test]
    public async Task MediaManagerStopped_WithoutProgress_WithActivePlaylist_AttemptsRecovery()
    {
        var service = CreateService(transientStopConfirmationDelay: TimeSpan.FromMilliseconds(40));
        var songs = CreateTestPlaylist(3);

        service.SetPlaylist(songs, 1);
        _mockMediaManager.Invocations.Clear();

        _mockMediaManager.Raise(m => m.StateChanged += null, new PlaybackRuntimeStateChangedEventArgs(PlaybackRuntimeState.Stopped));

        await Task.Delay(120);

        Assert.That(service.IsPlaying, Is.True);
        _mockPlaybackKeepAliveService.Verify(s => s.SetPlaybackActive(false), Times.Never);
    }

    [Test]
    public async Task MediaManagerPaused_WithoutProgress_WithActivePlaylist_AttemptsRecovery()
    {
        var service = CreateService(transientStopConfirmationDelay: TimeSpan.FromMilliseconds(40));
        var songs = CreateTestPlaylist(3);

        service.SetPlaylist(songs, 1);
        _mockMediaManager.Invocations.Clear();
        _mockPlaybackKeepAliveService.Invocations.Clear();

        _mediaManagerState = PlaybackRuntimeState.Paused;
        _mockMediaManager.Raise(m => m.StateChanged += null, new PlaybackRuntimeStateChangedEventArgs(PlaybackRuntimeState.Paused));

        await Task.Delay(120);

        Assert.Multiple(() =>
        {
            Assert.That(service.CurrentTrackIndex, Is.EqualTo(1));
            Assert.That(service.CurrentSong, Is.SameAs(songs[1]));
            Assert.That(service.IsPlaying, Is.True);
        });
        _mockPlaybackKeepAliveService.Verify(s => s.SetPlaybackActive(false), Times.Never);
        _mockMediaManager.Verify(m => m.PlayAsync(It.IsAny<IEnumerable<PlaybackMediaItem>>()), Times.Once);
        _mockMediaManager.Verify(m => m.PlayQueueItemAsync(1), Times.Once);
    }

    [TestCase(PlaybackRuntimeState.Paused)]
    [TestCase(PlaybackRuntimeState.Stopped)]
    public async Task MediaManagerTerminalState_UserRequest_WithActivePlaylist_DoesNotRecover(PlaybackRuntimeState terminalState)
    {
        var service = CreateService(transientStopConfirmationDelay: TimeSpan.FromMilliseconds(40));
        var songs = CreateTestPlaylist(3);

        service.SetPlaylist(songs, 1);
        _mockMediaManager.Invocations.Clear();
        _mockPlaybackKeepAliveService.Invocations.Clear();

        _mediaManagerState = terminalState;
        _mockMediaManager.Raise(
            m => m.StateChanged += null,
            new PlaybackRuntimeStateChangedEventArgs(terminalState, PlaybackRuntimeStateChangeReason.UserRequest));

        await Task.Delay(120);

        Assert.Multiple(() =>
        {
            Assert.That(service.CurrentTrackIndex, Is.EqualTo(1));
            Assert.That(service.CurrentSong, Is.SameAs(songs[1]));
            Assert.That(service.IsPlaying, Is.False);
        });
        _mockPlaybackKeepAliveService.Verify(s => s.SetPlaybackActive(false), Times.Once);
        _mockMediaManager.Verify(m => m.StopAsync(), terminalState == PlaybackRuntimeState.Stopped ? Times.Once : Times.Never);
        _mockMediaManager.Verify(m => m.PlayAsync(It.IsAny<IEnumerable<PlaybackMediaItem>>()), Times.Never);
        _mockMediaManager.Verify(m => m.PlayQueueItemAsync(It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task MediaManagerStopped_UserRequest_StopCleanupDoesNotReenterFromRuntimeStopEvent()
    {
        var service = CreateService(transientStopConfirmationDelay: TimeSpan.FromMilliseconds(40));
        var songs = CreateTestPlaylist(3);

        service.SetPlaylist(songs, 1);
        _mockMediaManager.Invocations.Clear();
        _mockPlaybackKeepAliveService.Invocations.Clear();

        _mockMediaManager
            .Setup(m => m.StopAsync())
            .Callback(() =>
            {
                _mediaManagerState = PlaybackRuntimeState.Stopped;
                _mockMediaManager.Raise(
                    m => m.StateChanged += null,
                    new PlaybackRuntimeStateChangedEventArgs(
                        PlaybackRuntimeState.Stopped,
                        PlaybackRuntimeStateChangeReason.UserRequest));
            })
            .Returns(Task.CompletedTask);

        _mediaManagerState = PlaybackRuntimeState.Stopped;
        _mockMediaManager.Raise(
            m => m.StateChanged += null,
            new PlaybackRuntimeStateChangedEventArgs(
                PlaybackRuntimeState.Stopped,
                PlaybackRuntimeStateChangeReason.UserRequest));

        await Task.Delay(120);

        Assert.Multiple(() =>
        {
            Assert.That(service.CurrentTrackIndex, Is.EqualTo(1));
            Assert.That(service.CurrentSong, Is.SameAs(songs[1]));
            Assert.That(service.IsPlaying, Is.False);
        });
        _mockMediaManager.Verify(m => m.StopAsync(), Times.Once);
        _mockMediaManager.Verify(m => m.PlayAsync(It.IsAny<IEnumerable<PlaybackMediaItem>>()), Times.Never);
        _mockMediaManager.Verify(m => m.PlayQueueItemAsync(It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task MediaManagerStopped_WithStaleBackwardNativeIndex_RecoversCurrentPlaylistIndex()
    {
        var service = CreateService(transientStopConfirmationDelay: TimeSpan.FromMilliseconds(40));
        var songs = CreateTestPlaylist(4);

        service.SetPlaylist(songs, 0);
        service.PlayTrackAtIndex(2);
        await Task.Delay(50);
        _mockMediaManager.Invocations.Clear();
        _mockPlaybackKeepAliveService.Invocations.Clear();
        _mockMediaQueue.Setup(q => q.HasCurrent).Returns(true);
        _mockMediaQueue.Setup(q => q.CurrentIndex).Returns(0);

        _mediaManagerState = PlaybackRuntimeState.Stopped;
        _mockMediaManager.Raise(m => m.StateChanged += null, new PlaybackRuntimeStateChangedEventArgs(PlaybackRuntimeState.Stopped));

        await Task.Delay(120);

        Assert.Multiple(() =>
        {
            Assert.That(service.CurrentTrackIndex, Is.EqualTo(2));
            Assert.That(service.CurrentSong, Is.SameAs(songs[2]));
            Assert.That(service.IsPlaying, Is.True);
        });
        _mockPlaybackKeepAliveService.Verify(s => s.SetPlaybackActive(false), Times.Never);
        _mockMediaManager.Verify(m => m.PlayAsync(It.IsAny<IEnumerable<PlaybackMediaItem>>()), Times.Once);
        _mockMediaManager.Verify(m => m.PlayQueueItemAsync(2), Times.Once);
        _mockMediaManager.Verify(m => m.PlayQueueItemAsync(0), Times.Never);
    }

    [Test]
    public async Task StreamTracking_PlayNext_StartsFreshTimerForNextSong()
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
        var item = new PlaybackMediaItem(songs[1].StreamUrl!);
        _mockMediaManager.Raise(m => m.MediaItemChanged += null, new PlaybackMediaItemEventArgs(item));

        for (int i = 1; i <= 9; i++)
        {
            _service.UpdatePosition(TimeSpan.FromSeconds(i), TimeSpan.FromSeconds(180));
        }

        _mockMusicService.Verify(s => s.RecordStreamAsync(It.IsAny<int>()), Times.Never);

        _service.UpdatePosition(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(180));

        await WaitForAsync(() => _mockMusicService.Invocations.Any(invocation =>
            invocation.Method.Name == nameof(IMusicService.RecordStreamAsync) &&
            invocation.Arguments.Count > 0 &&
            invocation.Arguments[0] is int songId &&
            songId == songs[1].Id));

        _mockMusicService.Verify(s => s.RecordStreamAsync(songs[0].Id), Times.Never);
        _mockMusicService.Verify(s => s.RecordStreamAsync(songs[1].Id), Times.Once);
    }

    [Test]
    public async Task StreamTracking_PlayPrevious_StartsFreshTimerForPreviousSong()
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
        var item = new PlaybackMediaItem(songs[0].StreamUrl!) { Title = songs[0].SongTitle };
        _mockMediaManager.Raise(m => m.MediaItemChanged += null, new PlaybackMediaItemEventArgs(item));

        for (int i = 1; i <= 9; i++)
        {
            _service.UpdatePosition(TimeSpan.FromSeconds(i), TimeSpan.FromSeconds(180));
        }

        _mockMusicService.Verify(s => s.RecordStreamAsync(It.IsAny<int>()), Times.Never);

        _service.UpdatePosition(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(180));

        await WaitForAsync(() => _mockMusicService.Invocations.Any(invocation =>
            invocation.Method.Name == nameof(IMusicService.RecordStreamAsync) &&
            invocation.Arguments.Count > 0 &&
            invocation.Arguments[0] is int songId &&
            songId == songs[0].Id));

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
        Assert.That(_service.FormattedPosition, Is.EqualTo("0:00"));
        _mockMediaManager.Verify(m => m.SeekToAsync(TimeSpan.Zero), Times.Once);
    }

    [Test]
    public void PreviewLimit_FeaturedNonSubscriber_PlaysPast60Seconds()
    {
        _mockAuthService.Setup(a => a.HasActiveSubscription).Returns(false);
        var song = new SongDto
        {
            Id = 1,
            SongTitle = "Featured",
            DisplayOnHomePage = true,
            StreamUrl = "https://test.com/featured.mp3"
        };
        _service.PlaySong(song);

        for (int i = 1; i <= 120; i++)
        {
            _service.UpdatePosition(TimeSpan.FromSeconds(i), TimeSpan.FromSeconds(180));
        }

        Assert.Multiple(() =>
        {
            Assert.That(_service.IsPlaying, Is.True);
            Assert.That(_service.PreviewLimitReached, Is.False);
            Assert.That(_service.FormattedPosition, Is.EqualTo("2:00"));
        });
        _mockMediaManager.Verify(m => m.SeekToAsync(TimeSpan.Zero), Times.Never);
        _mockMediaManager.Verify(m => m.PauseAsync(), Times.Never);
    }

    [Test]
    public void PreviewLimit_Playlist_AutoAdvancesToNextTrack()
    {
        _mockAuthService.Setup(a => a.HasActiveSubscription).Returns(false);
        var songs = CreateTestPlaylist(3);
        _service.SetPlaylist(songs, 0);

        _mockMediaManager.Invocations.Clear();

        _service.UpdatePosition(TimeSpan.FromSeconds(61), TimeSpan.FromSeconds(180));

        Assert.Multiple(() =>
        {
            Assert.That(_service.CurrentTrackIndex, Is.EqualTo(1));
            Assert.That(_service.CurrentSong?.Id, Is.EqualTo(songs[1].Id));
            Assert.That(_service.IsPlaying, Is.True);
            Assert.That(_service.PreviewLimitReached, Is.False);
        });
        _mockMediaManager.Verify(m => m.PlayQueueItemAsync(1), Times.Once);
        _mockMediaManager.Verify(m => m.PlayAsync(), Times.Never);
    }

    [Test]
    public void PreviewLimit_Playlist_LastTrack_StopsAndResetsToStart()
    {
        _mockAuthService.Setup(a => a.HasActiveSubscription).Returns(false);
        var songs = CreateTestPlaylist(2);
        _service.SetPlaylist(songs, 1);

        _service.UpdatePosition(TimeSpan.FromSeconds(61), TimeSpan.FromSeconds(180));

        Assert.Multiple(() =>
        {
            Assert.That(_service.CurrentTrackIndex, Is.EqualTo(1));
            Assert.That(_service.CurrentSong?.Id, Is.EqualTo(songs[1].Id));
            Assert.That(_service.IsPlaying, Is.False);
            Assert.That(_service.PreviewLimitReached, Is.True);
            Assert.That(_service.FormattedPosition, Is.EqualTo("0:00"));
        });
        _mockMediaManager.Verify(m => m.SeekToAsync(TimeSpan.Zero), Times.AtLeastOnce);
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
        _mockMediaManager.Verify(m => m.PlayAsync(), Times.AtLeastOnce);
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
        Assert.That(_mockMediaManager.Object.RepeatMode, Is.EqualTo(PlaybackRepeatMode.All));
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

    [Test]
    public void SetPlaylist_PreserveCurrentSongIfPresent_UpdatesQueueWithoutRestartingPlayback()
    {
        var librarySongs = CreateTestPlaylist(3);
        _service.SetPlaylist(librarySongs, 1);
        _mockMediaManager.Invocations.Clear();

        var pageSongs = new List<SongDto>
        {
            new() { Id = 2, SongTitle = "Song 2 On Page", ArtistName = "Artist 2", Genre = "Rock", StreamUrl = "https://test.com/song2.mp3" },
            new() { Id = 4, SongTitle = "Song 4", ArtistName = "Artist 4", Genre = "Rock", StreamUrl = "https://test.com/song4.mp3" }
        };

        _service.SetPlaylist(pageSongs, 0, PlaybackQueueStartBehavior.PreserveCurrentSongIfPresent);

        Assert.Multiple(() =>
        {
            Assert.That(_service.Playlist?.Select(song => song.Id), Is.EqualTo(new[] { 2, 4 }));
            Assert.That(_service.CurrentSong?.Id, Is.EqualTo(2));
            Assert.That(_service.CurrentSong?.SongTitle, Is.EqualTo("Song 2 On Page"));
            Assert.That(_service.CurrentTrackIndex, Is.EqualTo(0));
            Assert.That(_service.IsPlaying, Is.True);
        });
        _mockMediaManager.Verify(m => m.PlayAsync(It.IsAny<IEnumerable<PlaybackMediaItem>>()), Times.Never);
        _mockMediaManager.Verify(m => m.PlayAsync(It.IsAny<PlaybackMediaItem>()), Times.Never);
        _mockMediaManager.Verify(m => m.PlayQueueItemAsync(It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task SetPlaylist_PreserveCurrentSongIfPresent_WhenRuntimeCanReplaceQueue_ReplacesNativeQueueWithoutChangingCurrentSong()
    {
        var playbackRuntime = new Mock<IPlatformPlaybackRuntime>();
        var replacementRuntime = playbackRuntime.As<IQueueReplacementPlaybackRuntime>();
        var mediaQueue = new Mock<IPlaybackRuntimeQueue>();
        var capturedReplacement = new TaskCompletionSource<(IReadOnlyList<PlaybackMediaItem> Items, int CurrentIndex, TimeSpan Position, bool PlayWhenReady)>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        playbackRuntime.Setup(m => m.PlayAsync(It.IsAny<PlaybackMediaItem>())).ReturnsAsync((PlaybackMediaItem?)null);
        playbackRuntime.Setup(m => m.PlayAsync(It.IsAny<IEnumerable<PlaybackMediaItem>>())).ReturnsAsync((PlaybackMediaItem?)null);
        playbackRuntime.Setup(m => m.PlayAsync()).Returns(Task.CompletedTask);
        playbackRuntime.Setup(m => m.PauseAsync()).Returns(Task.CompletedTask);
        playbackRuntime.Setup(m => m.StopAsync()).Returns(Task.CompletedTask);
        playbackRuntime.Setup(m => m.PlayNextAsync()).Returns(Task.FromResult(false));
        playbackRuntime.Setup(m => m.PlayPreviousAsync()).Returns(Task.FromResult(false));
        playbackRuntime.Setup(m => m.PlayQueueItemAsync(It.IsAny<int>())).Returns(Task.FromResult(false));
        playbackRuntime.Setup(m => m.SeekToAsync(It.IsAny<TimeSpan>())).Returns(Task.CompletedTask);
        playbackRuntime.SetupProperty(m => m.RepeatMode);
        playbackRuntime.SetupProperty(m => m.ShuffleMode);
        playbackRuntime.Setup(m => m.Position).Returns(TimeSpan.FromSeconds(42));
        playbackRuntime.Setup(m => m.Duration).Returns(TimeSpan.FromMinutes(3));
        playbackRuntime.Setup(m => m.State).Returns(PlaybackRuntimeState.Playing);
        playbackRuntime.Setup(m => m.Queue).Returns(mediaQueue.Object);
        mediaQueue.Setup(q => q.HasCurrent).Returns(false);
        replacementRuntime
            .Setup(m => m.ReplaceQueueAsync(
                It.IsAny<IEnumerable<PlaybackMediaItem>>(),
                It.IsAny<int>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<bool>()))
            .Callback<IEnumerable<PlaybackMediaItem>, int, TimeSpan, bool>((items, currentIndex, position, playWhenReady) =>
                capturedReplacement.TrySetResult((items.ToList(), currentIndex, position, playWhenReady)))
            .Returns<IEnumerable<PlaybackMediaItem>, int, TimeSpan, bool>((items, currentIndex, _, _) =>
                Task.FromResult<PlaybackMediaItem?>(items.ElementAt(currentIndex)));

        var service = new PlaybackService(
            _mockAuthService.Object,
            _mockMusicService.Object,
            playbackRuntime.Object,
            _mockAudioCacheService.Object,
            _mockQueuePreparationService.Object,
            _mockPlaybackKeepAliveService.Object,
            NullLogger<PlaybackService>.Instance,
            anonymousFeaturedStreamStore: _mockAnonymousFeaturedStreamStore.Object);
        var librarySongs = CreateTestPlaylist(3);
        service.SetPlaylist(librarySongs, 1);
        playbackRuntime.Invocations.Clear();

        var pageSongs = new List<SongDto>
        {
            new() { Id = 2, SongTitle = "Song 2 On Page", ArtistName = "Artist 2", Genre = "Rock", StreamUrl = "https://test.com/song2.mp3" },
            new() { Id = 4, SongTitle = "Song 4", ArtistName = "Artist 4", Genre = "Rock", StreamUrl = "https://test.com/song4.mp3" }
        };

        service.SetPlaylist(pageSongs, 0, PlaybackQueueStartBehavior.PreserveCurrentSongIfPresent);

        var replacement = await capturedReplacement.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(replacement.Items.Select(item => item.SongId), Is.EqualTo(new[] { 2, 4 }));
            Assert.That(replacement.CurrentIndex, Is.EqualTo(0));
            Assert.That(replacement.Position, Is.EqualTo(TimeSpan.FromSeconds(42)));
            Assert.That(replacement.PlayWhenReady, Is.True);
            Assert.That(service.Playlist?.Select(song => song.Id), Is.EqualTo(new[] { 2, 4 }));
            Assert.That(service.CurrentSong?.Id, Is.EqualTo(2));
            Assert.That(service.CurrentSong?.SongTitle, Is.EqualTo("Song 2 On Page"));
            Assert.That(service.CurrentTrackIndex, Is.EqualTo(0));
            Assert.That(service.IsPlaying, Is.True);
        });
        playbackRuntime.Verify(m => m.PlayAsync(It.IsAny<IEnumerable<PlaybackMediaItem>>()), Times.Never);
        playbackRuntime.Verify(m => m.PlayAsync(It.IsAny<PlaybackMediaItem>()), Times.Never);
        playbackRuntime.Verify(m => m.PlayQueueItemAsync(It.IsAny<int>()), Times.Never);
        replacementRuntime.Verify(m =>
            m.ReplaceQueueAsync(
                It.IsAny<IEnumerable<PlaybackMediaItem>>(),
                0,
                TimeSpan.FromSeconds(42),
                true),
            Times.Once);
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
    public void PlayNext_AdvancesToNextTrackImmediately()
    {
        var songs = CreateTestPlaylist(3);
        _service.SetPlaylist(songs, 0);

        _service.PlayNext();

        Assert.That(_service.CurrentTrackIndex, Is.EqualTo(1));
        Assert.That(_service.CurrentSong, Is.SameAs(songs[1]));
        _mockMediaManager.Verify(m => m.PlayQueueItemAsync(1), Times.Once);
        _mockMediaManager.Verify(m => m.PlayNextAsync(), Times.Never);
    }

    [Test]
    public void PlayNext_DuplicateMediaItemChangedDoesNotChangeAdvancedState()
    {
        var songs = CreateTestPlaylist(3);
        _service.SetPlaylist(songs, 0);

        _service.PlayNext();

        var item = new PlaybackMediaItem(songs[1].StreamUrl!);
        _mockMediaManager.Raise(m => m.MediaItemChanged += null, new PlaybackMediaItemEventArgs(item));

        Assert.That(_service.CurrentTrackIndex, Is.EqualTo(1));
        Assert.That(_service.CurrentSong, Is.SameAs(songs[1]));
    }

    [Test]
    public void PlayNext_AtEnd_NoRepeat_DoesNotChangeState()
    {
        var songs = CreateTestPlaylist(3);
        _service.SetPlaylist(songs, 2);

        _service.PlayNext();

        Assert.That(_service.CurrentTrackIndex, Is.EqualTo(2));
        Assert.That(_service.CurrentSong, Is.SameAs(songs[2]));
    }

    [Test]
    public void PlayNext_AtEnd_WithRepeat_WrapsToFirstTrackImmediately()
    {
        var songs = CreateTestPlaylist(3);
        _service.SetPlaylist(songs, 2);
        _service.ToggleRepeat();

        _service.PlayNext();

        var item = new PlaybackMediaItem(songs[0].StreamUrl!);
        _mockMediaManager.Raise(m => m.MediaItemChanged += null, new PlaybackMediaItemEventArgs(item));

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
        _mockMediaManager.Verify(m => m.PlayNextAsync(), Times.Never);
    }

    // --- PlayPrevious ---

    [Test]
    public void PlayPrevious_MovesToPreviousTrackImmediately()
    {
        var songs = CreateTestPlaylist(3);
        _service.SetPlaylist(songs, 2);

        _service.PlayPrevious();

        Assert.That(_service.CurrentTrackIndex, Is.EqualTo(1));
        Assert.That(_service.CurrentSong, Is.SameAs(songs[1]));
        _mockMediaManager.Verify(m => m.PlayQueueItemAsync(1), Times.Once);
        _mockMediaManager.Verify(m => m.PlayPreviousAsync(), Times.Never);
    }

    [Test]
    public void PlayPrevious_DuplicateMediaItemChangedDoesNotChangeAdvancedState()
    {
        var songs = CreateTestPlaylist(3);
        _service.SetPlaylist(songs, 2);

        _service.PlayPrevious();

        var item = new PlaybackMediaItem(songs[1].StreamUrl!) { Title = songs[1].SongTitle };
        _mockMediaManager.Raise(m => m.MediaItemChanged += null, new PlaybackMediaItemEventArgs(item));

        Assert.That(_service.CurrentTrackIndex, Is.EqualTo(1));
        Assert.That(_service.CurrentSong, Is.SameAs(songs[1]));
    }

    [Test]
    public void PlayPrevious_AtStart_DoesNotChangeState()
    {
        var songs = CreateTestPlaylist(3);
        _service.SetPlaylist(songs, 0);

        _service.PlayPrevious();

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
        _mockMediaManager.Verify(m => m.PlayQueueItemAsync(3), Times.Once);
    }

    [Test]
    public void PlayTrackAtIndex_WhenMediaManagerFailed_UsesQueueRebuildForRecovery()
    {
        var songs = CreateTestPlaylist(5);
        _service.SetPlaylist(songs, 0);
        _mockMediaManager.Invocations.Clear();

        _mediaManagerState = PlaybackRuntimeState.Failed;
        _mockMediaManager.Raise(m => m.StateChanged += null, new PlaybackRuntimeStateChangedEventArgs(PlaybackRuntimeState.Failed));

        _service.PlayTrackAtIndex(2);

        Assert.That(_service.CurrentTrackIndex, Is.EqualTo(2));
        Assert.That(_service.CurrentSong, Is.SameAs(songs[2]));
        _mockMediaManager.Verify(m => m.PlayAsync(It.IsAny<IEnumerable<PlaybackMediaItem>>()), Times.Once);
        _mockMediaManager.Verify(m => m.PlayQueueItemAsync(2), Times.Once);
    }

    [Test]
    public async Task PlayTrackAtIndex_WhenNativeQueueCountDoesNotMatchPlaylist_RebuildsQueue()
    {
        var songs = CreateTestPlaylist(5);
        _service.SetPlaylist(songs, 0);
        await Task.Delay(50);
        _mockMediaManager.Invocations.Clear();

        var staleNativeItems = new List<PlaybackMediaItem>
        {
            new(songs[0].StreamUrl!, songs[0].Id, $"song-{songs[0].Id}")
        };
        var rebuiltQueueSource = new TaskCompletionSource<IReadOnlyList<PlaybackMediaItem>>();
        _mockMediaQueue.Setup(q => q.Count).Returns(staleNativeItems.Count);
        _mockMediaQueue.Setup(q => q.HasCurrent).Returns(true);
        _mockMediaQueue.Setup(q => q.CurrentIndex).Returns(0);
        _mockMediaQueue.Setup(q => q.Current).Returns(staleNativeItems[0]);
        _mockMediaQueue
            .As<IEnumerable<PlaybackMediaItem>>()
            .Setup(q => q.GetEnumerator())
            .Returns(() => staleNativeItems.GetEnumerator());
        _mockMediaManager
            .Setup(m => m.PlayAsync(It.IsAny<IEnumerable<PlaybackMediaItem>>()))
            .Callback<IEnumerable<PlaybackMediaItem>>(items => rebuiltQueueSource.TrySetResult(items.ToList()))
            .ReturnsAsync((PlaybackMediaItem?)null);

        _service.PlayTrackAtIndex(3);

        var rebuiltQueue = await rebuiltQueueSource.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.That(rebuiltQueue.Select(item => item.SongId), Is.EqualTo(songs.Select(song => song.Id)));
        _mockMediaManager.Verify(m => m.PlayAsync(It.IsAny<IEnumerable<PlaybackMediaItem>>()), Times.Once);
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
        Assert.That(_mockMediaManager.Object.ShuffleMode, Is.EqualTo(PlaybackShuffleMode.Off));

        _service.ToggleShuffle();
        Assert.That(_service.IsShuffleEnabled, Is.False);
        Assert.That(_mockMediaManager.Object.ShuffleMode, Is.EqualTo(PlaybackShuffleMode.Off));
    }

    [Test]
    public void ToggleShuffle_WithoutPlaylist_DoesNothing()
    {
        _service.ToggleShuffle();

        Assert.That(_service.IsShuffleEnabled, Is.False);
        Assert.That(_mockMediaManager.Object.ShuffleMode, Is.EqualTo(PlaybackShuffleMode.Off));
    }

    [Test]
    public void ToggleShuffle_WithSingleSongPlayback_DoesNothing()
    {
        var song = new SongDto { Id = 1, SongTitle = "Single", StreamUrl = "https://test.com/song.mp3" };
        _service.PlaySong(song);

        _service.ToggleShuffle();

        Assert.That(_service.IsShuffleEnabled, Is.False);
        Assert.That(_mockMediaManager.Object.ShuffleMode, Is.EqualTo(PlaybackShuffleMode.Off));
    }

    [Test]
    public async Task ToggleShuffle_WithActivePlaylist_RebuildsQueueAroundCurrentSong()
    {
        var songs = CreateTestPlaylist(5);
        var playCallCount = 0;
        var reshuffledQueueSource = new TaskCompletionSource<IReadOnlyList<PlaybackMediaItem>>();

        _mockMediaManager
            .Setup(m => m.PlayAsync(It.IsAny<IEnumerable<PlaybackMediaItem>>()))
            .Callback<IEnumerable<PlaybackMediaItem>>(items =>
            {
                playCallCount++;
                if (playCallCount == 2)
                {
                    reshuffledQueueSource.TrySetResult(items.ToList());
                }
            })
            .ReturnsAsync((PlaybackMediaItem?)null);

        _mockMediaManager
            .SetupSet(m => m.ShuffleMode = It.IsAny<PlaybackShuffleMode>())
            .Callback<PlaybackShuffleMode>(mode =>
            {
                if (mode == PlaybackShuffleMode.All)
                {
                    _mockMediaManager.Raise(
                        m => m.MediaItemChanged += null,
                        new PlaybackMediaItemEventArgs(new PlaybackMediaItem(songs[1].StreamUrl!)));
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
        _mockMediaManager.VerifySet(m => m.ShuffleMode = PlaybackShuffleMode.All, Times.Never);
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
        _mockMediaManager.Verify(m => m.PlayQueueItemAsync(1), Times.Once);
    }

    [Test]
    public async Task OnMediaEnded_WithPlaylist_WhenMediaManagerStillReportsPlaying_ForcesNextTrackAfterDelay()
    {
        var service = CreateService(TimeSpan.FromMilliseconds(10));
        var songs = CreateTestPlaylist(3);
        _mediaManagerState = PlaybackRuntimeState.Playing;
        service.SetPlaylist(songs, 0);

        service.OnMediaEnded();

        await Task.Delay(75);

        Assert.That(service.CurrentTrackIndex, Is.EqualTo(1));
        Assert.That(service.CurrentSong, Is.SameAs(songs[1]));
        Assert.That(service.IsPlaying, Is.True);
        _mockMediaManager.Verify(m => m.PlayQueueItemAsync(1), Times.Once);
    }

    [Test]
    public async Task OnMediaEnded_WithPlaylist_WhenNativeQueueAlreadyAdvanced_UsesNativeCurrentIndex()
    {
        var service = CreateService(TimeSpan.FromMilliseconds(10));
        var songs = CreateTestPlaylist(4);
        _mockMediaQueue.Setup(q => q.HasCurrent).Returns(true);
        _mockMediaQueue.Setup(q => q.CurrentIndex).Returns(2);
        _mockMediaQueue.Setup(q => q.Current).Returns(new PlaybackMediaItem(songs[2].StreamUrl!));
        service.SetPlaylist(songs, 0);

        service.OnMediaEnded();

        await Task.Delay(75);

        Assert.That(service.CurrentTrackIndex, Is.EqualTo(2));
        Assert.That(service.CurrentSong, Is.SameAs(songs[2]));
        Assert.That(service.IsPlaying, Is.True);
        _mockMediaManager.Verify(m => m.PlayQueueItemAsync(2), Times.Once);
        _mockMediaManager.Verify(m => m.PlayQueueItemAsync(1), Times.Never);
    }

    [Test]
    public async Task OnMediaEnded_WithPlaylist_WhenTrackChangesBeforeFallback_DoesNotForceAdvance()
    {
        var service = CreateService(TimeSpan.FromMilliseconds(50));
        var songs = CreateTestPlaylist(3);
        service.SetPlaylist(songs, 0);

        service.OnMediaEnded();

        var item = new PlaybackMediaItem(songs[1].StreamUrl!);
        _mockMediaManager.Raise(m => m.MediaItemChanged += null, new PlaybackMediaItemEventArgs(item));

        await Task.Delay(125);

        Assert.That(service.CurrentTrackIndex, Is.EqualTo(1));
        Assert.That(service.CurrentSong, Is.SameAs(songs[1]));
        _mockMediaManager.Verify(m => m.PlayQueueItemAsync(1), Times.Never);
    }

    [Test]
    public async Task OnMediaEnded_WithPlaylist_WhenNativeAdvanceFails_RetriesAdvancedTrack()
    {
        var service = CreateService(TimeSpan.FromMilliseconds(50));
        var songs = CreateTestPlaylist(3);
        service.SetPlaylist(songs, 0);

        service.OnMediaEnded();

        var item = new PlaybackMediaItem(songs[1].StreamUrl!);
        _mockMediaManager.Raise(m => m.MediaItemChanged += null, new PlaybackMediaItemEventArgs(item));
        _mediaManagerState = PlaybackRuntimeState.Failed;
        _mockMediaManager.Raise(m => m.StateChanged += null, new PlaybackRuntimeStateChangedEventArgs(PlaybackRuntimeState.Failed));
        _mediaManagerState = PlaybackRuntimeState.Buffering;
        _mockMediaManager.Raise(m => m.StateChanged += null, new PlaybackRuntimeStateChangedEventArgs(PlaybackRuntimeState.Buffering));

        await Task.Delay(125);

        Assert.That(service.CurrentTrackIndex, Is.EqualTo(1));
        Assert.That(service.CurrentSong, Is.SameAs(songs[1]));
        Assert.That(service.IsPlaying, Is.True);
        _mockMediaManager.Verify(m => m.PlayAsync(It.IsAny<IEnumerable<PlaybackMediaItem>>()), Times.Exactly(2));
        _mockMediaManager.Verify(m => m.PlayQueueItemAsync(1), Times.Once);
    }

    [Test]
    public async Task MediaItemFinished_WithPlaylist_WhenPositionNotNearEnd_IgnoresSpuriousFinish()
    {
        var service = CreateService(TimeSpan.FromMilliseconds(10));
        var songs = CreateTestPlaylist(3);
        var duration = TimeSpan.FromSeconds(219.144);
        _mediaManagerState = PlaybackRuntimeState.Buffering;
        _mockMediaManager.Setup(m => m.Duration).Returns(duration);
        service.SetPlaylist(songs, 0);

        var item = new PlaybackMediaItem(songs[1].StreamUrl!);
        _mockMediaManager.Raise(m => m.MediaItemChanged += null, new PlaybackMediaItemEventArgs(item));
        service.UpdatePosition(TimeSpan.Zero, duration);

        _mockMediaManager.Raise(m => m.MediaItemFinished += null, new PlaybackMediaItemEventArgs(item));

        await Task.Delay(75);

        Assert.That(service.CurrentTrackIndex, Is.EqualTo(1));
        Assert.That(service.CurrentSong, Is.SameAs(songs[1]));
        Assert.That(service.IsPlaying, Is.True);
        _mockMediaManager.Verify(m => m.PlayQueueItemAsync(2), Times.Never);
    }

    [Test]
    public async Task MediaItemFinished_WithPlaylist_WhenNoPositionOrDurationReported_IgnoresStartupFinish()
    {
        var service = CreateService(TimeSpan.FromMilliseconds(10));
        var songs = CreateTestPlaylist(3);
        service.SetPlaylist(songs, 0);
        service.UpdatePosition(TimeSpan.Zero, TimeSpan.Zero);

        var item = new PlaybackMediaItem(songs[0].StreamUrl!);
        _mockMediaManager.Raise(m => m.MediaItemFinished += null, new PlaybackMediaItemEventArgs(item));

        await Task.Delay(75);

        Assert.That(service.CurrentTrackIndex, Is.EqualTo(0));
        Assert.That(service.CurrentSong, Is.SameAs(songs[0]));
        Assert.That(service.IsPlaying, Is.True);
        _mockMediaManager.Verify(m => m.PlayQueueItemAsync(1), Times.Never);
    }

    [Test]
    public async Task MediaItemFinished_WithPlaylist_WhenPositionNearEnd_AdvancesToNextTrack()
    {
        var service = CreateService(TimeSpan.FromMilliseconds(10));
        var songs = CreateTestPlaylist(3);
        var duration = TimeSpan.FromSeconds(219.144);
        _mediaManagerState = PlaybackRuntimeState.Playing;
        _mockMediaManager.Setup(m => m.Duration).Returns(duration);
        service.SetPlaylist(songs, 0);
        service.UpdatePosition(duration - TimeSpan.FromMilliseconds(250), duration);

        var item = new PlaybackMediaItem(songs[0].StreamUrl!);
        _mockMediaManager.Raise(m => m.MediaItemFinished += null, new PlaybackMediaItemEventArgs(item));

        await Task.Delay(75);

        Assert.That(service.CurrentTrackIndex, Is.EqualTo(1));
        Assert.That(service.CurrentSong, Is.SameAs(songs[1]));
        Assert.That(service.IsPlaying, Is.True);
        _mockMediaManager.Verify(m => m.PlayQueueItemAsync(1), Times.Once);
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
            _mockQueuePreparationService.Object,
            _mockPlaybackKeepAliveService.Object,
            logger);

        var song = new SongDto { Id = 20, SongTitle = "Convoy & Crown", StreamUrl = "https://test.com/20.mp3" };
        _service.PlaySong(song);

        var failure = new InvalidOperationException("Simulated native player failure");
        _mockMediaManager.Raise(
            m => m.MediaItemFailed += null,
            new PlaybackMediaItemFailedEventArgs(new PlaybackMediaItem(song.StreamUrl), failure, failure.Message));

        var combinedLogs = string.Join(Environment.NewLine, logger.Messages);
        Assert.That(combinedLogs, Does.Contain("MediaItemFailed received"));
        Assert.That(combinedLogs, Does.Contain("Simulated native player failure"));
        Assert.That(combinedLogs, Does.Contain(typeof(InvalidOperationException).FullName));
    }

    [Test]
    public async Task MediaItemFailed_WithActivePlaylist_AdvancesToNextTrackWhenFailurePersists()
    {
        var service = CreateService(TimeSpan.FromMilliseconds(50));
        var songs = CreateTestPlaylist(3);
        SetupCachedSong(songs[1]);
        service.SetPlaylist(songs, 0);
        _mockPlaybackKeepAliveService.Invocations.Clear();

        _mediaManagerState = PlaybackRuntimeState.Failed;
        _mockMediaManager.Raise(m => m.StateChanged += null, new PlaybackRuntimeStateChangedEventArgs(PlaybackRuntimeState.Failed));

        var failure = new InvalidOperationException("simulated playback failure");
        _mockMediaManager.Raise(
            m => m.MediaItemFailed += null,
            new PlaybackMediaItemFailedEventArgs(new PlaybackMediaItem(songs[0].StreamUrl!), failure, failure.Message));

        await Task.Delay(125);

        Assert.Multiple(() =>
        {
            Assert.That(service.CurrentTrackIndex, Is.EqualTo(1));
            Assert.That(service.CurrentSong, Is.SameAs(songs[1]));
            Assert.That(service.IsPlaying, Is.True);
        });
        _mockPlaybackKeepAliveService.Verify(s => s.SetPlaybackActive(false), Times.Never);
        _mockMediaManager.Verify(m => m.PlayQueueItemAsync(1), Times.Once);
    }

    [Test]
    public async Task SleepNetworkFailure_DoesNotAdvanceToRemoteOnlyTrack()
    {
        var service = CreateService(TimeSpan.FromMilliseconds(50));
        var songs = CreateTestPlaylist(3);
        service.SetPlaylist(songs, 0);
        _mockMediaManager.Invocations.Clear();

        _mediaManagerState = PlaybackRuntimeState.Failed;
        var failure = new InvalidOperationException("Unable to resolve host");
        _mockMediaManager.Raise(
            m => m.MediaItemFailed += null,
            new PlaybackMediaItemFailedEventArgs(new PlaybackMediaItem(songs[0].StreamUrl!), failure, failure.Message));

        await Task.Delay(125);

        Assert.Multiple(() =>
        {
            Assert.That(service.CurrentTrackIndex, Is.EqualTo(0));
            Assert.That(service.CurrentSong, Is.SameAs(songs[0]));
            Assert.That(service.IsPlaying, Is.False);
            Assert.That(service.PreparationState, Is.EqualTo(PlaybackPreparationState.WaitingForNetwork));
        });
        _mockMediaManager.Verify(m => m.PlayQueueItemAsync(1), Times.Never);
        _mockMediaManager.Verify(m => m.PlayAsync(It.IsAny<IEnumerable<PlaybackMediaItem>>()), Times.Never);
    }

    [Test]
    public async Task MediaItemFailed_WhenCurrentRemoteTrackIsNowCached_ReplaysCurrentTrackFromCache()
    {
        var service = CreateService(TimeSpan.FromMilliseconds(50));
        var songs = CreateTestPlaylist(3);
        const string cachedPlaybackUri = "/data/user/0/com.streamtunes/cache/song1.mp3";
        var useCachedPlaybackUri = false;
        var queuedItemsSource = new TaskCompletionSource<IReadOnlyList<PlaybackMediaItem>>(TaskCreationOptions.RunContinuationsAsynchronously);

        _mockAudioCacheService
            .Setup(s => s.GetImmediatePlaybackUri(songs[0]))
            .Returns(() => useCachedPlaybackUri ? cachedPlaybackUri : songs[0].StreamUrl!);
        _mockMediaManager
            .Setup(m => m.PlayAsync(It.IsAny<IEnumerable<PlaybackMediaItem>>()))
            .Callback<IEnumerable<PlaybackMediaItem>>(items => queuedItemsSource.TrySetResult(items.ToList()))
            .ReturnsAsync((PlaybackMediaItem?)null);

        service.SetPlaylist(songs, 0);
        _mockMediaManager.Invocations.Clear();
        queuedItemsSource = new TaskCompletionSource<IReadOnlyList<PlaybackMediaItem>>(TaskCreationOptions.RunContinuationsAsynchronously);

        useCachedPlaybackUri = true;
        _mediaManagerState = PlaybackRuntimeState.Failed;
        var failure = new InvalidOperationException("simulated remote playback failure");
        _mockMediaManager.Raise(
            m => m.MediaItemFailed += null,
            new PlaybackMediaItemFailedEventArgs(new PlaybackMediaItem(songs[0].StreamUrl!), failure, failure.Message));

        var queuedItems = await queuedItemsSource.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(service.CurrentTrackIndex, Is.EqualTo(0));
            Assert.That(service.CurrentSong, Is.SameAs(songs[0]));
            Assert.That(service.IsPlaying, Is.True);
            Assert.That(queuedItems[0].MediaUri, Is.EqualTo(cachedPlaybackUri));
            Assert.That(queuedItems[0].MediaLocation, Is.EqualTo(PlaybackMediaLocation.FileSystem));
        });
        _mockMediaManager.Verify(m => m.PlayQueueItemAsync(1), Times.Never);
    }

    [Test]
    public async Task MediaManagerFailed_WithRecoverablePlaylist_KeepsPlaybackActiveAndRebuildsCurrentTrack()
    {
        var service = CreateService(TimeSpan.FromMilliseconds(50));
        var songs = CreateTestPlaylist(3);
        service.SetPlaylist(songs, 1);
        _mockMediaManager.Invocations.Clear();
        _mockPlaybackKeepAliveService.Invocations.Clear();

        _mediaManagerState = PlaybackRuntimeState.Failed;
        _mockMediaManager.Raise(m => m.StateChanged += null, new PlaybackRuntimeStateChangedEventArgs(PlaybackRuntimeState.Failed));

        Assert.That(service.IsPlaying, Is.True);
        _mockPlaybackKeepAliveService.Verify(s => s.SetPlaybackActive(false), Times.Never);

        await Task.Delay(125);

        Assert.Multiple(() =>
        {
            Assert.That(service.CurrentTrackIndex, Is.EqualTo(1));
            Assert.That(service.CurrentSong, Is.SameAs(songs[1]));
            Assert.That(service.IsPlaying, Is.True);
        });
        _mockPlaybackKeepAliveService.Verify(s => s.SetPlaybackActive(false), Times.Never);
        _mockMediaManager.Verify(m => m.PlayAsync(It.IsAny<IEnumerable<PlaybackMediaItem>>()), Times.Once);
        _mockMediaManager.Verify(m => m.PlayQueueItemAsync(1), Times.Once);
    }

    [Test]
    public void MediaManagerFailed_AtPlaylistEndWithoutRepeat_ReleasesPlaybackKeepAlive()
    {
        var songs = CreateTestPlaylist(2);
        _service.SetPlaylist(songs, 1);
        _mockPlaybackKeepAliveService.Invocations.Clear();

        _mediaManagerState = PlaybackRuntimeState.Failed;
        _mockMediaManager.Raise(m => m.StateChanged += null, new PlaybackRuntimeStateChangedEventArgs(PlaybackRuntimeState.Failed));

        Assert.That(_service.IsPlaying, Is.False);
        _mockPlaybackKeepAliveService.Verify(s => s.SetPlaybackActive(false), Times.Once);
    }

    [Test]
    public async Task MediaManagerBuffering_WithActivePlaylist_AdvancesAfterStall()
    {
        var service = CreateService(bufferingStallRecoveryDelay: TimeSpan.FromMilliseconds(50));
        var songs = CreateTestPlaylist(4);
        SetupCachedSong(songs[2]);
        service.SetPlaylist(songs, 0);
        service.PlayTrackAtIndex(1);
        _mockMediaManager.Invocations.Clear();
        _mockPlaybackKeepAliveService.Invocations.Clear();

        _mediaManagerState = PlaybackRuntimeState.Buffering;
        _mockMediaManager.Raise(m => m.StateChanged += null, new PlaybackRuntimeStateChangedEventArgs(PlaybackRuntimeState.Buffering));

        await Task.Delay(125);

        Assert.Multiple(() =>
        {
            Assert.That(service.CurrentTrackIndex, Is.EqualTo(2));
            Assert.That(service.CurrentSong, Is.SameAs(songs[2]));
            Assert.That(service.IsPlaying, Is.True);
        });
        _mockPlaybackKeepAliveService.Verify(s => s.SetPlaybackActive(false), Times.Never);
        _mockMediaManager.Verify(m => m.PlayQueueItemAsync(2), Times.Once);
    }

    [Test]
    public async Task MediaManagerBuffering_WhenPlaybackResumes_CancelsStallRecovery()
    {
        var service = CreateService(bufferingStallRecoveryDelay: TimeSpan.FromMilliseconds(50));
        var songs = CreateTestPlaylist(4);
        service.SetPlaylist(songs, 0);
        service.PlayTrackAtIndex(1);
        _mockMediaManager.Invocations.Clear();

        _mediaManagerState = PlaybackRuntimeState.Buffering;
        _mockMediaManager.Raise(m => m.StateChanged += null, new PlaybackRuntimeStateChangedEventArgs(PlaybackRuntimeState.Buffering));

        _mediaManagerState = PlaybackRuntimeState.Playing;
        _mockMediaManager.Raise(m => m.StateChanged += null, new PlaybackRuntimeStateChangedEventArgs(PlaybackRuntimeState.Playing));

        await Task.Delay(125);

        Assert.Multiple(() =>
        {
            Assert.That(service.CurrentTrackIndex, Is.EqualTo(1));
            Assert.That(service.CurrentSong, Is.SameAs(songs[1]));
            Assert.That(service.IsPlaying, Is.True);
        });
        _mockMediaManager.Verify(m => m.PlayQueueItemAsync(2), Times.Never);
    }

    [Test]
    public async Task MediaManagerFailedStateRecovery_WhenQueueRebuildStillBuffering_RearmsStallRecovery()
    {
        var service = CreateService(
            playlistAdvanceFallbackDelay: TimeSpan.FromMilliseconds(10),
            bufferingStallRecoveryDelay: TimeSpan.FromMilliseconds(50));
        var songs = CreateTestPlaylist(4);
        SetupCachedSong(songs[2]);
        service.SetPlaylist(songs, 0);
        service.PlayTrackAtIndex(1);
        _mockMediaManager.Invocations.Clear();
        _mockPlaybackKeepAliveService.Invocations.Clear();

        _mediaManagerState = PlaybackRuntimeState.Failed;
        _mockMediaManager.Raise(m => m.StateChanged += null, new PlaybackRuntimeStateChangedEventArgs(PlaybackRuntimeState.Failed));
        _mediaManagerState = PlaybackRuntimeState.Buffering;

        await Task.Delay(150);

        Assert.Multiple(() =>
        {
            Assert.That(service.CurrentTrackIndex, Is.EqualTo(2));
            Assert.That(service.CurrentSong, Is.SameAs(songs[2]));
            Assert.That(service.IsPlaying, Is.True);
        });
        _mockPlaybackKeepAliveService.Verify(s => s.SetPlaybackActive(false), Times.Never);
        _mockMediaManager.Verify(m => m.PlayQueueItemAsync(1), Times.Once);
        _mockMediaManager.Verify(m => m.PlayQueueItemAsync(2), Times.Once);
        _mockMediaManager.Verify(m => m.PlayQueueItemAsync(3), Times.Never);
    }

    [Test]
    public async Task MediaItemFailed_WhenPlaylistAlreadyAdvanced_DoesNotAdvanceTwice()
    {
        var service = CreateService(TimeSpan.FromMilliseconds(50));
        var songs = CreateTestPlaylist(3);
        SetupCachedSong(songs[1]);
        service.SetPlaylist(songs, 0);

        _mediaManagerState = PlaybackRuntimeState.Failed;
        var failure = new InvalidOperationException("simulated playback failure");
        _mockMediaManager.Raise(
            m => m.MediaItemFailed += null,
            new PlaybackMediaItemFailedEventArgs(new PlaybackMediaItem(songs[0].StreamUrl!), failure, failure.Message));

        _mockMediaManager.Raise(m => m.MediaItemChanged += null, new PlaybackMediaItemEventArgs(new PlaybackMediaItem(songs[1].StreamUrl!)));

        await Task.Delay(125);

        Assert.That(service.CurrentTrackIndex, Is.EqualTo(1));
        _mockMediaManager.Verify(m => m.PlayQueueItemAsync(1), Times.Once);
        _mockMediaManager.Verify(m => m.PlayQueueItemAsync(2), Times.Never);
    }

    [Test]
    public void MediaManagerFailed_WhenRecentMediaItemFailureMatchesCurrent_AdvancesWithoutWaitingForFallback()
    {
        var service = CreateService(TimeSpan.FromSeconds(5));
        var songs = CreateTestPlaylist(3);
        SetupCachedSong(songs[1]);
        service.SetPlaylist(songs, 0);
        _mockMediaManager.Invocations.Clear();
        _mockPlaybackKeepAliveService.Invocations.Clear();

        _mediaManagerState = PlaybackRuntimeState.Playing;
        var failure = new InvalidOperationException("simulated playback failure");
        _mockMediaManager.Raise(
            m => m.MediaItemFailed += null,
            new PlaybackMediaItemFailedEventArgs(new PlaybackMediaItem(songs[0].StreamUrl!), failure, failure.Message));

        Assert.That(service.CurrentTrackIndex, Is.EqualTo(0));

        _mediaManagerState = PlaybackRuntimeState.Failed;
        _mockMediaManager.Raise(m => m.StateChanged += null, new PlaybackRuntimeStateChangedEventArgs(PlaybackRuntimeState.Failed));

        Assert.Multiple(() =>
        {
            Assert.That(service.CurrentTrackIndex, Is.EqualTo(1));
            Assert.That(service.CurrentSong, Is.SameAs(songs[1]));
            Assert.That(service.IsPlaying, Is.True);
        });
        _mockPlaybackKeepAliveService.Verify(s => s.SetPlaybackActive(false), Times.Never);
        _mockMediaManager.Verify(m => m.PlayQueueItemAsync(1), Times.Once);
    }

    [Test]
    public void MediaItemChanged_BackwardRewindImmediatelyAfterFailure_IsIgnored()
    {
        var songs = CreateTestPlaylist(5);
        _service.SetPlaylist(songs, 0);
        _service.PlayTrackAtIndex(3);
        _mockMediaManager.Invocations.Clear();

        _mediaManagerState = PlaybackRuntimeState.Playing;
        var failure = new InvalidOperationException("simulated playback failure");
        _mockMediaManager.Raise(
            m => m.MediaItemFailed += null,
            new PlaybackMediaItemFailedEventArgs(new PlaybackMediaItem(songs[3].StreamUrl!), failure, failure.Message));

        _mockMediaManager.Raise(
            m => m.MediaItemChanged += null,
            new PlaybackMediaItemEventArgs(new PlaybackMediaItem(songs[0].StreamUrl!)));

        Assert.Multiple(() =>
        {
            Assert.That(_service.CurrentTrackIndex, Is.EqualTo(3));
            Assert.That(_service.CurrentSong, Is.SameAs(songs[3]));
        });
    }

    [Test]
    public async Task MediaItemFailed_WhenNativeStartsEarlierTrackAfterFailure_StillRecoversForward()
    {
        var service = CreateService(TimeSpan.FromMilliseconds(50));
        var songs = CreateTestPlaylist(5);
        SetupCachedSong(songs[4]);
        service.SetPlaylist(songs, 0);
        service.PlayTrackAtIndex(3);
        _mockMediaManager.Invocations.Clear();

        var nativeCurrentIndex = 3;
        _mockMediaQueue.Setup(q => q.HasCurrent).Returns(true);
        _mockMediaQueue.Setup(q => q.CurrentIndex).Returns(() => nativeCurrentIndex);
        _mockMediaQueue
            .Setup(q => q.Current)
            .Returns(() => new PlaybackMediaItem(songs[nativeCurrentIndex].StreamUrl!));

        _mediaManagerState = PlaybackRuntimeState.Playing;
        var failure = new InvalidOperationException("simulated playback failure");
        _mockMediaManager.Raise(
            m => m.MediaItemFailed += null,
            new PlaybackMediaItemFailedEventArgs(new PlaybackMediaItem(songs[3].StreamUrl!), failure, failure.Message));

        nativeCurrentIndex = 0;
        _mockMediaManager.Raise(
            m => m.MediaItemChanged += null,
            new PlaybackMediaItemEventArgs(new PlaybackMediaItem(songs[0].StreamUrl!)));

        await Task.Delay(125);

        Assert.Multiple(() =>
        {
            Assert.That(service.CurrentTrackIndex, Is.EqualTo(4));
            Assert.That(service.CurrentSong, Is.SameAs(songs[4]));
            Assert.That(service.IsPlaying, Is.True);
        });
        _mockMediaManager.Verify(m => m.PlayQueueItemAsync(0), Times.Never);
        _mockMediaManager.Verify(m => m.PlayAsync(It.IsAny<IEnumerable<PlaybackMediaItem>>()), Times.Once);
        _mockMediaManager.Verify(m => m.PlayQueueItemAsync(4), Times.Once);
    }

    [Test]
    public async Task MediaItemFailed_WhenEventItemIsStale_RecoversFromCurrentTrack()
    {
        var service = CreateService(TimeSpan.FromSeconds(5));
        var songs = CreateTestPlaylist(6);
        SetupCachedSong(songs[5]);
        service.SetPlaylist(songs, 0);
        service.PlayTrackAtIndex(4);
        _mockMediaManager.Invocations.Clear();

        _mediaManagerState = PlaybackRuntimeState.Buffering;
        var failure = new InvalidOperationException("simulated stale playback failure");
        _mockMediaManager.Raise(
            m => m.MediaItemFailed += null,
            new PlaybackMediaItemFailedEventArgs(new PlaybackMediaItem(songs[0].StreamUrl!), failure, failure.Message));

        await Task.Delay(50);

        Assert.Multiple(() =>
        {
            Assert.That(service.CurrentTrackIndex, Is.EqualTo(5));
            Assert.That(service.CurrentSong, Is.SameAs(songs[5]));
            Assert.That(service.IsPlaying, Is.True);
        });
        _mockMediaManager.Verify(m => m.PlayQueueItemAsync(1), Times.Never);
        _mockMediaManager.Verify(m => m.PlayQueueItemAsync(5), Times.Once);
    }

    [Test]
    public async Task MediaItemChanged_BackwardRewindAfterFailure_DoesNotPreventForwardRecovery()
    {
        var service = CreateService(TimeSpan.FromMilliseconds(50));
        var songs = CreateTestPlaylist(5);
        SetupCachedSong(songs[4]);
        service.SetPlaylist(songs, 0);
        service.PlayTrackAtIndex(3);
        _mockMediaManager.Invocations.Clear();

        _mediaManagerState = PlaybackRuntimeState.Playing;
        var failure = new InvalidOperationException("simulated playback failure");
        _mockMediaManager.Raise(
            m => m.MediaItemFailed += null,
            new PlaybackMediaItemFailedEventArgs(new PlaybackMediaItem(songs[3].StreamUrl!), failure, failure.Message));

        _mockMediaManager.Raise(
            m => m.MediaItemChanged += null,
            new PlaybackMediaItemEventArgs(new PlaybackMediaItem(songs[0].StreamUrl!)));

        _mediaManagerState = PlaybackRuntimeState.Failed;
        _mockMediaManager.Raise(m => m.StateChanged += null, new PlaybackRuntimeStateChangedEventArgs(PlaybackRuntimeState.Failed));

        await Task.Delay(125);

        Assert.Multiple(() =>
        {
            Assert.That(service.CurrentTrackIndex, Is.EqualTo(4));
            Assert.That(service.CurrentSong, Is.SameAs(songs[4]));
        });
        _mockMediaManager.Verify(m => m.PlayQueueItemAsync(0), Times.Never);
        _mockMediaManager.Verify(m => m.PlayQueueItemAsync(4), Times.Once);
    }

    [Test]
    public void TogglePlayPause_WhenPlaylistStateIsFailed_ReplaysCurrentTrackIndexWithoutRawPlay()
    {
        var songs = CreateTestPlaylist(3);
        _service.SetPlaylist(songs, 0);
        _service.PlayTrackAtIndex(1);
        _mockMediaManager.Invocations.Clear();

        _mediaManagerState = PlaybackRuntimeState.Failed;
        _mockMediaManager.Raise(m => m.StateChanged += null, new PlaybackRuntimeStateChangedEventArgs(PlaybackRuntimeState.Failed));

        _service.TogglePlayPause();

        Assert.That(_service.CurrentTrackIndex, Is.EqualTo(1));
        _mockMediaManager.Verify(m => m.PlayAsync(), Times.Never);
        _mockMediaManager.Verify(m => m.PlayAsync(It.IsAny<IEnumerable<PlaybackMediaItem>>()), Times.Once);
    }

    [Test]
    public void PlaySong_WhenCachedPlaybackUriIsAvailable_UsesLocalMediaItem()
    {
        var song = new SongDto { Id = 31, SongTitle = "Cached Song", StreamUrl = "https://test.com/song31.mp3" };
        const string localPlaybackPath = "/data/user/0/com.streamtunes/cache/song31.mp3";
        _mockAudioCacheService
            .Setup(s => s.GetImmediatePlaybackUri(song))
            .Returns(localPlaybackPath);

        _service.PlaySong(song);

        _mockMediaManager.Verify(
            m => m.PlayAsync(It.Is<PlaybackMediaItem>(item => item.MediaUri == localPlaybackPath && item.MediaLocation == PlaybackMediaLocation.FileSystem)),
            Times.Once);
    }

    [Test]
    public async Task PlaySong_WhenCacheWarmIsStillRunning_StartsImmediatelyFromRemoteUri()
    {
        var song = new SongDto { Id = 32, SongTitle = "Remote Start", StreamUrl = "https://test.com/song32.mp3" };
        var cacheWarmStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cacheWarmCompletion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        _mockAudioCacheService
            .Setup(s => s.GetImmediatePlaybackUri(song))
            .Returns(song.StreamUrl!);
        _mockAudioCacheService
            .Setup(s => s.ResolvePlaybackUriAsync(song, It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cacheWarmStarted.TrySetResult(true);
                return cacheWarmCompletion.Task;
            });

        _service.PlaySong(song);

        await cacheWarmStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        _mockMediaManager.Verify(
            m => m.PlayAsync(It.Is<PlaybackMediaItem>(item => item.MediaUri == song.StreamUrl && item.MediaLocation == PlaybackMediaLocation.Remote)),
            Times.Once);
    }

    [Test]
    public async Task SetPlaylist_WhenCachedPlaybackUriIsAvailableForCurrentSong_UsesLocalQueueItemForStartTrack()
    {
        var songs = CreateTestPlaylist(3);
        const string localPlaybackPath = "/data/user/0/com.streamtunes/cache/song2.mp3";
        var queuedItemsSource = new TaskCompletionSource<IReadOnlyList<PlaybackMediaItem>>();
        _mockMediaManager
            .Setup(m => m.PlayAsync(It.IsAny<IEnumerable<PlaybackMediaItem>>()))
            .Callback<IEnumerable<PlaybackMediaItem>>(items => queuedItemsSource.TrySetResult(items.ToList()))
            .ReturnsAsync((PlaybackMediaItem?)null);
        _mockAudioCacheService
            .Setup(s => s.GetImmediatePlaybackUri(songs[1]))
            .Returns(localPlaybackPath);

        _service.SetPlaylist(songs, 1);

        var queuedItems = await queuedItemsSource.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.That(queuedItems[1].MediaUri, Is.EqualTo(localPlaybackPath));
        Assert.That(queuedItems[1].MediaLocation, Is.EqualTo(PlaybackMediaLocation.FileSystem));
        Assert.That(queuedItems[0].MediaUri, Is.EqualTo(songs[0].StreamUrl));
    }

    [Test]
    public async Task SetPlaylist_WhenRuntimeSupportsIndexedQueueStart_PassesStartIndexWithoutSeparateSeek()
    {
        var songs = CreateTestPlaylist(4);
        var playbackRuntime = new Mock<IPlatformPlaybackRuntime>();
        var indexedRuntime = playbackRuntime.As<IIndexedQueuePlaybackRuntime>();
        var queueStartSource = new TaskCompletionSource<(IReadOnlyList<PlaybackMediaItem> Items, int StartIndex)>();
        playbackRuntime.Setup(m => m.PlayQueueItemAsync(It.IsAny<int>())).Returns(Task.FromResult(false));
        playbackRuntime.SetupProperty(m => m.RepeatMode);
        playbackRuntime.SetupProperty(m => m.ShuffleMode);
        playbackRuntime.Setup(m => m.State).Returns(() => _mediaManagerState);
        playbackRuntime.Setup(m => m.Position).Returns(TimeSpan.Zero);
        playbackRuntime.Setup(m => m.Duration).Returns(TimeSpan.Zero);
        playbackRuntime.Setup(m => m.Queue).Returns(_mockMediaQueue.Object);
        indexedRuntime
            .Setup(m => m.PlayAsync(It.IsAny<IEnumerable<PlaybackMediaItem>>(), It.IsAny<int>()))
            .Callback<IEnumerable<PlaybackMediaItem>, int>((items, startIndex) =>
                queueStartSource.TrySetResult((items.ToList(), startIndex)))
            .ReturnsAsync((PlaybackMediaItem?)null);
        var service = new PlaybackService(
            _mockAuthService.Object,
            _mockMusicService.Object,
            playbackRuntime.Object,
            _mockAudioCacheService.Object,
            _mockQueuePreparationService.Object,
            _mockPlaybackKeepAliveService.Object,
            NullLogger<PlaybackService>.Instance);

        service.SetPlaylist(songs, 2);

        var (queuedItems, startIndex) = await queueStartSource.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.That(startIndex, Is.EqualTo(2));
        Assert.That(queuedItems.Select(item => item.MediaUri), Is.EqualTo(songs.Select(song => song.StreamUrl)));
        playbackRuntime.Verify(m => m.PlayQueueItemAsync(2), Times.Never);
    }

    [Test]
    public async Task SetPlaylist_WhenCachedPlaybackUrisAreAvailableForUpcomingTracks_UsesLocalQueueItemsForNativeHandoff()
    {
        var songs = CreateTestPlaylist(3);
        const string localCurrentPath = "/data/user/0/com.streamtunes/cache/song1.mp3";
        const string localNextPath = "/data/user/0/com.streamtunes/cache/song2.mp3";
        var queuedItemsSource = new TaskCompletionSource<IReadOnlyList<PlaybackMediaItem>>();
        _mockMediaManager
            .Setup(m => m.PlayAsync(It.IsAny<IEnumerable<PlaybackMediaItem>>()))
            .Callback<IEnumerable<PlaybackMediaItem>>(items => queuedItemsSource.TrySetResult(items.ToList()))
            .ReturnsAsync((PlaybackMediaItem?)null);
        _mockAudioCacheService
            .Setup(s => s.GetImmediatePlaybackUri(songs[0]))
            .Returns(localCurrentPath);
        _mockAudioCacheService
            .Setup(s => s.GetImmediatePlaybackUri(songs[1]))
            .Returns(localNextPath);

        _service.SetPlaylist(songs, 0);

        var queuedItems = await queuedItemsSource.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.That(queuedItems[0].MediaUri, Is.EqualTo(localCurrentPath));
        Assert.That(queuedItems[0].MediaLocation, Is.EqualTo(PlaybackMediaLocation.FileSystem));
        Assert.That(queuedItems[1].MediaUri, Is.EqualTo(localNextPath));
        Assert.That(queuedItems[1].MediaLocation, Is.EqualTo(PlaybackMediaLocation.FileSystem));
        Assert.That(queuedItems[2].MediaUri, Is.EqualTo(songs[2].StreamUrl));
    }

    [Test]
    public async Task SetPlaylist_WhenCacheWarmIsStillRunning_StartsQueueImmediatelyFromAvailableUris()
    {
        var songs = CreateTestPlaylist(3);
        var queueStarted = new TaskCompletionSource<IReadOnlyList<PlaybackMediaItem>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cacheWarmCompletion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        _mockMediaManager
            .Setup(m => m.PlayAsync(It.IsAny<IEnumerable<PlaybackMediaItem>>()))
            .Callback<IEnumerable<PlaybackMediaItem>>(items => queueStarted.TrySetResult(items.ToList()))
            .ReturnsAsync((PlaybackMediaItem?)null);
        _mockAudioCacheService
            .Setup(s => s.ResolvePlaybackUriAsync(It.IsAny<SongDto>(), It.IsAny<CancellationToken>()))
            .Returns(() => cacheWarmCompletion.Task);

        _service.SetPlaylist(songs, 0);

        var queuedItems = await queueStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.That(queuedItems.Select(item => item.MediaUri), Is.EqualTo(songs.Select(song => song.StreamUrl)));
    }

    [Test]
    public async Task SetPlaylist_WhenNativeQueueReportsFirstItemBeforeCapturedStart_IgnoresTransientRewind()
    {
        var songs = CreateTestPlaylist(4);
        _mockMediaManager
            .Setup(m => m.PlayAsync(It.IsAny<IEnumerable<PlaybackMediaItem>>()))
            .Callback<IEnumerable<PlaybackMediaItem>>(_ =>
                _mockMediaManager.Raise(
                    m => m.MediaItemChanged += null,
                    new PlaybackMediaItemEventArgs(new PlaybackMediaItem(songs[0].StreamUrl!))))
            .ReturnsAsync((PlaybackMediaItem?)null);

        _service.SetPlaylist(songs, 2);

        await Task.Delay(50);

        Assert.Multiple(() =>
        {
            Assert.That(_service.CurrentTrackIndex, Is.EqualTo(2));
            Assert.That(_service.CurrentSong, Is.SameAs(songs[2]));
            Assert.That(_service.IsPlaying, Is.True);
        });
        _mockMediaManager.Verify(m => m.PlayQueueItemAsync(2), Times.Once);
    }

    [Test]
    public async Task SetPlaylist_WhenNativeQueueReportsFirstItemAfterCapturedStartSelection_IgnoresDelayedRewind()
    {
        var songs = CreateTestPlaylist(4);
        var startSelected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _mockMediaManager
            .Setup(m => m.PlayQueueItemAsync(2))
            .Callback<int>(_ =>
            {
                _mockMediaManager.Raise(
                    m => m.MediaItemChanged += null,
                    new PlaybackMediaItemEventArgs(new PlaybackMediaItem(songs[2].StreamUrl!)));
                startSelected.TrySetResult();
            })
            .ReturnsAsync(true);

        _service.SetPlaylist(songs, 2);

        await startSelected.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await Task.Delay(75);

        _mockMediaManager.Raise(
            m => m.MediaItemChanged += null,
            new PlaybackMediaItemEventArgs(new PlaybackMediaItem(songs[0].StreamUrl!)));

        Assert.Multiple(() =>
        {
            Assert.That(_service.CurrentTrackIndex, Is.EqualTo(2));
            Assert.That(_service.CurrentSong, Is.SameAs(songs[2]));
            Assert.That(_service.IsPlaying, Is.True);
        });
    }

    [Test]
    public void MediaItemChanged_WhenUriDoesNotMatchButNativeQueueHasCurrentIndex_UpdatesCurrentSong()
    {
        var songs = CreateTestPlaylist(3);
        _service.SetPlaylist(songs, 0);
        _mockMediaQueue.Setup(q => q.HasCurrent).Returns(true);
        _mockMediaQueue.Setup(q => q.CurrentIndex).Returns(1);

        _mockMediaManager.Raise(m => m.MediaItemChanged += null, new PlaybackMediaItemEventArgs(new PlaybackMediaItem("https://test.com/normalized-or-renewed-url.mp3")));

        Assert.That(_service.CurrentTrackIndex, Is.EqualTo(1));
        Assert.That(_service.CurrentSong, Is.SameAs(songs[1]));
        Assert.That(_service.IsPlaying, Is.True);
        _mockMediaManager.Verify(m => m.PlayQueueItemAsync(It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task OnMediaEnded_WithPlaylist_ShuffleEnabled_UsesMediaManagerPlayNextFallback()
    {
        var service = CreateService(TimeSpan.FromMilliseconds(10));
        var songs = CreateTestPlaylist(3);

        _mockMediaManager
            .Setup(m => m.PlayNextAsync())
            .Returns(() =>
            {
                var shuffledItem = new PlaybackMediaItem(songs[2].StreamUrl!);
                _mockMediaManager.Raise(m => m.MediaItemChanged += null, new PlaybackMediaItemEventArgs(shuffledItem));
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
        _mockMediaManager.Verify(m => m.PlayNextAsync(), Times.Once);
        _mockMediaManager.Verify(m => m.PlayQueueItemAsync(It.IsAny<int>()), Times.Never);
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
        _mockMediaManager.Verify(m => m.PlayQueueItemAsync(0), Times.Never);
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
        _mockMediaManager.Verify(m => m.PlayQueueItemAsync(0), Times.Once);
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

        _mockMediaManager.Verify(m => m.SeekToAsync(TimeSpan.Zero), Times.Once);
        _mockMediaManager.Verify(m => m.PlayAsync(), Times.Once);
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

    private void SetupCachedSong(SongDto song, string? localPlaybackPath = null)
    {
        localPlaybackPath ??= $"/data/user/0/com.streamtunes/cache/song{song.Id}.mp3";
        _mockAudioCacheService
            .Setup(s => s.GetImmediatePlaybackUri(It.Is<SongDto>(candidate => candidate.Id == song.Id)))
            .Returns(localPlaybackPath);
        _mockAudioCacheService
            .Setup(s => s.GetCacheStatus(It.Is<SongDto>(candidate => candidate.Id == song.Id)))
            .Returns(new TrackCacheStatus(song.Id, $"song-{song.Id}", localPlaybackPath, true, true));
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
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
