using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.Tests.Services;

[TestFixture]
public class ImageCacheServiceTests
{
    private const string ImagePath = "/images-dev/covers/42.jpg";

    private string _cacheDirectory = string.Empty;
    private RecordingHttpMessageHandler _handler = null!;
    private Mock<IHttpClientFactory> _httpClientFactory = null!;
    private TestNetworkStatusService _networkStatus = null!;

    [SetUp]
    public void SetUp()
    {
        _cacheDirectory = Path.Combine(Path.GetTempPath(), "image-cache-tests", Guid.NewGuid().ToString("N"));
        _handler = new RecordingHttpMessageHandler();
        _networkStatus = new TestNetworkStatusService();

        _httpClientFactory = new Mock<IHttpClientFactory>();
        _httpClientFactory
            .Setup(f => f.CreateClient(AudioCacheService.AudioDownloadClientName))
            .Returns(() => new HttpClient(_handler, disposeHandler: false));
    }

    [TearDown]
    public void TearDown()
    {
        _handler.Dispose();
        if (Directory.Exists(_cacheDirectory))
        {
            Directory.Delete(_cacheDirectory, recursive: true);
        }
    }

    private ImageCacheService CreateService(IOfflineCacheSettingsService? settings = null) => new(
        _httpClientFactory.Object,
        NullLogger<ImageCacheService>.Instance,
        _cacheDirectory,
        settings,
        _networkStatus);

    private static string ImageUrl(string signature = "aaa", string path = ImagePath)
        => $"https://storage.blob.core.windows.net{path}?sv=2024-01-01&sig={signature}";

    private static byte[] ValidImageBytes(int size = 4096) => new byte[size];

    // --- Downloading ---

    [Test]
    public async Task EnsureCachedAsync_DownloadsAndReturnsALocalPath()
    {
        _handler.RespondWith(HttpStatusCode.OK, ValidImageBytes());

        var path = await CreateService().EnsureCachedAsync(ImageUrl());

        Assert.That(path, Is.Not.Null);
        Assert.That(File.Exists(path), Is.True);
    }

    [Test]
    public async Task EnsureCachedAsync_SecondCall_IsServedFromDiskWithoutARequest()
    {
        _handler.RespondWith(HttpStatusCode.OK, ValidImageBytes());
        var service = CreateService();
        await service.EnsureCachedAsync(ImageUrl());

        await service.EnsureCachedAsync(ImageUrl());

        Assert.That(_handler.RequestCount, Is.EqualTo(1));
    }

    [Test]
    public async Task EnsureCachedAsync_RotatedSasToken_HitsTheSameCachedFile()
    {
        // The server mints a fresh SAS query string on every API call. If the cache key included it,
        // artwork would re-download on every single load and never survive a restart.
        _handler.RespondWith(HttpStatusCode.OK, ValidImageBytes());
        var service = CreateService();
        var first = await service.EnsureCachedAsync(ImageUrl("first-token"));

        var second = await service.EnsureCachedAsync(ImageUrl("completely-different-token"));

        Assert.That(second, Is.EqualTo(first));
        Assert.That(_handler.RequestCount, Is.EqualTo(1));
    }

    [Test]
    public async Task EnsureCachedAsync_DifferentImages_AreCachedSeparately()
    {
        _handler.RespondWith(HttpStatusCode.OK, ValidImageBytes());
        var service = CreateService();

        var first = await service.EnsureCachedAsync(ImageUrl(path: "/images/1.jpg"));
        var second = await service.EnsureCachedAsync(ImageUrl(path: "/images/2.jpg"));

        Assert.That(second, Is.Not.EqualTo(first));
    }

    // --- Rejection and failure ---

    [Test]
    public async Task EnsureCachedAsync_UndersizedPayload_IsRejected()
    {
        // An expired-SAS "AuthenticationFailed" XML body is a few hundred bytes. Caching one would pin
        // a broken image forever.
        _handler.RespondWith(HttpStatusCode.OK, new byte[100]);

        var path = await CreateService().EnsureCachedAsync(ImageUrl());

        Assert.That(path, Is.Null);
        Assert.That(Directory.Exists(_cacheDirectory) && Directory.EnumerateFiles(_cacheDirectory).Any(), Is.False);
    }

    [Test]
    public async Task EnsureCachedAsync_NonSuccessResponse_ReturnsNullAndLeavesNoTempFile()
    {
        _handler.RespondWith(HttpStatusCode.Forbidden, []);

        var path = await CreateService().EnsureCachedAsync(ImageUrl());

        Assert.That(path, Is.Null);
        Assert.That(EnumerateCacheFiles(), Is.Empty);
    }

    [Test]
    public async Task EnsureCachedAsync_TransportFailure_ReturnsNullRatherThanThrowing()
    {
        _handler.ThrowOnSend(new HttpRequestException("Unable to resolve host"));

        Assert.That(await CreateService().EnsureCachedAsync(ImageUrl()), Is.Null);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("not-an-absolute-url")]
    public async Task EnsureCachedAsync_InvalidUrl_ReturnsNullWithoutARequest(string? url)
    {
        Assert.That(await CreateService().EnsureCachedAsync(url), Is.Null);
        Assert.That(_handler.RequestCount, Is.Zero);
    }

    // --- Offline ---

    [Test]
    public async Task EnsureCachedAsync_WhileOffline_MakesNoRequest()
    {
        _networkStatus.SetOffline(true);

        var path = await CreateService().EnsureCachedAsync(ImageUrl());

        Assert.That(path, Is.Null);
        Assert.That(_handler.RequestCount, Is.Zero);
    }

    [Test]
    public async Task EnsureCachedAsync_WhileOffline_StillReturnsAnAlreadyCachedImage()
    {
        _handler.RespondWith(HttpStatusCode.OK, ValidImageBytes());
        var service = CreateService();
        var cachedPath = await service.EnsureCachedAsync(ImageUrl());
        _networkStatus.SetOffline(true);

        Assert.That(await service.EnsureCachedAsync(ImageUrl("rotated")), Is.EqualTo(cachedPath));
    }

    // --- TryGetCachedImagePath ---

    [Test]
    public async Task TryGetCachedImagePath_ReturnsThePathOnlyOnceCached()
    {
        _handler.RespondWith(HttpStatusCode.OK, ValidImageBytes());
        var service = CreateService();
        Assert.That(service.TryGetCachedImagePath(ImageUrl()), Is.Null);

        var cachedPath = await service.EnsureCachedAsync(ImageUrl());

        Assert.That(service.TryGetCachedImagePath(ImageUrl("rotated")), Is.EqualTo(cachedPath));
    }

    [Test]
    public void TryGetCachedImagePath_NeverIssuesARequest()
    {
        CreateService().TryGetCachedImagePath(ImageUrl());

        Assert.That(_handler.RequestCount, Is.Zero);
    }

    [Test]
    public async Task TryGetCachedImagePath_DiscardsAnUndersizedFileOnDisk()
    {
        // Self-heals a truncated write left by an earlier crash rather than serving a broken image.
        _handler.RespondWith(HttpStatusCode.OK, ValidImageBytes());
        var service = CreateService();
        var cachedPath = await service.EnsureCachedAsync(ImageUrl())!;
        await File.WriteAllBytesAsync(cachedPath!, new byte[10]);

        Assert.That(service.TryGetCachedImagePath(ImageUrl()), Is.Null);
        Assert.That(File.Exists(cachedPath), Is.False);
    }

    // --- Budget ---

    [Test]
    public async Task EnsureCachedAsync_OverBudget_SkipsTheDownload()
    {
        var settings = new Mock<IOfflineCacheSettingsService>();
        settings.Setup(s => s.GetImageCacheLimitBytes()).Returns(1);
        settings.Setup(s => s.GetDeviceFreeSpaceReserveBytes()).Returns(0);
        _handler.RespondWith(HttpStatusCode.OK, ValidImageBytes());
        // Put something in the directory so measured usage already exceeds the 1-byte limit.
        Directory.CreateDirectory(_cacheDirectory);
        await File.WriteAllBytesAsync(Path.Combine(_cacheDirectory, "existing.img"), new byte[2048]);

        Assert.That(await CreateService(settings.Object).EnsureCachedAsync(ImageUrl()), Is.Null);
        Assert.That(_handler.RequestCount, Is.Zero);
    }

    [Test]
    public async Task GetCacheUsageBytesAsync_ReportsTheOnDiskTotal()
    {
        _handler.RespondWith(HttpStatusCode.OK, ValidImageBytes(4096));
        var service = CreateService();
        await service.EnsureCachedAsync(ImageUrl());

        Assert.That(await service.GetCacheUsageBytesAsync(), Is.EqualTo(4096));
    }

    [Test]
    public async Task EnsureCachedAsync_WithRealBudgetSettings_StillDownloads()
    {
        // Every other download test passes null settings, which short-circuits CanQueueDownload - so
        // none of them exercised the free-space probe. That gap is how a probe measuring the wrong
        // filesystem (and so reporting a full disk on every Android device) reached a phone.
        var settings = new Mock<IOfflineCacheSettingsService>();
        settings.Setup(s => s.GetImageCacheLimitBytes()).Returns(64L * 1024 * 1024);
        settings.Setup(s => s.GetDeviceFreeSpaceReserveBytes()).Returns(1024L * 1024 * 1024);
        _handler.RespondWith(HttpStatusCode.OK, ValidImageBytes());

        var path = await CreateService(settings.Object).EnsureCachedAsync(ImageUrl());

        Assert.That(path, Is.Not.Null, "a normal download must not be rejected by the storage guards");
        Assert.That(File.Exists(path!), Is.True);
    }

    // --- Pruning ---

    [Test]
    public async Task PruneAsync_DeletesOnlyImagesNotInTheRetainedSet()
    {
        _handler.RespondWith(HttpStatusCode.OK, ValidImageBytes());
        var service = CreateService();
        var keptUrl = ImageUrl(path: "/images/keep.jpg");
        var droppedUrl = ImageUrl(path: "/images/drop.jpg");
        var keptPath = await service.EnsureCachedAsync(keptUrl);
        var droppedPath = await service.EnsureCachedAsync(droppedUrl);

        await service.PruneAsync([keptUrl]);

        Assert.That(File.Exists(keptPath!), Is.True);
        Assert.That(File.Exists(droppedPath!), Is.False);
    }

    [Test]
    public async Task PruneAsync_MatchesRetainedUrlsByPathNotBySasToken()
    {
        _handler.RespondWith(HttpStatusCode.OK, ValidImageBytes());
        var service = CreateService();
        var cachedPath = await service.EnsureCachedAsync(ImageUrl("token-at-download-time"));

        await service.PruneAsync([ImageUrl("a-completely-different-token")]);

        Assert.That(File.Exists(cachedPath!), Is.True);
    }

    [Test]
    public async Task PruneAsync_WithNoCacheDirectory_DoesNotThrow()
    {
        Assert.That(async () => await CreateService().PruneAsync([]), Throws.Nothing);
        await Task.CompletedTask;
    }

    [Test]
    public async Task PruneAsync_LeavesInFlightTemporaryDownloadsAlone()
    {
        // A prune runs after every live library load, which is exactly when downloads are in flight.
        // Deleting the partial file would fail that download - and because the backfill only retries a
        // limited number of times, the cover could stay missing.
        Directory.CreateDirectory(_cacheDirectory);
        var inFlightPath = Path.Combine(_cacheDirectory, "abc123.jpg.tmp");
        await File.WriteAllBytesAsync(inFlightPath, ValidImageBytes());

        await CreateService().PruneAsync([]);

        Assert.That(File.Exists(inFlightPath), Is.True);
    }

    // --- Startup sweep ---

    [Test]
    public async Task Construction_SweepsAwayStaleTemporaryFiles()
    {
        // Orphans from a process kill mid-download are invisible to the cache but still count against
        // the budget, so they would silently block new downloads.
        Directory.CreateDirectory(_cacheDirectory);
        var stalePath = Path.Combine(_cacheDirectory, "orphan.jpg.tmp");
        await File.WriteAllBytesAsync(stalePath, ValidImageBytes());
        File.SetLastWriteTimeUtc(stalePath, DateTime.UtcNow.AddHours(-4));

        await CreateService().StaleTemporaryFileSweep;

        Assert.That(File.Exists(stalePath), Is.False);
    }

    [Test]
    public async Task Construction_LeavesRecentTemporaryFilesAlone()
    {
        // The sweep runs on a background thread now, so it can overlap a download that started moments
        // earlier. The age check is what keeps that safe.
        Directory.CreateDirectory(_cacheDirectory);
        var recentPath = Path.Combine(_cacheDirectory, "in-flight.jpg.tmp");
        await File.WriteAllBytesAsync(recentPath, ValidImageBytes());

        await CreateService().StaleTemporaryFileSweep;

        Assert.That(File.Exists(recentPath), Is.True);
    }

    private IEnumerable<string> EnumerateCacheFiles()
        => Directory.Exists(_cacheDirectory) ? Directory.EnumerateFiles(_cacheDirectory) : [];
}

/// <summary>
/// Minimal stub handler: counts requests and replays a canned response or exception.
/// </summary>
internal sealed class RecordingHttpMessageHandler : HttpMessageHandler
{
    private HttpStatusCode _statusCode = HttpStatusCode.OK;
    private byte[] _content = [];
    private Exception? _exception;

    public int RequestCount { get; private set; }

    public void RespondWith(HttpStatusCode statusCode, byte[] content)
    {
        _statusCode = statusCode;
        _content = content;
        _exception = null;
    }

    public void ThrowOnSend(Exception exception) => _exception = exception;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        RequestCount++;

        if (_exception != null)
        {
            return Task.FromException<HttpResponseMessage>(_exception);
        }

        return Task.FromResult(new HttpResponseMessage(_statusCode)
        {
            Content = new ByteArrayContent(_content)
        });
    }
}
