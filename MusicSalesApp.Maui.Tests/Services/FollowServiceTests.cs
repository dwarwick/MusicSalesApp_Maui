using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.Tests.Services;

/// <summary>
/// The follow client's contract with the server, and the two places it is easy to get wrong: what a
/// failed bulk lookup reports, and when the notifier is allowed to run.
/// </summary>
[TestFixture]
public class FollowServiceTests
{
    private Mock<IHttpClientFactory> _factory = null!;
    private Mock<IAuthService> _authService = null!;
    private ArtistFollowNotifier _notifier = null!;

    [SetUp]
    public void SetUp()
    {
        _factory = new Mock<IHttpClientFactory>();

        _authService = new Mock<IAuthService>();
        _authService.SetupGet(a => a.IsLoggedIn).Returns(true);

        // The real notifier: whether it is raised, and when, is the behaviour under test.
        _notifier = new ArtistFollowNotifier();
    }

    private void GivenResponse(Func<HttpResponseMessage> respond)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
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

    private FollowService CreateService() =>
        new(_factory.Object, _authService.Object, _notifier, Mock.Of<ILogger<FollowService>>());

    // -------------------------------------------------------------------------------------------
    // The bulk lookup: "you follow none of these" versus "we could not ask"
    // -------------------------------------------------------------------------------------------

    [Test]
    public async Task GetFollowedPersonaIds_ReturnsTheSet_WhenTheServerAnswers()
    {
        GivenResponse(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new List<int> { 10, 12 })
        });

        var followed = await CreateService().GetFollowedPersonaIdsAsync([10, 11, 12]);

        Assert.That(followed, Is.EquivalentTo(new[] { 10, 12 }));
    }

    [Test]
    public async Task GetFollowedPersonaIds_ReturnsEmpty_WhenTheServerSaysNone()
    {
        // An answer, and the caller is right to clear on it.
        GivenResponse(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new List<int>())
        });

        var followed = await CreateService().GetFollowedPersonaIdsAsync([10]);

        Assert.That(followed, Is.Not.Null.And.Empty);
    }

    [Test]
    public async Task GetFollowedPersonaIds_ReturnsNull_WhenTheRequestFails()
    {
        // Not empty. Empty would tell the coordinator the user follows nobody, which erases the
        // shared set and visibly unfollows their whole library over a dropped connection.
        GivenTransportFailure();

        var followed = await CreateService().GetFollowedPersonaIdsAsync([10]);

        Assert.That(followed, Is.Null);
    }

    [TestCase(HttpStatusCode.Unauthorized)]
    [TestCase(HttpStatusCode.InternalServerError)]
    [TestCase(HttpStatusCode.ServiceUnavailable)]
    public async Task GetFollowedPersonaIds_ReturnsNull_ForAnyNonSuccessStatus(HttpStatusCode status)
    {
        GivenResponse(() => new HttpResponseMessage(status));

        var followed = await CreateService().GetFollowedPersonaIdsAsync([10]);

        Assert.That(followed, Is.Null, $"{status} means we could not ask, not that they follow nobody");
    }

    [Test]
    public async Task GetFollowedPersonaIds_ReturnsEmpty_WhenSignedOut()
    {
        // Signed out genuinely IS "follows nobody", and costs no round trip.
        _authService.SetupGet(a => a.IsLoggedIn).Returns(false);
        GivenTransportFailure();

        var followed = await CreateService().GetFollowedPersonaIdsAsync([10]);

        Assert.That(followed, Is.Not.Null.And.Empty);
    }

    // -------------------------------------------------------------------------------------------
    // Setting the state, and the notifier
    // -------------------------------------------------------------------------------------------

    [Test]
    public async Task SetFollowState_ReportsWhatTheServerSettledOn_NotWhatWasAsked()
    {
        // Following someone already followed answers AlreadyFollowing; a refusal that still
        // returned 200 would otherwise be recorded as success.
        GivenResponse(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new FollowStateResult(10, Following: false, Outcome: "Blocked"))
        });

        var result = await CreateService().SetFollowStateAsync(10, following: true);

        Assert.That(result!.Following, Is.False);
    }

    [Test]
    public async Task SetFollowState_RaisesTheNotifier_WithTheSettledState()
    {
        GivenResponse(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new FollowStateResult(10, Following: true, Outcome: "Followed"))
        });

        ArtistFollowChange? seen = null;
        _notifier.FollowStateChanged += (_, change) => seen = change;

        await CreateService().SetFollowStateAsync(10, following: true);

        Assert.Multiple(() =>
        {
            Assert.That(seen, Is.Not.Null);
            Assert.That(seen!.PersonaId, Is.EqualTo(10));
            Assert.That(seen.IsFollowing, Is.True);
        });
    }

    [Test]
    public async Task SetFollowState_StillReportsSuccess_WhenASubscriberThrows()
    {
        // The server has recorded the follow by the time subscribers run, so a subscriber blowing
        // up - easy, since they touch bound state - must not turn it into a reported failure. That
        // is what rolled the bell back on a change the server had accepted, and blamed the network.
        GivenResponse(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new FollowStateResult(10, Following: true, Outcome: "Followed"))
        });

        _notifier.FollowStateChanged += (_, _) => throw new InvalidOperationException("bound state blew up");

        var result = await CreateService().SetFollowStateAsync(10, following: true);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null, "the follow was saved, so it must be reported as saved");
            Assert.That(result!.Following, Is.True);
        });
    }

    [Test]
    public async Task SetFollowState_ReturnsNull_OnADomainRefusal()
    {
        // 400 is the contract for every domain refusal - following yourself, a blocked artist, an
        // unavailable persona - and must not be retried as though it were a transport failure.
        GivenResponse(() => new HttpResponseMessage(HttpStatusCode.BadRequest));

        var result = await CreateService().SetFollowStateAsync(10, following: true);

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task SetFollowState_ReturnsNull_AndRaisesNothing_WhenTheRequestFails()
    {
        GivenTransportFailure();

        var raised = false;
        _notifier.FollowStateChanged += (_, _) => raised = true;

        var result = await CreateService().SetFollowStateAsync(10, following: true);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Null);
            Assert.That(raised, Is.False, "nothing changed, so nothing may be announced");
        });
    }

    [Test]
    public async Task SetFollowState_ReturnsNull_WhenSignedOut()
    {
        _authService.SetupGet(a => a.IsLoggedIn).Returns(false);
        GivenTransportFailure();

        var result = await CreateService().SetFollowStateAsync(10, following: true);

        Assert.That(result, Is.Null);
    }
}
