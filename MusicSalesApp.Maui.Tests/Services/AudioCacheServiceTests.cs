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
        var factory = new Mock<IHttpClientFactory>();
        var handler = CreateHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([1, 2, 3, 4])
        });
        factory.Setup(f => f.CreateClient(AudioCacheService.AudioDownloadClientName))
            .Returns(CreateHttpClient(handler.Object));

        var service = new AudioCacheService(factory.Object, NullLogger<AudioCacheService>.Instance, _cacheDirectory);
        var song = new SongDto { Id = 19, StreamUrl = "https://example.com/audio/test-song.mp3?sig=123" };

        var playbackUri = await service.ResolvePlaybackUriAsync(song);

        Assert.That(playbackUri, Does.StartWith(_cacheDirectory));
        Assert.That(File.Exists(playbackUri), Is.True);
        Assert.That(new FileInfo(playbackUri).Length, Is.EqualTo(4));
    }

    [Test]
    public async Task ResolvePlaybackUriAsync_WhenCachedFileExists_ReturnsLocalPathWithoutDownloadingAgain()
    {
        var factory = new Mock<IHttpClientFactory>();
        var handler = CreateHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([9, 8, 7])
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
    public async Task GetImmediatePlaybackUri_WhenCachedFileExists_ReturnsLocalPathWithoutDownloading()
    {
        var factory = new Mock<IHttpClientFactory>();
        var handler = CreateHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([4, 3, 2, 1])
        });
        factory.Setup(f => f.CreateClient(AudioCacheService.AudioDownloadClientName))
            .Returns(CreateHttpClient(handler.Object));

        var service = new AudioCacheService(factory.Object, NullLogger<AudioCacheService>.Instance, _cacheDirectory);
        var song = new SongDto { Id = 22, StreamUrl = "https://example.com/audio/cached-song.mp3?sig=xyz" };

        var cachedPlaybackUri = await service.ResolvePlaybackUriAsync(song);
        var immediatePlaybackUri = service.GetImmediatePlaybackUri(song);

        Assert.That(immediatePlaybackUri, Is.EqualTo(cachedPlaybackUri));
        handler.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [Test]
    public void GetImmediatePlaybackUri_WhenCacheMiss_ReturnsRemoteStreamUrlWithoutDownloading()
    {
        var factory = new Mock<IHttpClientFactory>(MockBehavior.Strict);
        var service = new AudioCacheService(factory.Object, NullLogger<AudioCacheService>.Instance, _cacheDirectory);
        var song = new SongDto { Id = 23, StreamUrl = "https://example.com/audio/uncached-song.mp3" };

        var immediatePlaybackUri = service.GetImmediatePlaybackUri(song);

        Assert.That(immediatePlaybackUri, Is.EqualTo(song.StreamUrl));
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