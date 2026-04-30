using System.Net;
using System.Net.Http.Json;
using Moq;
using Moq.Protected;
using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.ViewModels;
using Microsoft.Extensions.Logging;

namespace MusicSalesApp.Maui.Tests.Services;

[TestFixture]
public class PlaylistServiceTests
{
    private Mock<IHttpClientFactory> _mockFactory;
    private Mock<ILogger<PlaylistService>> _mockLogger;

    [SetUp]
    public void Setup()
    {
        _mockFactory = new Mock<IHttpClientFactory>();
        _mockLogger = new Mock<ILogger<PlaylistService>>();
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

    private PlaylistService CreateService() => new(_mockFactory.Object, _mockLogger.Object);

    [Test]
    public async Task GetHomePlaylistsAsync_ReturnsDto_OnSuccess()
    {
        var expected = new HomePlaylistsDto
        {
            Recommended = new PlaylistDto { Id = 0, Name = "Recommended For You", SongCount = 5, Kind = PlaylistKinds.Recommended, IsSystemGenerated = true },
            LikedSongs = new PlaylistDto { Id = 3, Name = "Liked Songs", SongCount = 2, Kind = PlaylistKinds.LikedSongs, IsSystemGenerated = true }
        };
        CreateMockHttpClient(CreateHandlerWithResponse(HttpStatusCode.OK, expected).Object);

        var result = await CreateService().GetHomePlaylistsAsync();

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Recommended, Is.Not.Null);
        Assert.That(result.Recommended!.SongCount, Is.EqualTo(5));
        Assert.That(result.LikedSongs, Is.Not.Null);
        Assert.That(result.LikedSongs!.Id, Is.EqualTo(3));
    }

    [Test]
    public async Task GetHomePlaylistsAsync_ReturnsNull_OnError()
    {
        CreateMockHttpClient(CreateHandlerWithResponse(HttpStatusCode.InternalServerError).Object);

        var result = await CreateService().GetHomePlaylistsAsync();

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetMyPlaylistsAsync_ReturnsListFromServer()
    {
        var items = new List<PlaylistDto>
        {
            new() { Id = 1, Name = "Mine", SongCount = 3, Kind = PlaylistKinds.Custom },
            new() { Id = 2, Name = "Liked Songs", SongCount = 1, Kind = PlaylistKinds.LikedSongs, IsSystemGenerated = true },
        };
        CreateMockHttpClient(CreateHandlerWithResponse(HttpStatusCode.OK, items).Object);

        var result = await CreateService().GetMyPlaylistsAsync();

        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result[0].Name, Is.EqualTo("Mine"));
    }

    [Test]
    public async Task GetPlaylistSongsAsync_ReturnsNull_OnNotFound()
    {
        CreateMockHttpClient(CreateHandlerWithResponse(HttpStatusCode.NotFound).Object);

        var result = await CreateService().GetPlaylistSongsAsync(123);

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task CreatePlaylistAsync_ReturnsNeedsSubscription_On403()
    {
        CreateMockHttpClient(CreateHandlerWithResponse(HttpStatusCode.Forbidden).Object);

        var result = await CreateService().CreatePlaylistAsync("My Jams");

        Assert.That(result.Success, Is.False);
        Assert.That(result.RequiresSubscription, Is.True);
    }

    [Test]
    public async Task CreatePlaylistAsync_ReturnsDto_OnSuccess()
    {
        var created = new PlaylistDto { Id = 42, Name = "New", Kind = PlaylistKinds.Custom };
        CreateMockHttpClient(CreateHandlerWithResponse(HttpStatusCode.OK, created).Object);

        var result = await CreateService().CreatePlaylistAsync("New");

        Assert.That(result.Success, Is.True);
        Assert.That(result.Value, Is.Not.Null);
        Assert.That(result.Value!.Id, Is.EqualTo(42));
    }

    [Test]
    public async Task RenamePlaylistAsync_SendsPut_AndReturnsOk()
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(r =>
                    r.Method == HttpMethod.Put &&
                    r.RequestUri!.PathAndQuery.EndsWith("/api/mobile/playlists/7")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.NoContent));
        CreateMockHttpClient(handler.Object);

        var result = await CreateService().RenamePlaylistAsync(7, "Renamed");

        Assert.That(result.Success, Is.True);
        handler.Protected().Verify("SendAsync", Times.Once(),
            ItExpr.Is<HttpRequestMessage>(r =>
                r.Method == HttpMethod.Put &&
                r.RequestUri!.PathAndQuery.EndsWith("/api/mobile/playlists/7")),
            ItExpr.IsAny<CancellationToken>());
    }

    [Test]
    public async Task DeletePlaylistAsync_ReturnsNeedsSubscription_On403()
    {
        CreateMockHttpClient(CreateHandlerWithResponse(HttpStatusCode.Forbidden).Object);

        var result = await CreateService().DeletePlaylistAsync(1);

        Assert.That(result.RequiresSubscription, Is.True);
    }

    [Test]
    public async Task GetAvailableSongsAsync_ReturnsResponse()
    {
        var payload = new AvailableSongsResponse
        {
            Songs = new List<PlaylistSongDto> { new() { Id = 1, SongMetadataId = 10, SongTitle = "A", CreatorId = 44, CreatorUserId = 77 } },
            RequiresSubscription = false
        };
        CreateMockHttpClient(CreateHandlerWithResponse(HttpStatusCode.OK, payload).Object);

        var result = await CreateService().GetAvailableSongsAsync(5);

        Assert.That(result.Songs, Has.Count.EqualTo(1));
        Assert.That(result.Songs[0].CreatorId, Is.EqualTo(44));
        Assert.That(result.Songs[0].CreatorUserId, Is.EqualTo(77));
        Assert.That(result.RequiresSubscription, Is.False);
    }

    [Test]
    public async Task AddSongAsync_ReturnsNeedsSubscription_On403()
    {
        CreateMockHttpClient(CreateHandlerWithResponse(HttpStatusCode.Forbidden).Object);

        var result = await CreateService().AddSongAsync(1, 2);

        Assert.That(result.RequiresSubscription, Is.True);
    }

    [Test]
    public async Task AddSongAsync_ReturnsOk_OnNoContent()
    {
        CreateMockHttpClient(CreateHandlerWithResponse(HttpStatusCode.NoContent).Object);

        var result = await CreateService().AddSongAsync(1, 2);

        Assert.That(result.Success, Is.True);
    }

    [Test]
    public async Task RemoveSongAsync_ReturnsFail_On404()
    {
        CreateMockHttpClient(CreateHandlerWithResponse(HttpStatusCode.NotFound).Object);

        var result = await CreateService().RemoveSongAsync(1, 99);

        Assert.That(result.Success, Is.False);
        Assert.That(result.RequiresSubscription, Is.False);
    }

    [Test]
    public async Task ReorderAsync_SendsPutWithIdsPayload()
    {
        HttpRequestMessage? captured = null;
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.NoContent));
        CreateMockHttpClient(handler.Object);

        var result = await CreateService().ReorderAsync(11, new[] { 3, 1, 2 });

        Assert.That(result.Success, Is.True);
        Assert.That(captured, Is.Not.Null);
        Assert.That(captured!.Method, Is.EqualTo(HttpMethod.Put));
        Assert.That(captured.RequestUri!.PathAndQuery, Does.EndWith("/api/mobile/playlists/11/reorder"));
        var body = await captured.Content!.ReadAsStringAsync();
        Assert.That(body, Does.Contain("userPlaylistIds").IgnoreCase);
        Assert.That(body, Does.Contain("3"));
    }

    [Test]
    public async Task ReorderAsync_ReturnsNeedsSubscription_On403()
    {
        CreateMockHttpClient(CreateHandlerWithResponse(HttpStatusCode.Forbidden).Object);

        var result = await CreateService().ReorderAsync(1, new[] { 1, 2 });

        Assert.That(result.RequiresSubscription, Is.True);
    }
}
