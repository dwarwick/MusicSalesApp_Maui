using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Maui.Networking;
using Moq;
using Moq.Protected;
using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.ViewModels;
using Microsoft.Extensions.Logging;

namespace MusicSalesApp.Maui.Tests.Services;

[TestFixture]
public class MusicServiceTests
{
    private const string PendingStreamRecordsPreferenceKey = "pending_stream_records_v1";
    private Mock<IHttpClientFactory> _mockFactory;
    private Mock<IAppSettingsService> _mockAppSettingsService;
    private Mock<ILogger<MusicService>> _mockLogger;
    private InMemoryPreferenceStore _preferenceStore;
    private TestConnectivity _connectivity;

    [SetUp]
    public void Setup()
    {
        _mockFactory = new Mock<IHttpClientFactory>();
        _mockAppSettingsService = new Mock<IAppSettingsService>();
        _mockLogger = new Mock<ILogger<MusicService>>();
        _preferenceStore = new InMemoryPreferenceStore();
        _connectivity = new TestConnectivity();
    }

    private HttpClient CreateMockHttpClient(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://test.example.com/") };
        _mockFactory.Setup(f => f.CreateClient("MusicSalesApi")).Returns(client);
        return client;
    }

    private Mock<HttpMessageHandler> CreateHandlerWithResponse(HttpStatusCode statusCode, object? content = null)
    {
        var handler = new Mock<HttpMessageHandler>();
        var response = new HttpResponseMessage(statusCode);
        if (content != null)
        {
            response.Content = JsonContent.Create(content);
        }
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);
        return handler;
    }

    private MusicService CreateService(TimeSpan? pendingStreamRetryInterval = null) => new(
        _mockFactory.Object,
        _mockAppSettingsService.Object,
        _preferenceStore,
        _connectivity,
        _mockLogger.Object,
        pendingStreamRetryInterval);

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var timeoutAt = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < timeoutAt)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.Fail("Timed out waiting for the expected async test condition.");
    }

    [Test]
    public async Task GetSongsAsync_ReturnsSongsFromApi()
    {
        // Arrange
        var expectedSongs = new List<SongDto>
        {
            new() { Id = 1, SongTitle = "Test Song", ArtistName = "Artist", Genre = "Rock", StreamCount = 42, CreatorId = 77, CreatorUserId = 88 },
            new() { Id = 2, SongTitle = "Another", ArtistName = "Other", Genre = "Pop", StreamCount = 10 }
        };
        var handler = CreateHandlerWithResponse(HttpStatusCode.OK, expectedSongs);
        CreateMockHttpClient(handler.Object);
        var service = CreateService();

        // Act
        var result = await service.GetSongsAsync();

        // Assert
        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result[0].SongTitle, Is.EqualTo("Test Song"));
        Assert.That(result[0].CreatorId, Is.EqualTo(77));
        Assert.That(result[0].CreatorUserId, Is.EqualTo(88));
        Assert.That(result[1].SongTitle, Is.EqualTo("Another"));
        Assert.That(service.LastSongsError, Is.Null);
    }

    [Test]
    public async Task GetSongByTitleAsync_ReturnsSongWithCreatorIdentifiers()
    {
        // Arrange
        var expectedSong = new SongDto
        {
            Id = 5,
            SongTitle = "Deep Link Song",
            ArtistName = "Artist",
            CreatorId = 55,
            CreatorUserId = 99
        };
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(r =>
                    r.Method == HttpMethod.Get &&
                    r.RequestUri!.PathAndQuery.EndsWith("api/music/song-by-title/Deep%20Link%20Song")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(expectedSong)
            });
        CreateMockHttpClient(handler.Object);
        var service = CreateService();

        // Act
        var result = await service.GetSongByTitleAsync("Deep Link Song");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.CreatorId, Is.EqualTo(55));
        Assert.That(result.CreatorUserId, Is.EqualTo(99));
    }

    [Test]
    public async Task GetSongsAsync_ReturnsEmptyListOnError_AndCapturesLastSongsError()
    {
        // Arrange
        var handler = CreateHandlerWithResponse(HttpStatusCode.InternalServerError);
        CreateMockHttpClient(handler.Object);
        var service = CreateService();

        // Act
        var result = await service.GetSongsAsync();

        // Assert
        Assert.That(result, Is.Empty);
        Assert.That(service.LastSongsError, Does.Contain("500"));
        Assert.That(service.LastSongsError, Does.Contain("api/music/songs"));
    }

    [Test]
    public async Task GetSongsAsync_ReturnsEmptyListOnInvalidJson_AndCapturesLastSongsError()
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("not-json", System.Text.Encoding.UTF8, "application/json")
            });
        CreateMockHttpClient(handler.Object);
        var service = CreateService();

        var result = await service.GetSongsAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Empty);
            Assert.That(service.LastSongsError, Does.Contain("Unable to load data from"));
            Assert.That(service.LastSongsError, Does.Contain("api/music/songs"));
        });
    }

    [Test]
    public async Task RecordStreamAsync_CallsCorrectEndpoint()
    {
        // Arrange
        var handler = new Mock<HttpMessageHandler>();
        var recordedCounts = new List<(int songId, int newCount)>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(r =>
                    r.Method == HttpMethod.Post &&
                    r.RequestUri!.PathAndQuery.Contains("api/music/stream/42")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { songMetadataId = 42, streamCount = 99, countIncremented = true })
            });
        CreateMockHttpClient(handler.Object);
        var service = CreateService();
        service.OnStreamCountRecorded += (songId, newCount) => recordedCounts.Add((songId, newCount));

        // Act
        var streamCount = await service.RecordStreamAsync(42);

        // Assert
        Assert.That(streamCount, Is.EqualTo(99));
        Assert.That(recordedCounts, Is.EqualTo(new[] { (42, 99) }));

        handler.Protected().Verify("SendAsync", Times.Once(),
            ItExpr.Is<HttpRequestMessage>(r =>
                r.Method == HttpMethod.Post &&
                r.RequestUri!.PathAndQuery.Contains("api/music/stream/42")),
            ItExpr.IsAny<CancellationToken>());
    }

    [Test]
    public async Task RecordStreamAsync_WhenServerSuppressesDuplicate_ReturnsCountWithoutRaisingEvent()
    {
        var handler = new Mock<HttpMessageHandler>();
        var recordedCounts = new List<(int songId, int newCount)>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(r =>
                    r.Method == HttpMethod.Post &&
                    r.RequestUri!.PathAndQuery.Contains("api/music/stream/42")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { songMetadataId = 42, streamCount = 99, countIncremented = false })
            });

        CreateMockHttpClient(handler.Object);
        var service = CreateService();
        service.OnStreamCountRecorded += (songId, newCount) => recordedCounts.Add((songId, newCount));

        var streamCount = await service.RecordStreamAsync(42);

        Assert.Multiple(() =>
        {
            Assert.That(streamCount, Is.EqualTo(99));
            Assert.That(recordedCounts, Is.Empty);
        });
    }

    [Test]
    public async Task RecordStreamAsync_DoesNotThrowOnError()
    {
        // Arrange
        var handler = CreateHandlerWithResponse(HttpStatusCode.InternalServerError);
        CreateMockHttpClient(handler.Object);
        var service = CreateService();

        // Act & Assert — should not throw
        Assert.DoesNotThrowAsync(() => service.RecordStreamAsync(1));
    }

    [Test]
    public async Task RecordStreamAsync_WhenInternetIsUnavailable_QueuesPendingStreamAfterRequestFailure()
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection failure"));
        CreateMockHttpClient(handler.Object);
        _connectivity.NetworkAccess = NetworkAccess.None;
        var service = CreateService();

        var streamCount = await service.RecordStreamAsync(42);

        Assert.Multiple(() =>
        {
            Assert.That(streamCount, Is.Null);
            Assert.That(_preferenceStore.GetString(PendingStreamRecordsPreferenceKey), Does.Contain("42"));
        });

        handler.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [Test]
    public async Task RecordStreamAsync_WhenDnsLookupFails_QueuesPendingStream()
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException(
                "Connection failure",
                new InvalidOperationException("Unable to resolve host \"davidtest.dev\"")));
        CreateMockHttpClient(handler.Object);
        var service = CreateService();

        var streamCount = await service.RecordStreamAsync(42);

        Assert.Multiple(() =>
        {
            Assert.That(streamCount, Is.Null);
            Assert.That(_preferenceStore.GetString(PendingStreamRecordsPreferenceKey), Does.Contain("42"));
        });
    }

    [Test]
    public async Task FlushPendingStreamRecordsAsync_ReplaysQueuedStreamsAndClearsQueue()
    {
        var handler = new Mock<HttpMessageHandler>();
        var recordedCounts = new List<(int songId, int newCount)>();
        var requestCount = 0;
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(request =>
                    request.Method == HttpMethod.Post
                    && request.RequestUri != null
                    && request.RequestUri.PathAndQuery.Contains("api/music/stream/42")),
                ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>((_, _) =>
            {
                requestCount++;
                if (requestCount == 1)
                {
                    return Task.FromException<HttpResponseMessage>(new HttpRequestException("Connection failure"));
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { songMetadataId = 42, streamCount = 99, countIncremented = true })
                });
            });
        CreateMockHttpClient(handler.Object);
        var service = CreateService();
        service.OnStreamCountRecorded += (songId, newCount) => recordedCounts.Add((songId, newCount));

        _connectivity.NetworkAccess = NetworkAccess.None;
        await service.RecordStreamAsync(42);

        await service.FlushPendingStreamRecordsAsync();

        Assert.That(_preferenceStore.GetString(PendingStreamRecordsPreferenceKey), Is.Null);
        Assert.That(recordedCounts, Is.EqualTo(new[] { (42, 99) }));
        handler.Protected().Verify(
            "SendAsync",
            Times.Exactly(2),
            ItExpr.Is<HttpRequestMessage>(request =>
                request.Method == HttpMethod.Post
                && request.RequestUri != null
                && request.RequestUri.PathAndQuery.Contains("api/music/stream/42")),
            ItExpr.IsAny<CancellationToken>());
    }

    [Test]
    public async Task PendingStreamRetryLoop_RetriesQueuedStreamsWithoutLifecycleTriggers()
    {
        var recordedCounts = new List<(int songId, int newCount)>();
        var secondAttemptObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requestCount = 0;

        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>((_, _) =>
            {
                requestCount++;
                if (requestCount == 1)
                {
                    return Task.FromException<HttpResponseMessage>(new HttpRequestException("Connection failure"));
                }

                secondAttemptObserved.TrySetResult();
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { songMetadataId = 42, streamCount = 99, countIncremented = true })
                });
            });

        CreateMockHttpClient(handler.Object);
        _connectivity.NetworkAccess = NetworkAccess.None;
        var service = CreateService(TimeSpan.FromMilliseconds(25));
        service.OnStreamCountRecorded += (songId, newCount) => recordedCounts.Add((songId, newCount));

        await service.RecordStreamAsync(42);

        _connectivity.NetworkAccess = NetworkAccess.Internet;
        await WaitForAsync(() =>
            secondAttemptObserved.Task.IsCompleted
            && _preferenceStore.GetString(PendingStreamRecordsPreferenceKey) is null);

        Assert.Multiple(() =>
        {
            Assert.That(secondAttemptObserved.Task.IsCompleted, Is.True);
            Assert.That(_preferenceStore.GetString(PendingStreamRecordsPreferenceKey), Is.Null);
            Assert.That(recordedCounts, Is.EqualTo(new[] { (42, 99) }));
        });
    }

    [Test]
    public async Task RecordStreamAsync_WhenPendingQueueExceedsLimit_TrimsOldestEntriesAtConfiguredCap()
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection failure"));

        CreateMockHttpClient(handler.Object);
        _connectivity.NetworkAccess = NetworkAccess.None;
        var service = CreateService(TimeSpan.FromHours(1));

        for (var songId = 1; songId <= 1001; songId++)
        {
            await service.RecordStreamAsync(songId);
        }

        using var document = JsonDocument.Parse(_preferenceStore.GetString(PendingStreamRecordsPreferenceKey)!);
        var pendingRecords = document.RootElement.EnumerateArray().ToList();

        Assert.Multiple(() =>
        {
            Assert.That(pendingRecords, Has.Count.EqualTo(1000));
            Assert.That(pendingRecords.First().GetProperty("songMetadataId").GetInt32(), Is.EqualTo(2));
            Assert.That(pendingRecords.Last().GetProperty("songMetadataId").GetInt32(), Is.EqualTo(1001));
        });
    }

    // --- GetBulkLikeCountsAsync tests ---

    [Test]
    public async Task GetBulkLikeCountsAsync_ReturnsCountsFromApi()
    {
        // Arrange
        var expected = new List<LikeCountDto>
        {
            new() { SongMetadataId = 1, LikeCount = 5, DislikeCount = 2 },
            new() { SongMetadataId = 2, LikeCount = 10, DislikeCount = 1 }
        };
        var handler = CreateHandlerWithResponse(HttpStatusCode.OK, expected);
        CreateMockHttpClient(handler.Object);
        var service = CreateService();

        // Act
        var result = await service.GetBulkLikeCountsAsync([1, 2]);

        // Assert
        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result[0].SongMetadataId, Is.EqualTo(1));
        Assert.That(result[0].LikeCount, Is.EqualTo(5));
        Assert.That(result[0].DislikeCount, Is.EqualTo(2));
    }

    [Test]
    public async Task GetBulkLikeCountsAsync_ReturnsEmptyOnError()
    {
        // Arrange
        var handler = CreateHandlerWithResponse(HttpStatusCode.InternalServerError);
        CreateMockHttpClient(handler.Object);
        var service = CreateService();

        // Act
        var result = await service.GetBulkLikeCountsAsync([1, 2]);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetBulkLikeCountsAsync_ReturnsEmptyForEmptyIds()
    {
        // Arrange - shouldn't even need a handler since it short-circuits
        var handler = CreateHandlerWithResponse(HttpStatusCode.OK, Array.Empty<LikeCountDto>());
        CreateMockHttpClient(handler.Object);
        var service = CreateService();

        // Act
        var result = await service.GetBulkLikeCountsAsync([]);

        // Assert
        Assert.That(result, Is.Empty);
    }

    // --- ToggleLikeAsync tests ---

    [Test]
    public async Task ToggleLikeAsync_ReturnsResultOnSuccess()
    {
        // Arrange
        var expected = new LikeToggleResult { IsLiked = true, LikeCount = 6, DislikeCount = 2 };
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(r =>
                    r.Method == HttpMethod.Post &&
                    r.RequestUri!.PathAndQuery.Contains("api/music/like/42")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(expected)
            });
        CreateMockHttpClient(handler.Object);
        var service = CreateService();

        // Act
        var result = await service.ToggleLikeAsync(42);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.IsLiked, Is.True);
        Assert.That(result.LikeCount, Is.EqualTo(6));
    }

    [Test]
    public async Task ToggleLikeAsync_ReturnsNullOnAuthError()
    {
        // Arrange
        var handler = CreateHandlerWithResponse(HttpStatusCode.Unauthorized);
        CreateMockHttpClient(handler.Object);
        var service = CreateService();

        // Act
        var result = await service.ToggleLikeAsync(42);

        // Assert
        Assert.That(result, Is.Null);
    }

    // --- ToggleDislikeAsync tests ---

    [Test]
    public async Task ToggleDislikeAsync_ReturnsResultOnSuccess()
    {
        // Arrange
        var expected = new LikeToggleResult { IsDisliked = true, LikeCount = 5, DislikeCount = 3 };
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(r =>
                    r.Method == HttpMethod.Post &&
                    r.RequestUri!.PathAndQuery.Contains("api/music/dislike/42")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(expected)
            });
        CreateMockHttpClient(handler.Object);
        var service = CreateService();

        // Act
        var result = await service.ToggleDislikeAsync(42);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.IsDisliked, Is.True);
        Assert.That(result.DislikeCount, Is.EqualTo(3));
    }

    [Test]
    public async Task ToggleDislikeAsync_ReturnsNullOnError()
    {
        // Arrange
        var handler = CreateHandlerWithResponse(HttpStatusCode.InternalServerError);
        CreateMockHttpClient(handler.Object);
        var service = CreateService();

        // Act
        var result = await service.ToggleDislikeAsync(42);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetStreamQualifyingSecondsAsync_DelegatesToAppSettingsService()
    {
        // Arrange
        _mockAppSettingsService.Setup(s => s.GetStreamQualifyingSecondsAsync()).ReturnsAsync(45);
        var handler = CreateHandlerWithResponse(HttpStatusCode.OK);
        CreateMockHttpClient(handler.Object);
        var service = CreateService();

        // Act
        var result = await service.GetStreamQualifyingSecondsAsync();

        // Assert
        Assert.That(result, Is.EqualTo(45));
        _mockAppSettingsService.Verify(s => s.GetStreamQualifyingSecondsAsync(), Times.Once);
    }

    // --- Google Play Purchase Verification ---

    [Test]
    public async Task VerifyGooglePlayPurchaseAsync_ReturnsTrue_OnSuccess()
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(r =>
                    r.Method == HttpMethod.Post &&
                    r.RequestUri!.PathAndQuery.Contains("api/subscription/google-play/verify")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { success = true, subscriptionId = 1, status = "Active" })
            });
        CreateMockHttpClient(handler.Object);
        var service = CreateService();

        var result = await service.VerifyGooglePlayPurchaseAsync("token-123", "order-456");

        Assert.That(result.Success, Is.True);
        Assert.That(result.ErrorMessage, Is.Empty);
    }

    [Test]
    public async Task VerifyGooglePlayPurchaseAsync_ReturnsServerErrorMessage_OnServerError()
    {
        var handler = CreateHandlerWithResponse(HttpStatusCode.BadRequest,
            new { error = "Configured Google Play service account key file was not found on the server." });
        CreateMockHttpClient(handler.Object);
        var service = CreateService();

        var result = await service.VerifyGooglePlayPurchaseAsync("bad-token", null);

        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("Configured Google Play service account key file was not found on the server."));
    }

    [Test]
    public async Task VerifyGooglePlayPurchaseAsync_ReturnsConnectionMessage_OnException()
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Network error"));
        CreateMockHttpClient(handler.Object);
        var service = CreateService();

        var result = await service.VerifyGooglePlayPurchaseAsync("token", "order");

        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("Unable to connect to server"));
    }

    [Test]
    public async Task VerifySubscriptionPurchaseAsync_RoutesGooglePlayRequestToServer()
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(r =>
                    r.Method == HttpMethod.Post &&
                    r.RequestUri!.PathAndQuery.Contains("api/subscription/google-play/verify")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { success = true, subscriptionId = 1, status = "Active" })
            });
        CreateMockHttpClient(handler.Object);
        var service = CreateService();

        var result = await service.VerifySubscriptionPurchaseAsync(
            BillingPurchaseVerificationRequest.ForGooglePlay("token-123", "order-456"));

        Assert.That(result.Success, Is.True);
        Assert.That(result.ErrorMessage, Is.Empty);
    }

    [Test]
    public async Task VerifySubscriptionPurchaseAsync_ReturnsNotImplemented_ForAppleRequests()
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(r =>
                    r.Method == HttpMethod.Post &&
                    r.RequestUri!.PathAndQuery.Contains("api/subscription/app-store/verify")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { success = true, subscriptionId = 1, status = "Active" })
            });
        CreateMockHttpClient(handler.Object);
        var service = CreateService();

        var result = await service.VerifySubscriptionPurchaseAsync(
            BillingPurchaseVerificationRequest.ForApple("tx-123", "orig-123", "streamtunes_monthly_sub", "account-token"));

        Assert.That(result.Success, Is.True);
        Assert.That(result.ErrorMessage, Is.Empty);
    }

    [Test]
    public async Task VerifySubscriptionPurchaseAsync_IncludesTimeZoneId_ForAppleRequests()
    {
        string? requestBody = null;

        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(r =>
                    r.Method == HttpMethod.Post &&
                    r.RequestUri!.PathAndQuery.Contains("api/subscription/app-store/verify")),
                ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>(async (request, _) =>
            {
                requestBody = await request.Content!.ReadAsStringAsync();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { success = true, subscriptionId = 1, status = "Active" })
                };
            });
        CreateMockHttpClient(handler.Object);
        var service = CreateService();

        var result = await service.VerifySubscriptionPurchaseAsync(
            BillingPurchaseVerificationRequest.ForApple("tx-123", "orig-123", "streamtunes_monthly_sub", "account-token"));

        Assert.That(result.Success, Is.True);
        Assert.That(requestBody, Is.Not.Null);
        using var document = JsonDocument.Parse(requestBody!);
        Assert.That(document.RootElement.GetProperty("timeZoneId").GetString(), Is.EqualTo(TimeZoneInfo.Local.Id));
    }

    [Test]
    public async Task VerifySubscriptionPurchaseAsync_IncludesTimeZoneId_ForGooglePlayRequests()
    {
        string? requestBody = null;

        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(r =>
                    r.Method == HttpMethod.Post &&
                    r.RequestUri!.PathAndQuery.Contains("api/subscription/google-play/verify")),
                ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>(async (request, _) =>
            {
                requestBody = await request.Content!.ReadAsStringAsync();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { success = true, subscriptionId = 1, status = "Active" })
                };
            });
        CreateMockHttpClient(handler.Object);
        var service = CreateService();

        var result = await service.VerifySubscriptionPurchaseAsync(
            BillingPurchaseVerificationRequest.ForGooglePlay("token-123", "order-456", 2_990_000, "USD", "$2.99"));

        Assert.That(result.Success, Is.True);
        Assert.That(requestBody, Is.Not.Null);
        using var document = JsonDocument.Parse(requestBody!);
        Assert.That(document.RootElement.GetProperty("timeZoneId").GetString(), Is.EqualTo(TimeZoneInfo.Local.Id));
        Assert.That(document.RootElement.GetProperty("priceAmountMicros").GetInt64(), Is.EqualTo(2_990_000));
        Assert.That(document.RootElement.GetProperty("priceCurrencyCode").GetString(), Is.EqualTo("USD"));
        Assert.That(document.RootElement.GetProperty("formattedPrice").GetString(), Is.EqualTo("$2.99"));
    }

    [Test]
    public async Task VerifySubscriptionPurchaseAsync_ReturnsAppleServerErrorMessage_OnServerError()
    {
        var handler = CreateHandlerWithResponse(HttpStatusCode.BadRequest,
            new { error = "Apple App Store private key is not configured on the server." });
        CreateMockHttpClient(handler.Object);
        var service = CreateService();

        var result = await service.VerifySubscriptionPurchaseAsync(
            BillingPurchaseVerificationRequest.ForApple("tx-123", "orig-123", "streamtunes_monthly_sub", "account-token"));

        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("Apple App Store private key is not configured on the server."));
    }

    // --- Cancel Subscription ---

    [Test]
    public async Task CancelSubscriptionAsync_ReturnsSuccessAndEndDate_OnOk()
    {
        var endDate = DateTime.UtcNow.AddDays(30);
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(r =>
                    r.Method == HttpMethod.Post &&
                    r.RequestUri!.PathAndQuery.Contains("api/subscription/cancel")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { success = true, endDate = endDate })
            });
        CreateMockHttpClient(handler.Object);
        var service = CreateService();

        var (success, resultEndDate) = await service.CancelSubscriptionAsync();

        Assert.Multiple(() =>
        {
            Assert.That(success, Is.True);
            Assert.That(resultEndDate, Is.Not.Null);
        });
    }

    [Test]
    public async Task CancelSubscriptionAsync_ReturnsFailure_OnServerError()
    {
        var handler = CreateHandlerWithResponse(HttpStatusCode.BadRequest);
        CreateMockHttpClient(handler.Object);
        var service = CreateService();

        var (success, endDate) = await service.CancelSubscriptionAsync();

        Assert.Multiple(() =>
        {
            Assert.That(success, Is.False);
            Assert.That(endDate, Is.Null);
        });
    }

    [Test]
    public async Task CancelSubscriptionAsync_ReturnsFailure_OnException()
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Network error"));
        CreateMockHttpClient(handler.Object);
        var service = CreateService();

        var (success, endDate) = await service.CancelSubscriptionAsync();

        Assert.Multiple(() =>
        {
            Assert.That(success, Is.False);
            Assert.That(endDate, Is.Null);
        });
    }

    // --- ReportSongAsync Tests ---

    [Test]
    public async Task ReportSongAsync_ReturnsTrue_OnSuccess()
    {
        var handler = CreateHandlerWithResponse(HttpStatusCode.OK);
        CreateMockHttpClient(handler.Object);
        var service = CreateService();

        var result = await service.ReportSongAsync(42, "Copyright Violation");

        Assert.That(result, Is.True);
    }

    [Test]
    public async Task GetSubscriptionStatusAsync_ReturnsSubscriptionStatusDto()
    {
        var handler = CreateHandlerWithResponse(HttpStatusCode.OK, new
        {
            hasSubscription = true,
            status = "CANCELLED",
            endDate = DateTime.UtcNow.AddDays(5),
            billingSource = "GooglePlay"
        });
        CreateMockHttpClient(handler.Object);
        var service = CreateService();

        var result = await service.GetSubscriptionStatusAsync();

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.HasSubscription, Is.True);
        Assert.That(result.Status, Is.EqualTo("CANCELLED"));
        Assert.That(result.BillingSource, Is.EqualTo("GooglePlay"));
    }

    [Test]
    public async Task GetSubscriptionStatusAsync_ReturnsNull_OnException()
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Network error"));
        CreateMockHttpClient(handler.Object);
        var service = CreateService();

        var result = await service.GetSubscriptionStatusAsync();

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task ReportSongAsync_ReturnsFalse_OnServerError()
    {
        var handler = CreateHandlerWithResponse(HttpStatusCode.BadRequest);
        CreateMockHttpClient(handler.Object);
        var service = CreateService();

        var result = await service.ReportSongAsync(42, "Copyright Violation");

        Assert.That(result, Is.False);
    }

    [Test]
    public void ReportSongAsync_ThrowsInvalidOperationException_OnConflict()
    {
        var handler = CreateHandlerWithResponse(HttpStatusCode.Conflict);
        CreateMockHttpClient(handler.Object);
        var service = CreateService();

        var ex = Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ReportSongAsync(42, "Copyright Violation"));
        Assert.That(ex!.Message, Does.Contain("already reported"));
    }

    [Test]
    public async Task ReportSongAsync_ReturnsFalse_OnException()
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Network error"));
        CreateMockHttpClient(handler.Object);
        var service = CreateService();

        var result = await service.ReportSongAsync(42, "Copyright Violation");

        Assert.That(result, Is.False);
    }
}

sealed class InMemoryPreferenceStore : IAppPreferenceStore
{
    private readonly Dictionary<string, string> _values = [];

    public bool GetBool(string key, bool defaultValue = false)
        => bool.TryParse(GetString(key), out var value) ? value : defaultValue;

    public void SetBool(string key, bool value)
        => SetString(key, value.ToString());

    public int GetInt(string key, int defaultValue = 0)
        => int.TryParse(GetString(key), out var value) ? value : defaultValue;

    public void SetInt(string key, int value)
        => SetString(key, value.ToString());

    public string? GetString(string key)
        => _values.TryGetValue(key, out var value) ? value : null;

    public void SetString(string key, string value)
        => _values[key] = value;

    public void Remove(string key)
        => _values.Remove(key);
}

sealed class TestConnectivity : IConnectivity
{
    public event EventHandler<ConnectivityChangedEventArgs>? ConnectivityChanged;

    public NetworkAccess NetworkAccess { get; set; } = NetworkAccess.Internet;

    public IEnumerable<ConnectionProfile> ConnectionProfiles { get; set; } = [];

    public void RaiseConnectivityChanged()
        => ConnectivityChanged?.Invoke(this, new ConnectivityChangedEventArgs(NetworkAccess, ConnectionProfiles));
}
