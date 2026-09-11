using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using MusicSalesApp.Common.Helpers;
using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.Tests.Services;

/// <summary>
/// The wire contract for the notification preferences.
/// </summary>
/// <remarks>
/// <see cref="NotificationPreferences"/> is a hand-mirrored copy of the server's record, matched by
/// JSON property name and nothing else - so a rename on either side compiles cleanly on both and
/// deserialises to the defaults here. These tests are what turns that into a failure rather than a
/// listener quietly losing their email preferences, since a PUT replaces the whole record.
/// </remarks>
[TestFixture]
public class NotificationPreferenceApiServiceTests
{
    private Mock<IHttpClientFactory> _factory = null!;
    private HttpRequestMessage? _lastRequest;

    [SetUp]
    public void SetUp()
    {
        _factory = new Mock<IHttpClientFactory>();
        _lastRequest = null;
    }

    private void Given(Func<HttpResponseMessage> respond)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((request, _) => _lastRequest = request)
            .ReturnsAsync(respond);

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

    private NotificationPreferenceApiService CreateService() =>
        new(_factory.Object, Mock.Of<ILogger<NotificationPreferenceApiService>>());

    [Test]
    public async Task Get_ReadsEveryField_FromTheServersPropertyNames()
    {
        // Spelled out as JSON rather than round-tripping the same class, which would pass however
        // the names drifted.
        Given(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                {
                  "receiveArtistReleaseEmails": true,
                  "receiveArtistMessageEmails": false,
                  "receiveArtistReleasePush": true,
                  "receiveArtistMessagePush": true,
                  "artistPushFrequency": 2
                }
                """,
                System.Text.Encoding.UTF8,
                "application/json")
        });

        var preferences = await CreateService().GetAsync();

        Assert.That(preferences, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(preferences!.ReceiveArtistReleaseEmails, Is.True);
            Assert.That(preferences.ReceiveArtistMessageEmails, Is.False);
            Assert.That(preferences.ReceiveArtistReleasePush, Is.True);
            Assert.That(preferences.ReceiveArtistMessagePush, Is.True);
            Assert.That(preferences.ArtistPushFrequency, Is.EqualTo((ArtistPushFrequency)2));
        });
    }

    [Test]
    public async Task Get_IsNull_WhenSignedOut()
    {
        // 401 is ordinary: the settings page is reachable signed out.
        Given(() => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        Assert.That(await CreateService().GetAsync(), Is.Null);
    }

    [Test]
    public async Task Get_IsNull_WhenTheServerCannotBeReached()
    {
        GivenTransportFailure();

        Assert.That(await CreateService().GetAsync(), Is.Null);
    }

    [Test]
    public async Task Set_SendsTheWholeRecord()
    {
        // The endpoint replaces everything it is given, so a partial body would switch the
        // listener's email preferences off.
        Given(() => new HttpResponseMessage(HttpStatusCode.OK));

        await CreateService().SetAsync(new NotificationPreferences
        {
            ReceiveArtistReleaseEmails = true,
            ReceiveArtistMessageEmails = true,
            ReceiveArtistReleasePush = false,
            ReceiveArtistMessagePush = true,
            ArtistPushFrequency = ArtistPushFrequency.Daily,
        });

        var body = await _lastRequest!.Content!.ReadAsStringAsync();
        using var sent = JsonDocument.Parse(body);

        Assert.Multiple(() =>
        {
            foreach (var name in new[]
                     {
                         "ReceiveArtistReleaseEmails",
                         "ReceiveArtistMessageEmails",
                         "ReceiveArtistReleasePush",
                         "ReceiveArtistMessagePush",
                         "ArtistPushFrequency",
                     })
            {
                Assert.That(
                    sent.RootElement.TryGetProperty(
                        System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(name),
                        out _),
                    Is.True,
                    $"{name} must be sent, or the server records its default");
            }
        });
    }

    [Test]
    public async Task Set_ReportsFailure_WhenTheServerRefuses()
    {
        Given(() => new HttpResponseMessage(HttpStatusCode.BadRequest));

        Assert.That(await CreateService().SetAsync(new NotificationPreferences()), Is.False);
    }

    [Test]
    public async Task Set_ReportsFailure_WhenTheServerCannotBeReached()
    {
        GivenTransportFailure();

        Assert.That(await CreateService().SetAsync(new NotificationPreferences()), Is.False);
    }

    [Test]
    public async Task Set_ReportsFailure_WithoutAskingTheServer_WhenGivenNothing()
    {
        // No handler is set up, so reaching the network here would throw.
        Assert.That(await CreateService().SetAsync(null!), Is.False);
    }
}
