using Microsoft.Extensions.Logging;
using Moq;
using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Tests.Services;

/// <summary>
/// The rules that keep every bell for one artist saying the same thing.
/// </summary>
[TestFixture]
public class ArtistFollowStateCoordinatorTests
{
    private Mock<IFollowService> _followService;
    private ArtistFollowNotifier _notifier;
    private ArtistFollowStateCoordinator _coordinator;

    [SetUp]
    public void SetUp()
    {
        _followService = new Mock<IFollowService>();

        // The real notifier, not a mock: the coordinator subscribing to it and re-raising in the
        // right order is the behaviour under test, and a mock would assert the wiring away.
        _notifier = new ArtistFollowNotifier();

        _coordinator = new ArtistFollowStateCoordinator(
            _followService.Object,
            _notifier,
            Mock.Of<ILogger<ArtistFollowStateCoordinator>>());
    }

    private static SongDto Song(int id, int? personaId) =>
        new() { Id = id, SongTitle = $"Song {id}", PersonaId = personaId };

    [Test]
    public async Task LoadFor_ResolvesEveryArtistInOneCall()
    {
        // One request for the page, not one per card. The library renders dozens of cards and many
        // of them share an artist.
        var songs = new[] { Song(1, 10), Song(2, 10), Song(3, 11), Song(4, null) };

        _followService
            .Setup(s => s.GetFollowedPersonaIdsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync([10]);

        await _coordinator.LoadForAsync(songs);

        _followService.Verify(
            s => s.GetFollowedPersonaIdsAsync(It.IsAny<IEnumerable<int>>()), Times.Once);

        Assert.Multiple(() =>
        {
            Assert.That(songs[0].IsFollowingArtist, Is.True);
            Assert.That(songs[1].IsFollowingArtist, Is.True, "both cards for persona 10 agree");
            Assert.That(songs[2].IsFollowingArtist, Is.False);
            Assert.That(songs[3].IsFollowingArtist, Is.False, "a song with no artist entity");
        });
    }

    [Test]
    public async Task LoadFor_AsksAboutEachArtistOnceEvenWhenManySongsShareOne()
    {
        var songs = new[] { Song(1, 10), Song(2, 10), Song(3, 10) };
        List<int>? asked = null;

        _followService
            .Setup(s => s.GetFollowedPersonaIdsAsync(It.IsAny<IEnumerable<int>>()))
            .Callback<IEnumerable<int>>(ids => asked = ids.ToList())
            .ReturnsAsync([]);

        await _coordinator.LoadForAsync(songs);

        Assert.That(asked, Is.EqualTo(new[] { 10 }));
    }

    [Test]
    public async Task Toggle_MovesEveryOtherCardForTheSameArtist()
    {
        // The whole reason this type exists. Following from one card has to move the rest, or the
        // page contradicts itself.
        var onScreen = new[] { Song(1, 10), Song(2, 10), Song(3, 11) };

        _followService
            .Setup(s => s.GetFollowedPersonaIdsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync([]);
        await _coordinator.LoadForAsync(onScreen);

        // A page re-applies on the coordinator's event, which is what the ViewModels do.
        _coordinator.FollowStateChanged += (_, _) => _coordinator.ApplyKnownState(onScreen);

        _followService
            .Setup(s => s.SetFollowStateAsync(10, true, It.IsAny<int?>()))
            .ReturnsAsync(new FollowStateResult(10, true, "Followed"))
            .Callback<int, bool, int?>((personaId, following, _) =>
                _notifier.NotifyFollowStateChanged(personaId, following));

        await _coordinator.ToggleAsync(onScreen[0]);

        Assert.Multiple(() =>
        {
            Assert.That(onScreen[0].IsFollowingArtist, Is.True);
            Assert.That(onScreen[1].IsFollowingArtist, Is.True, "the sibling card has to move too");
            Assert.That(onScreen[2].IsFollowingArtist, Is.False, "a different artist is untouched");
        });
    }

    [Test]
    public async Task Toggle_PutsTheBellBackWhenTheServerRefuses()
    {
        // Every domain refusal is a 400 by contract - following yourself, a blocked artist - so a
        // null result is a normal answer, not an exception. The bell must not keep a state the
        // server rejected.
        var song = Song(1, 10);

        _followService
            .Setup(s => s.SetFollowStateAsync(10, true, It.IsAny<int?>()))
            .ReturnsAsync((FollowStateResult?)null);

        var stuck = await _coordinator.ToggleAsync(song);

        Assert.Multiple(() =>
        {
            Assert.That(stuck, Is.False);
            Assert.That(song.IsFollowingArtist, Is.False);
        });
    }

    [Test]
    public async Task Toggle_SendsTheSongAsTheSourceSoTheArtistLearnsWhatEarnedTheFollow()
    {
        // The creator dashboard's "top songs generating follows" is built from this.
        var song = Song(42, 10);

        _followService
            .Setup(s => s.SetFollowStateAsync(10, true, 42))
            .ReturnsAsync(new FollowStateResult(10, true, "Followed"));

        await _coordinator.ToggleAsync(song);

        _followService.Verify(s => s.SetFollowStateAsync(10, true, 42), Times.Once);
    }

    [Test]
    public async Task Toggle_IgnoresASongWithNoArtistEntity()
    {
        // A song whose artist is only free text has nothing to follow. The bell is hidden for it,
        // so this only guards against a stale binding - but it must never reach the server.
        var song = Song(1, null);

        var stuck = await _coordinator.ToggleAsync(song);

        Assert.That(stuck, Is.False);
        _followService.Verify(
            s => s.SetFollowStateAsync(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<int?>()),
            Times.Never);
    }

    [Test]
    public async Task LoadFor_KeepsWhatItLearnedAboutArtistsOnOtherPages()
    {
        // The coordinator is a singleton and outlives any one page. A library load that asks only
        // about its own artists must not forget the one the user followed on the home page.
        _followService
            .Setup(s => s.GetFollowedPersonaIdsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync([10]);
        await _coordinator.LoadForAsync([Song(1, 10)]);

        _followService
            .Setup(s => s.GetFollowedPersonaIdsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync([]);
        await _coordinator.LoadForAsync([Song(2, 11)]);

        var backOnTheFirstPage = Song(3, 10);
        _coordinator.ApplyKnownState([backOnTheFirstPage]);

        Assert.That(backOnTheFirstPage.IsFollowingArtist, Is.True);
    }

    [Test]
    public async Task LoadFor_ClearsAnArtistTheServerNoLongerReports()
    {
        // The other half: an unfollow made on the website has to land here on the next load, or the
        // app shows a filled bell over a follow that no longer exists.
        _followService
            .Setup(s => s.GetFollowedPersonaIdsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync([10]);
        await _coordinator.LoadForAsync([Song(1, 10)]);

        _followService
            .Setup(s => s.GetFollowedPersonaIdsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync([]);

        var song = Song(2, 10);
        await _coordinator.LoadForAsync([song]);

        Assert.That(song.IsFollowingArtist, Is.False);
    }
}
