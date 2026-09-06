using Microsoft.Extensions.Logging;
using Moq;
using MusicSalesApp.Common.Helpers;
using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Tests.Services;

/// <summary>
/// Where a tapped notification takes you. The two platform heads capture the payload very
/// differently but hand over the same flat dictionary, so every routing decision is tested here.
/// </summary>
[TestFixture]
public class PushNotificationRouterTests
{
    private Mock<INavigationService> _navigation;
    private Mock<IMusicService> _musicService;
    private PushNotificationRouter _router;

    private static readonly SongDto Song = new() { Id = 42, SongTitle = "Test Song" };

    [SetUp]
    public void SetUp()
    {
        _navigation = new Mock<INavigationService>();
        _musicService = new Mock<IMusicService>();
        _musicService.Setup(x => x.GetSongsAsync()).ReturnsAsync([Song]);

        _router = new PushNotificationRouter(
            _navigation.Object,
            _musicService.Object,
            Mock.Of<ILogger<PushNotificationRouter>>());
    }

    private static Dictionary<string, string?> ReleasePayload(string songId = "42") => new()
    {
        [PushDataKeys.Kind] = PushNotificationKinds.Release,
        [PushDataKeys.PersonaId] = "7",
        [PushDataKeys.SongId] = songId,
        [PushDataKeys.EntityId] = "1234",
    };

    private void VerifyNavigatedToSong() =>
        _navigation.Verify(
            x => x.GoToAsync(
                NavigationRoutes.SongPlayer,
                It.Is<IDictionary<string, object>>(p => ReferenceEquals(p["Song"], Song))),
            Times.Once);

    private void VerifyNoNavigation() =>
        _navigation.Verify(
            x => x.GoToAsync(It.IsAny<string>(), It.IsAny<IDictionary<string, object>>()),
            Times.Never);

    [Test]
    public async Task HandleAsync_ForARelease_OpensThatSongInThePlayer()
    {
        await _router.HandleAsync(ReleasePayload());

        VerifyNavigatedToSong();
    }

    [Test]
    public async Task HandleAsync_ForAnArtistMessage_StaysWhereItLanded()
    {
        // There is no Artist Messages page yet. Home is the current destination and remains correct
        // until there is somewhere better to go.
        await _router.HandleAsync(new Dictionary<string, string?>
        {
            [PushDataKeys.Kind] = PushNotificationKinds.ArtistMessage,
            [PushDataKeys.EntityId] = "9",
        });

        VerifyNoNavigation();
    }

    [TestCase("")]
    [TestCase("not-a-number")]
    public async Task HandleAsync_WithAnUnusableSongId_DoesNotNavigate(string songId)
    {
        await _router.HandleAsync(ReleasePayload(songId));

        VerifyNoNavigation();
    }

    [Test]
    public async Task HandleAsync_WhenTheSongIsNoLongerInTheCatalogue_DoesNotNavigate()
    {
        // Withdrawn between the push being sent and the tap, or an offline snapshot without it.
        _musicService.Setup(x => x.GetSongsAsync()).ReturnsAsync([]);

        await _router.HandleAsync(ReleasePayload());

        VerifyNoNavigation();
    }

    [TestCase(null)]
    [TestCase(0)]
    public async Task HandleAsync_WithNothingToRouteOn_DoesNotNavigate(int? emptiness)
    {
        await _router.HandleAsync(emptiness is null ? null : new Dictionary<string, string?>());

        VerifyNoNavigation();
    }

    [Test]
    public async Task HandleAsync_WhenNavigationThrows_DoesNotPropagate()
    {
        // This runs during launch on a cold-start tap, where an escaping exception is a crash the
        // user sees as the app dying when they touched a notification.
        _navigation
            .Setup(x => x.GoToAsync(It.IsAny<string>(), It.IsAny<IDictionary<string, object>>()))
            .ThrowsAsync(new InvalidOperationException("Shell is not ready"));

        Assert.DoesNotThrowAsync(() => _router.HandleAsync(ReleasePayload()));
        await Task.CompletedTask;
    }

    [Test]
    public async Task FlushPendingAsync_ReplaysATapThatArrivedBeforeTheAppCouldNavigate()
    {
        _router.QueuePending(ReleasePayload());

        VerifyNoNavigation();

        await _router.FlushPendingAsync();

        VerifyNavigatedToSong();
    }

    [Test]
    public async Task FlushPendingAsync_WithNothingQueued_DoesNothing()
    {
        // Every app activation calls this, so the empty case is the common one.
        await _router.FlushPendingAsync();

        VerifyNoNavigation();
        _musicService.Verify(x => x.GetSongsAsync(), Times.Never);
    }

    [Test]
    public async Task FlushPendingAsync_TwiceOverOneTap_NavigatesOnlyOnce()
    {
        _router.QueuePending(ReleasePayload());

        await _router.FlushPendingAsync();
        await _router.FlushPendingAsync();

        VerifyNavigatedToSong();
    }

    [Test]
    public async Task QueuePending_TwiceBeforeAFlush_HonoursTheTapTheUserActuallyMade()
    {
        // Last tap wins: two notifications opened before the app is up is not a queue worth
        // keeping, and replaying the older one would take them somewhere they did not ask for.
        var second = new SongDto { Id = 99, SongTitle = "Second Song" };
        _musicService.Setup(x => x.GetSongsAsync()).ReturnsAsync([Song, second]);

        _router.QueuePending(ReleasePayload());
        _router.QueuePending(ReleasePayload("99"));

        await _router.FlushPendingAsync();

        _navigation.Verify(
            x => x.GoToAsync(
                NavigationRoutes.SongPlayer,
                It.Is<IDictionary<string, object>>(p => ReferenceEquals(p["Song"], second))),
            Times.Once);
    }

    // --- Digests: the destination has to match what the notification actually said ---

    private static Dictionary<string, string?> DigestPayload(string? artistName, int count = 3)
    {
        var data = new Dictionary<string, string?>
        {
            [PushDataKeys.Kind] = PushNotificationKinds.Digest,
            [PushDataKeys.Count] = count.ToString(),
        };

        if (artistName is not null)
        {
            data[PushDataKeys.ArtistName] = artistName;
            data[PushDataKeys.PersonaId] = "7";
        }

        return data;
    }

    [Test]
    public async Task HandleAsync_ForASingleArtistDigest_OpensThatArtist()
    {
        await _router.HandleAsync(DigestPayload("Alex Rivers"));

        _navigation.Verify(
            x => x.GoToAsync(
                NavigationRoutes.PlaylistPlayer,
                It.Is<IDictionary<string, object>>(p => (string)p["ArtistName"] == "Alex Rivers")),
            Times.Once);
    }

    [Test]
    public async Task HandleAsync_ForADigestSpanningArtists_StaysOnHome()
    {
        // "4 new updates from 3 artists you follow" names no destination, so opening one would be
        // taking the user somewhere the notification did not offer.
        await _router.HandleAsync(DigestPayload(artistName: null));

        VerifyNoNavigation();
    }

    [Test]
    public async Task HandleAsync_ForADigest_NeverNeedsTheCatalogue()
    {
        // Deliberate: a digest arrives with the artist name on it so a cold-start tap works with
        // no network, unlike a release which has to resolve its SongDto.
        await _router.HandleAsync(DigestPayload("Alex Rivers"));

        _musicService.Verify(x => x.GetSongsAsync(), Times.Never);
    }
}
