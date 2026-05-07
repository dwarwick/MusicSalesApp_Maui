using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Tests.Services;

[TestFixture]
public class AdminMessageApiServiceTests
{
    private Mock<IHttpClientFactory> _mockFactory = default!;
    private Mock<ILogger<AdminMessageApiService>> _mockLogger = default!;
    private AdminMessageApiService _service = default!;

    [SetUp]
    public void SetUp()
    {
        _mockFactory = new Mock<IHttpClientFactory>();
        _mockLogger = new Mock<ILogger<AdminMessageApiService>>();
        _service = new AdminMessageApiService(_mockFactory.Object, _mockLogger.Object);
    }

    [Test]
    public async Task GetPendingDialogMessagesAsync_ReturnsMessagesFromApi()
    {
        var handler = CreateHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new List<PendingAdminMessageDto>
            {
                new() { MessageId = 7, MessageText = "Hello", CreatedAtUtc = DateTime.UtcNow }
            })
        });

        _mockFactory.Setup(x => x.CreateClient("MusicSalesApi")).Returns(CreateHttpClient(handler.Object));

        var messages = await _service.GetPendingDialogMessagesAsync();

        Assert.That(messages.Count, Is.EqualTo(1));
        Assert.That(messages[0].MessageId, Is.EqualTo(7));
    }

    [Test]
    public async Task AcknowledgeMessageAsync_ReturnsFalse_WhenRequestFails()
    {
        var handler = CreateHandler(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        _mockFactory.Setup(x => x.CreateClient("MusicSalesApi")).Returns(CreateHttpClient(handler.Object));

        var acknowledged = await _service.AcknowledgeMessageAsync(7);

        Assert.That(acknowledged, Is.False);
    }

    private static HttpClient CreateHttpClient(HttpMessageHandler handler)
    {
        return new HttpClient(handler)
        {
            BaseAddress = new Uri("https://streamtunes.net/")
        };
    }

    private static Mock<HttpMessageHandler> CreateHandler(HttpResponseMessage response)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);
        return handler;
    }
}