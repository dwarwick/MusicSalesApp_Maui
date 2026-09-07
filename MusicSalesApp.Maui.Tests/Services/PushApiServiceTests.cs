using System.Net;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.Tests.Services;

/// <summary>
/// The one thing this service decides, and the reason it returns an enum rather than a bool: a
/// registration that failed because the server is unreachable will work later, and one the server
/// refused will not. Collapsing them either retries forever or gives up on a device that would have
/// registered fine a minute later.
/// </summary>
[TestFixture]
public class PushApiServiceTests
{
    private Mock<IHttpClientFactory> _factory = null!;

    [SetUp]
    public void SetUp() => _factory = new Mock<IHttpClientFactory>();

    private void GivenStatus(HttpStatusCode status)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => new HttpResponseMessage(status));

        _factory
            .Setup(f => f.CreateClient("MusicSalesApi"))
            .Returns(new HttpClient(handler.Object) { BaseAddress = new Uri("https://test.example.com/") });
    }

    private void GivenTransportFailure()
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("no route to host"));

        _factory
            .Setup(f => f.CreateClient("MusicSalesApi"))
            .Returns(new HttpClient(handler.Object) { BaseAddress = new Uri("https://test.example.com/") });
    }

    private PushApiService CreateService() =>
        new(_factory.Object, Mock.Of<ILogger<PushApiService>>());

    private Task<PushRegistrationOutcome> RegisterAsync() =>
        CreateService().RegisterDeviceAsync("android", "token-abc", "device-1");

    [Test]
    public async Task Register_IsRegistered_OnSuccess()
    {
        GivenStatus(HttpStatusCode.OK);

        Assert.That(await RegisterAsync(), Is.EqualTo(PushRegistrationOutcome.Registered));
    }

    [TestCase(HttpStatusCode.Unauthorized)]
    [TestCase(HttpStatusCode.InternalServerError)]
    [TestCase(HttpStatusCode.BadGateway)]
    [TestCase(HttpStatusCode.ServiceUnavailable)]
    public async Task Register_IsDeferred_ForFailuresThatCouldPassLater(HttpStatusCode status)
    {
        // A 401 here is a token the app has not refreshed yet, not a device the server rejected.
        Assert.That(await Deferred(status), Is.EqualTo(PushRegistrationOutcome.Deferred));

        async Task<PushRegistrationOutcome> Deferred(HttpStatusCode s)
        {
            GivenStatus(s);
            return await RegisterAsync();
        }
    }

    [Test]
    public async Task Register_IsDeferred_WhenTheServerCannotBeReached()
    {
        GivenTransportFailure();

        Assert.That(await RegisterAsync(), Is.EqualTo(PushRegistrationOutcome.Deferred));
    }

    [TestCase(HttpStatusCode.BadRequest)]
    [TestCase(HttpStatusCode.Forbidden)]
    [TestCase(HttpStatusCode.NotFound)]
    public async Task Register_IsRejected_ForRefusalsRetryingCannotFix(HttpStatusCode status)
    {
        GivenStatus(status);

        Assert.That(await RegisterAsync(), Is.EqualTo(PushRegistrationOutcome.Rejected));
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase(null)]
    public async Task Register_IsRejected_WithoutAskingTheServer_WhenThereIsNoToken(string? token)
    {
        // No request is set up: reaching the network here would throw, which is the assertion.
        var outcome = await CreateService().RegisterDeviceAsync("android", token!, "device-1");

        Assert.That(outcome, Is.EqualTo(PushRegistrationOutcome.Rejected));
    }

    [Test]
    public async Task Unregister_ReportsSuccess_OnlyWhenTheServerConfirms()
    {
        GivenStatus(HttpStatusCode.OK);
        Assert.That(await CreateService().UnregisterDeviceAsync("token-abc"), Is.True);
    }

    [Test]
    public async Task Unregister_ReportsFailure_WhenTheServerRefuses()
    {
        // The caller uses this to warn that the handset may keep receiving the previous account's
        // notifications, so a false must not be mistaken for a clean unregister.
        GivenStatus(HttpStatusCode.Unauthorized);

        Assert.That(await CreateService().UnregisterDeviceAsync("token-abc"), Is.False);
    }

    [Test]
    public async Task Unregister_ReportsFailure_WhenTheServerCannotBeReached()
    {
        GivenTransportFailure();

        Assert.That(await CreateService().UnregisterDeviceAsync("token-abc"), Is.False);
    }
}
