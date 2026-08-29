using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Tests.ViewModels;

/// <summary>
/// How a playlist tile turns into a navigation.
/// </summary>
/// <remarks>
/// The reason this type exists is that <b>not every playlist can be opened by id</b>. Recommended and
/// the five "most streamed" playlists are generated server-side, have no row, and all report
/// <c>Id = 0</c> - so navigating on the id would send six different tiles to the same wrong page.
/// These tests pin that rule.
/// </remarks>
[TestFixture]
public class PlaylistNavigationTargetTests
{
    private const int UserId = 42;

    private static PlaylistDto Playlist(string kind, int id = 0, string? key = null) => new()
    {
        Id = id,
        Kind = kind,
        Key = key,
        Name = "A playlist"
    };

    [Test]
    public void ACustomPlaylistOpensById()
    {
        var target = PlaylistNavigationTarget.For(Playlist(PlaylistKinds.Custom, id: 17), UserId);

        Assert.That(target, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(target!.Value.Route, Is.EqualTo(NavigationRoutes.PlaylistPlayer));
            Assert.That(target.Value.Query[PlaylistNavigationTarget.PlaylistIdKey], Is.EqualTo("17"));
        });
    }

    [Test]
    public void LikedSongsOpensById()
    {
        var target = PlaylistNavigationTarget.For(Playlist(PlaylistKinds.LikedSongs, id: 3), UserId);

        Assert.That(target!.Value.Query[PlaylistNavigationTarget.PlaylistIdKey], Is.EqualTo("3"));
    }

    [Test]
    public void RecommendedOpensByUserIdBecauseItHasNoIdOfItsOwn()
    {
        var target = PlaylistNavigationTarget.For(Playlist(PlaylistKinds.Recommended), UserId);

        Assert.Multiple(() =>
        {
            Assert.That(target!.Value.Query.ContainsKey(PlaylistNavigationTarget.PlaylistIdKey), Is.False);
            Assert.That(target.Value.Query[PlaylistNavigationTarget.RecommendedUserIdKey], Is.EqualTo("42"));
        });
    }

    [Test]
    public void AMostStreamedPlaylistOpensByWindowKey()
    {
        var target = PlaylistNavigationTarget.For(Playlist(PlaylistKinds.TopStreamed, key: "Week"), UserId);

        Assert.Multiple(() =>
        {
            Assert.That(target!.Value.Query[PlaylistNavigationTarget.TopStreamedWindowKey], Is.EqualTo("Week"));
            Assert.That(target.Value.Query.ContainsKey(PlaylistNavigationTarget.PlaylistIdKey), Is.False,
                "Opening by id would send all five to playlist 0.");
        });
    }

    [Test]
    public void EveryMostStreamedPlaylistGetsItsOwnDestinationDespiteSharingIdZero()
    {
        // The bug this whole type guards against: five tiles, one id.
        string[] windows = ["Day", "Week", "Month", "Year", "AllTime"];

        var destinations = windows
            .Select(window => PlaylistNavigationTarget.For(Playlist(PlaylistKinds.TopStreamed, key: window), UserId))
            .Select(target => target!.Value.Query[PlaylistNavigationTarget.TopStreamedWindowKey])
            .ToList();

        Assert.That(destinations, Is.EquivalentTo(windows));
    }

    [Test]
    public void AMostStreamedPlaylistWithNoKeyGoesNowhere()
    {
        // Rather than falling through to id 0.
        Assert.That(PlaylistNavigationTarget.For(Playlist(PlaylistKinds.TopStreamed, key: null), UserId), Is.Null);
    }

    [Test]
    public void RecommendedGoesNowhereWhenSignedOut()
    {
        Assert.That(PlaylistNavigationTarget.For(Playlist(PlaylistKinds.Recommended), currentUserId: null), Is.Null);
    }

    [Test]
    public void AMostStreamedPlaylistStillOpensWhenSignedOut()
    {
        // These are not personal, so they must not need a user id the way Recommended does.
        var target = PlaylistNavigationTarget.For(Playlist(PlaylistKinds.TopStreamed, key: "Day"), currentUserId: null);

        Assert.That(target!.Value.Query[PlaylistNavigationTarget.TopStreamedWindowKey], Is.EqualTo("Day"));
    }

    [Test]
    public void AnUnknownKindWithNoIdGoesNowhere()
    {
        // A future server sending a generated list under a kind this build does not know about must
        // do nothing rather than open playlist 0.
        Assert.That(PlaylistNavigationTarget.For(Playlist("SomethingNew"), UserId), Is.Null);
    }

    [Test]
    public void NullGoesNowhere()
    {
        Assert.That(PlaylistNavigationTarget.For(null, UserId), Is.Null);
    }

    [Test]
    public void EveryValueCrossesAsAStringForShell()
    {
        // Shell.ApplyQueryAttributes does a direct cast for non-string values, and every target
        // property is string?, so a boxed int would throw at navigation time.
        PlaylistDto[] playlists =
        [
            Playlist(PlaylistKinds.Custom, id: 9),
            Playlist(PlaylistKinds.Recommended),
            Playlist(PlaylistKinds.TopStreamed, key: "Month")
        ];

        Assert.Multiple(() =>
        {
            foreach (var playlist in playlists)
            {
                var target = PlaylistNavigationTarget.For(playlist, UserId);
                Assert.That(target!.Value.Query.Values, Is.All.InstanceOf<string>(), playlist.Kind);
            }
        });
    }
}
