using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.Tests.Services;

[TestFixture]
public class ContactApiServiceTests
{
    private Mock<IHttpClientFactory> _mockFactory = null!;
    private Mock<ILogger<ContactApiService>> _mockLogger = null!;

    [SetUp]
    public void SetUp()
    {
        _mockFactory = new Mock<IHttpClientFactory>();
        _mockLogger = new Mock<ILogger<ContactApiService>>();
    }

    [Test]
    public async Task SubmitContactRequestAsync_PostsExpectedPayload()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((request, _) => capturedRequest = request)
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));

        CreateMockHttpClient(handler.Object);

        var result = await CreateService().SubmitContactRequestAsync("Bug Report", "Something broke.");

        Assert.That(result.Success, Is.True);
        Assert.That(capturedRequest, Is.Not.Null);
        Assert.That(capturedRequest!.Method, Is.EqualTo(HttpMethod.Post));
        Assert.That(capturedRequest.RequestUri!.PathAndQuery, Is.EqualTo("/api/mobile/contact"));
        var body = await capturedRequest.Content!.ReadAsStringAsync();
        Assert.That(body, Does.Contain("Bug Report"));
        Assert.That(body, Does.Contain("Something broke."));
    }

    [Test]
    public async Task SubmitContactRequestAsync_ReturnsApiErrorMessage()
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = JsonContent.Create(new { error = "Please wait before sending another message." })
            });

        CreateMockHttpClient(handler.Object);

        var result = await CreateService().SubmitContactRequestAsync("Bug Report", "Something broke.");

        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorMessage, Is.EqualTo("Please wait before sending another message."));
    }

    [Test]
    public async Task SubmitContactRequestAsync_WhenRequestThrows_ReturnsFriendlyError()
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Network down"));

        CreateMockHttpClient(handler.Object);

        var result = await CreateService().SubmitContactRequestAsync("Bug Report", "Something broke.");

        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("Unable to send your message"));
    }

    private void CreateMockHttpClient(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://test.example.com/")
        };

        _mockFactory.Setup(factory => factory.CreateClient("MusicSalesApi")).Returns(client);
    }

    private ContactApiService CreateService() => new(_mockFactory.Object, _mockLogger.Object);
}