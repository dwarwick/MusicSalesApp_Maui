using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Moq;
using Moq.Protected;
using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.ViewModels;
using Microsoft.Extensions.Logging;

namespace MusicSalesApp.Maui.Tests.Services;

[TestFixture]
public class MusicServiceTests
{
    private Mock<IHttpClientFactory> _mockFactory;
    private Mock<IAppSettingsService> _mockAppSettingsService;
    private Mock<ILogger<MusicService>> _mockLogger;

    [SetUp]
    public void Setup()
    {
        _mockFactory = new Mock<IHttpClientFactory>();
        _mockAppSettingsService = new Mock<IAppSettingsService>();
        _mockLogger = new Mock<ILogger<MusicService>>();
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

    private MusicService CreateService() => new(_mockFactory.Object, _mockAppSettingsService.Object, _mockLogger.Object);

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
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(r =>
                    r.Method == HttpMethod.Post &&
                    r.RequestUri!.PathAndQuery.Contains("api/music/stream/42")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));
        CreateMockHttpClient(handler.Object);
        var service = CreateService();

        // Act & Assert — no exception
        await service.RecordStreamAsync(42);

        handler.Protected().Verify("SendAsync", Times.Once(),
            ItExpr.Is<HttpRequestMessage>(r =>
                r.Method == HttpMethod.Post &&
                r.RequestUri!.PathAndQuery.Contains("api/music/stream/42")),
            ItExpr.IsAny<CancellationToken>());
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
