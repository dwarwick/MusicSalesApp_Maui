using System.Net;
using System.Net.Http.Json;
using Moq;
using Moq.Protected;
using MusicSalesApp.Maui.Services;
using Microsoft.Extensions.Logging;

namespace MusicSalesApp.Maui.Tests.Services;

[TestFixture]
public class AppSettingsServiceTests
{
    private Mock<IHttpClientFactory> _mockFactory;
    private Mock<ILogger<AppSettingsService>> _mockLogger;

    [SetUp]
    public void Setup()
    {
        _mockFactory = new Mock<IHttpClientFactory>();
        _mockLogger = new Mock<ILogger<AppSettingsService>>();
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
    public async Task GetStreamQualifyingSecondsAsync_ReturnsValueFromApi()
    {
        var handler = CreateHandlerWithResponse(HttpStatusCode.OK, new { streamQualifyingSeconds = 45 });
        CreateMockHttpClient(handler.Object);
        var service = new AppSettingsService(_mockFactory.Object, _mockLogger.Object);

        var result = await service.GetStreamQualifyingSecondsAsync();

        Assert.That(result, Is.EqualTo(45));
    }

    [Test]
    public async Task GetStreamQualifyingSecondsAsync_ReturnsDefaultOnHttpError()
    {
        var handler = CreateHandlerWithResponse(HttpStatusCode.InternalServerError);
        CreateMockHttpClient(handler.Object);
        var service = new AppSettingsService(_mockFactory.Object, _mockLogger.Object);

        var result = await service.GetStreamQualifyingSecondsAsync();

        Assert.That(result, Is.EqualTo(30));
    }

    [Test]
    public async Task GetStreamQualifyingSecondsAsync_ReturnsDefaultOnNetworkException()
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Network error"));
        CreateMockHttpClient(handler.Object);
        var service = new AppSettingsService(_mockFactory.Object, _mockLogger.Object);

        var result = await service.GetStreamQualifyingSecondsAsync();

        Assert.That(result, Is.EqualTo(30));
    }

    [Test]
    public async Task CachesResultsAfterFirstFetch()
    {
        var handler = CreateHandlerWithResponse(HttpStatusCode.OK, new { streamQualifyingSeconds = 45 });
        CreateMockHttpClient(handler.Object);
        var service = new AppSettingsService(_mockFactory.Object, _mockLogger.Object);

        var seconds1 = await service.GetStreamQualifyingSecondsAsync();
        var seconds2 = await service.GetStreamQualifyingSecondsAsync();

        Assert.Multiple(() =>
        {
            Assert.That(seconds1, Is.EqualTo(45));
            Assert.That(seconds2, Is.EqualTo(45));
        });

        // Verify HTTP was called only once (cached after first call)
        handler.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }
}
