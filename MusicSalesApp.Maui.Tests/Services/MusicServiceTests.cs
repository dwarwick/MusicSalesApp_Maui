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
    private Mock<ILogger<MusicService>> _mockLogger;

    [SetUp]
    public void Setup()
    {
        _mockFactory = new Mock<IHttpClientFactory>();
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

    [Test]
    public async Task GetSongsAsync_ReturnsSongsFromApi()
    {
        // Arrange
        var expectedSongs = new List<SongDto>
        {
            new() { Id = 1, SongTitle = "Test Song", ArtistName = "Artist", Genre = "Rock", StreamCount = 42 },
            new() { Id = 2, SongTitle = "Another", ArtistName = "Other", Genre = "Pop", StreamCount = 10 }
        };
        var handler = CreateHandlerWithResponse(HttpStatusCode.OK, expectedSongs);
        CreateMockHttpClient(handler.Object);
        var service = new MusicService(_mockFactory.Object, _mockLogger.Object);

        // Act
        var result = await service.GetSongsAsync();

        // Assert
        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result[0].SongTitle, Is.EqualTo("Test Song"));
        Assert.That(result[1].SongTitle, Is.EqualTo("Another"));
    }

    [Test]
    public async Task GetSongsAsync_ReturnsEmptyListOnError()
    {
        // Arrange
        var handler = CreateHandlerWithResponse(HttpStatusCode.InternalServerError);
        CreateMockHttpClient(handler.Object);
        var service = new MusicService(_mockFactory.Object, _mockLogger.Object);

        // Act
        var result = await service.GetSongsAsync();

        // Assert
        Assert.That(result, Is.Empty);
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
        var service = new MusicService(_mockFactory.Object, _mockLogger.Object);

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
        var service = new MusicService(_mockFactory.Object, _mockLogger.Object);

        // Act & Assert — should not throw
        Assert.DoesNotThrowAsync(() => service.RecordStreamAsync(1));
    }
}
