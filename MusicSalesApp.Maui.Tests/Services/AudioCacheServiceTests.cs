using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Tests.Services;

[TestFixture]
public class AudioCacheServiceTests
{
    private string _cacheDirectory = null!;

    [SetUp]
    public void SetUp()
    {
        _cacheDirectory = Path.Combine(Path.GetTempPath(), "streamtunes-audio-cache-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_cacheDirectory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_cacheDirectory))
        {
            Directory.Delete(_cacheDirectory, true);
        }
    }

    [Test]
    public async Task ResolvePlaybackUriAsync_DownloadsTrackAndReturnsLocalPath()
    {
        var payload = CreateAudioPayload(8192);
        var factory = new Mock<IHttpClientFactory>();
        var handler = CreateHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(payload)
        });
        factory.Setup(f => f.CreateClient(AudioCacheService.AudioDownloadClientName))
            .Returns(CreateHttpClient(handler.Object));

        var service = new AudioCacheService(factory.Object, NullLogger<AudioCacheService>.Instance, _cacheDirectory);
        var song = new SongDto { Id = 19, StreamUrl = "https://example.com/audio/test-song.mp3?sig=123" };

        var playbackUri = await service.ResolvePlaybackUriAsync(song);

        Assert.That(playbackUri, Does.StartWith(_cacheDirectory));
        Assert.That(File.Exists(playbackUri), Is.True);
        Assert.That(new FileInfo(playbackUri).Length, Is.EqualTo(payload.Length));
    }

    [Test]
    public async Task ResolvePlaybackUriAsync_WhenCachedFileExists_ReturnsLocalPathWithoutDownloadingAgain()
    {
        var factory = new Mock<IHttpClientFactory>();
        var handler = CreateHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(CreateAudioPayload(8192))
        });
        factory.Setup(f => f.CreateClient(AudioCacheService.AudioDownloadClientName))
            .Returns(CreateHttpClient(handler.Object));

        var service = new AudioCacheService(factory.Object, NullLogger<AudioCacheService>.Instance, _cacheDirectory);
        var song = new SongDto { Id = 20, StreamUrl = "https://example.com/audio/another-song.mp3?sig=abc" };

        var firstPlaybackUri = await service.ResolvePlaybackUriAsync(song);
        var secondPlaybackUri = await service.ResolvePlaybackUriAsync(song);

        Assert.That(secondPlaybackUri, Is.EqualTo(firstPlaybackUri));
        handler.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [Test]
    public async Task SignedUrlChanges_ButStableCacheKeyStillFindsDownloadedTrack()
    {
        var factory = new Mock<IHttpClientFactory>();
        var handler = CreateHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(CreateAudioPayload(8192))
        });
        factory.Setup(f => f.CreateClient(AudioCacheService.AudioDownloadClientName))
            .Returns(CreateHttpClient(handler.Object));

        var service = new AudioCacheService(factory.Object, NullLogger<AudioCacheService>.Instance, _cacheDirectory);
        var firstLease = new SongDto { Id = 30, StreamUrl = "https://example.com/audio/stable-song.mp3?sig=old" };
        var renewedLease = new SongDto { Id = 30, StreamUrl = "https://example.com/audio/stable-song.mp3?sig=new" };

        var firstPlaybackUri = await service.ResolvePlaybackUriAsync(firstLease);
        var renewedStatus = await service.GetCacheStatusAsync(renewedLease);
        var renewedPlaybackUri = renewedStatus.LocalPlaybackUri;

        Assert.Multiple(() =>
        {
            Assert.That(renewedPlaybackUri, Is.EqualTo(firstPlaybackUri));
            Assert.That(service.GetStableCacheKey(renewedLease), Is.EqualTo(service.GetStableCacheKey(firstLease)));
            Assert.That(renewedStatus.IsLocalReady, Is.True);
        });
        handler.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [Test]
    public async Task GetCacheUsageBytesAsync_SumsSizeOfDownloadedFiles()
    {
        var payload = CreateAudioPayload(8192);
        var factory = new Mock<IHttpClientFactory>();
        var handler = CreateHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(payload)
        });
        factory.Setup(f => f.CreateClient(AudioCacheService.AudioDownloadClientName))
            .Returns(CreateHttpClient(handler.Object));

        var service = new AudioCacheService(factory.Object, NullLogger<AudioCacheService>.Instance, _cacheDirectory);
        var song = new SongDto { Id = 40, StreamUrl = "https://example.com/audio/usage-song.mp3" };
        await service.ResolvePlaybackUriAsync(song);

        var usageBytes = await service.GetCacheUsageBytesAsync();

        Assert.That(usageBytes, Is.EqualTo(payload.Length));
    }

    [Test]
    public async Task ResolvePlaybackUriAsync_WhenDownloadFails_FallsBackToRemoteStreamUrl()
    {
        var factory = new Mock<IHttpClientFactory>();
        var handler = CreateHandler(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        factory.Setup(f => f.CreateClient(AudioCacheService.AudioDownloadClientName))
            .Returns(CreateHttpClient(handler.Object));

        var service = new AudioCacheService(factory.Object, NullLogger<AudioCacheService>.Instance, _cacheDirectory);
        var song = new SongDto { Id = 21, StreamUrl = "https://example.com/audio/failure-song.mp3" };

        var playbackUri = await service.ResolvePlaybackUriAsync(song);

        Assert.That(playbackUri, Is.EqualTo(song.StreamUrl));
    }

    [Test]
    public async Task ResolvePlaybackUriAsync_WhenPayloadTooSmallToBePlayable_FallsBackToRemoteAndCachesNothing()
    {
        // Regression: a junk blob (170 bytes on-device) was cached as a completed song and
        // poisoned playback. Undersized payloads must never be committed to the cache.
        var factory = new Mock<IHttpClientFactory>();
        var handler = CreateHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(CreateAudioPayload(170))
        });
        factory.Setup(f => f.CreateClient(AudioCacheService.AudioDownloadClientName))
            .Returns(CreateHttpClient(handler.Object));

        var service = new AudioCacheService(factory.Object, NullLogger<AudioCacheService>.Instance, _cacheDirectory);
        var song = new SongDto { Id = 26, StreamUrl = "https://example.com/audio/junk-song.mp3" };

        var playbackUri = await service.ResolvePlaybackUriAsync(song);

        Assert.Multiple(() =>
        {
            Assert.That(playbackUri, Is.EqualTo(song.StreamUrl));
            Assert.That(Directory.EnumerateFiles(_cacheDirectory), Is.Empty);
        });
    }

    [Test]
    public async Task GetCacheStatusAsync_WhenCachedFileIsUndersized_PurgesItAndReportsNotReady()
    {
        var factory = new Mock<IHttpClientFactory>();
        var handler = CreateHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(CreateAudioPayload(8192))
        });
        factory.Setup(f => f.CreateClient(AudioCacheService.AudioDownloadClientName))
            .Returns(CreateHttpClient(handler.Object));

        var service = new AudioCacheService(factory.Object, NullLogger<AudioCacheService>.Instance, _cacheDirectory);
        var song = new SongDto { Id = 27, StreamUrl = "https://example.com/audio/poisoned-song.mp3" };
        var cachedPlaybackUri = await service.ResolvePlaybackUriAsync(song);

        // Simulate a poisoned cache entry left behind by an earlier junk download.
        File.WriteAllBytes(cachedPlaybackUri, CreateAudioPayload(170));

        var status = await service.GetCacheStatusAsync(song);

        Assert.Multiple(() =>
        {
            Assert.That(status.IsLocalReady, Is.False);
            Assert.That(File.Exists(cachedPlaybackUri), Is.False);
        });
    }

    [Test]
    public async Task ResolvePlaybackUriAsync_WhenContentWouldExceedConfiguredCacheLimit_FallsBackToRemoteStreamUrl()
    {
        var factory = new Mock<IHttpClientFactory>();
        var handler = CreateHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(CreateAudioPayload(8192))
        });
        var settings = new Mock<IOfflineCacheSettingsService>();
        // The audio share of the configured limit, not the whole thing - cached artwork is spent out of
        // the same budget.
        settings.Setup(s => s.GetAudioCacheLimitBytes()).Returns(8000);
        settings.Setup(s => s.GetDeviceFreeSpaceReserveBytes()).Returns(0);
        factory.Setup(f => f.CreateClient(AudioCacheService.AudioDownloadClientName))
            .Returns(CreateHttpClient(handler.Object));

        var service = new AudioCacheService(
            factory.Object,
            NullLogger<AudioCacheService>.Instance,
            _cacheDirectory,
            settings.Object);
        var song = new SongDto { Id = 24, StreamUrl = "https://example.com/audio/too-large-song.mp3" };

        var playbackUri = await service.ResolvePlaybackUriAsync(song);

        Assert.Multiple(() =>
        {
            Assert.That(playbackUri, Is.EqualTo(song.StreamUrl));
            Assert.That(Directory.EnumerateFiles(_cacheDirectory), Is.Empty);
        });
    }

    [Test]
    public async Task ResolvePlaybackUriAsync_WhenContentFitsTheAudioBudget_StillCaches()
    {
        // The companion to the test above: it would keep passing if the audio budget ever collapsed to
        // zero, which is exactly what carving the artwork share out of the limit risks getting wrong.
        var factory = new Mock<IHttpClientFactory>();
        var handler = CreateHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(CreateAudioPayload(8192))
        });
        var settings = new Mock<IOfflineCacheSettingsService>();
        settings.Setup(s => s.GetAudioCacheLimitBytes()).Returns(1024L * 1024);
        settings.Setup(s => s.GetDeviceFreeSpaceReserveBytes()).Returns(0);
        factory.Setup(f => f.CreateClient(AudioCacheService.AudioDownloadClientName))
            .Returns(CreateHttpClient(handler.Object));

        var service = new AudioCacheService(
            factory.Object,
            NullLogger<AudioCacheService>.Instance,
            _cacheDirectory,
            settings.Object);
        var song = new SongDto { Id = 25, StreamUrl = "https://example.com/audio/fits.mp3" };

        var playbackUri = await service.ResolvePlaybackUriAsync(song);

        Assert.That(playbackUri, Is.Not.EqualTo(song.StreamUrl));
        Assert.That(File.Exists(playbackUri), Is.True);
    }

    [Test]
    public async Task GetCacheStatusAsync_WhenCachedFileExists_ReturnsLocalPathWithoutDownloading()
    {
        var factory = new Mock<IHttpClientFactory>();
        var handler = CreateHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(CreateAudioPayload(8192))
        });
        factory.Setup(f => f.CreateClient(AudioCacheService.AudioDownloadClientName))
            .Returns(CreateHttpClient(handler.Object));

        var service = new AudioCacheService(factory.Object, NullLogger<AudioCacheService>.Instance, _cacheDirectory);
        var song = new SongDto { Id = 22, StreamUrl = "https://example.com/audio/cached-song.mp3?sig=xyz" };

        var cachedPlaybackUri = await service.ResolvePlaybackUriAsync(song);
        var immediatePlaybackUri = (await service.GetCacheStatusAsync(song)).LocalPlaybackUri;

        Assert.That(immediatePlaybackUri, Is.EqualTo(cachedPlaybackUri));
        handler.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [Test]
    public async Task GetCacheStatusAsync_WhenCacheMiss_ReturnsNotReadyWithoutDownloading()
    {
        var factory = new Mock<IHttpClientFactory>(MockBehavior.Strict);
        var service = new AudioCacheService(factory.Object, NullLogger<AudioCacheService>.Instance, _cacheDirectory);
        var song = new SongDto { Id = 23, StreamUrl = "https://example.com/audio/uncached-song.mp3" };

        var status = await service.GetCacheStatusAsync(song);

        Assert.Multiple(() =>
        {
            Assert.That(status.IsLocalReady, Is.False);
            Assert.That(status.LocalPlaybackUri, Is.Null);
        });
    }

    private static byte[] CreateAudioPayload(int length)
    {
        var payload = new byte[length];
        for (var index = 0; index < payload.Length; index++)
        {
            payload[index] = (byte)(index % 251);
        }

        return payload;
    }

    private static HttpClient CreateHttpClient(HttpMessageHandler handler)
    {
        return new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.com/")
        };
    }

    private static Mock<HttpMessageHandler> CreateHandler(HttpResponseMessage response)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);
        return handler;
    }
}
