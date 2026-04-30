using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Tests.Services;

[TestFixture]
public class TipApiServiceTests
{
    private Mock<IHttpClientFactory> _mockFactory = null!;
    private Mock<ILogger<TipApiService>> _mockLogger = null!;

    [SetUp]
    public void Setup()
    {
        _mockFactory = new Mock<IHttpClientFactory>();
        _mockLogger = new Mock<ILogger<TipApiService>>();
    }

    private void CreateMockHttpClient(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://test.example.com/")
        };

        _mockFactory.Setup(f => f.CreateClient("MusicSalesApi")).Returns(client);
    }

    private TipApiService CreateService() => new(_mockFactory.Object, _mockLogger.Object);

    [Test]
    public async Task CreateOrderAsync_ReturnsApprovalUrl_OnSuccess()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((request, _) => capturedRequest = request)
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new TipOperationResponseDto
                {
                    Success = true,
                    ResultKind = TipResultKinds.RequiresApproval,
                    ApprovalUrl = "https://paypal.test/approve"
                })
            });

        CreateMockHttpClient(handler.Object);

        var result = await CreateService().CreateOrderAsync(7, 11, 5.00m);

        Assert.That(result.Success, Is.True);
        Assert.That(result.ResultKind, Is.EqualTo(TipResultKinds.RequiresApproval));
        Assert.That(result.ApprovalUrl, Is.EqualTo("https://paypal.test/approve"));
        Assert.That(capturedRequest, Is.Not.Null);
        Assert.That(capturedRequest!.Method, Is.EqualTo(HttpMethod.Post));
        Assert.That(capturedRequest.RequestUri!.PathAndQuery, Is.EqualTo("/api/mobile/tips/create-order"));
        var body = await capturedRequest.Content!.ReadAsStringAsync();
        Assert.That(body, Does.Contain("creatorId"));
        Assert.That(body, Does.Contain("7"));
    }

    [Test]
    public async Task CaptureAsync_WhenRequestFails_ReturnsFailureMessage()
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = JsonContent.Create(new { message = "Tip not found." })
            });

        CreateMockHttpClient(handler.Object);

        var result = await CreateService().CaptureAsync("ORDER-1");

        Assert.That(result.Success, Is.False);
        Assert.That(result.ResultKind, Is.EqualTo(TipResultKinds.PaymentFailure));
        Assert.That(result.Message, Does.Contain("Tip not found."));
    }

    [Test]
    public async Task CancelAsync_WhenRequestThrows_ReturnsFailureResponse()
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Network down"));

        CreateMockHttpClient(handler.Object);

        var result = await CreateService().CancelAsync("ORDER-2");

        Assert.That(result.Success, Is.False);
        Assert.That(result.ResultKind, Is.EqualTo(TipResultKinds.PaymentFailure));
        Assert.That(result.Message, Does.Contain("Network down"));
    }
}