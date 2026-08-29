using Moq;
using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.ViewModels;
using NUnit.Framework;

namespace MusicSalesApp.Maui.Tests.ViewModels;

[TestFixture]
public class MyPlaylistsViewModelTests
{
    private Mock<IPlaylistService> _mockPlaylist = null!;
    private Mock<IAuthService> _mockAuth = null!;
    private Mock<IAlertService> _mockAlert = null!;
    private Mock<INavigationService> _mockNav = null!;

    [SetUp]
    public void SetUp()
    {
        _mockPlaylist = new Mock<IPlaylistService>();
        _mockAuth = new Mock<IAuthService>();
        _mockAlert = new Mock<IAlertService>();
        _mockNav = new Mock<INavigationService>();

        // Signed in and verified is the only state that reaches this page: the flyout hides it
        // otherwise. So that is the default, and the /home payload is where both generated sections
        // come from.
        _mockAuth.SetupGet(a => a.IsLoggedIn).Returns(true);
        _mockAuth.SetupGet(a => a.EmailConfirmed).Returns(true);

        _mockPlaylist.Setup(s => s.GetMyPlaylistsAsync()).ReturnsAsync([]);
        _mockPlaylist.Setup(s => s.GetHomePlaylistsAsync()).ReturnsAsync(new HomePlaylistsDto());
        _mockPlaylist.Setup(s => s.GetTopStreamedPlaylistsAsync()).ReturnsAsync([]);
    }

    /// <summary>
    /// Sets the /home payload the page reads when signed in. Recommended is synthesised per request
    /// and only exists here - GET api/mobile/playlists returns real rows, so it can never carry it.
    /// </summary>
    private void SetUpHome(PlaylistDto? recommended, params PlaylistDto[] topStreamed) =>
        _mockPlaylist.Setup(s => s.GetHomePlaylistsAsync()).ReturnsAsync(new HomePlaylistsDto
        {
            Recommended = recommended,
            TopStreamed = [.. topStreamed]
        });

    private static PlaylistDto RecommendedTile(int songCount = 25) => new()
    {
        Id = 0,
        Name = "Recommended For You",
        SongCount = songCount,
        IsSystemGenerated = true,
        Kind = PlaylistKinds.Recommended
    };

    private MyPlaylistsViewModel CreateVm() =>
        new(_mockPlaylist.Object, _mockAuth.Object, _mockAlert.Object, _mockNav.Object);

    private static PlaylistDto TopStreamedTile(string window) => new()
    {
        Id = 0,
        Key = window,
        Name = $"Top 10 {window}",
        SongCount = 10,
        IsSystemGenerated = true,
        Kind = PlaylistKinds.TopStreamed
    };

    // ---- The global "most streamed" playlists -------------------------------------

    [Test]
    public async Task LoadAsync_PopulatesTheMostStreamedPlaylistsInServerOrder()
    {
        SetUpHome(null, TopStreamedTile("Day"), TopStreamedTile("Week"), TopStreamedTile("AllTime"));

        var vm = CreateVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(vm.TopStreamedPlaylists.Select(p => p.Key),
                Is.EqualTo(new[] { "Day", "Week", "AllTime" }),
                "The server dictates the order; the ViewModel must not re-sort.");
            Assert.That(vm.ShowTopStreamed, Is.True);
        });
    }

    [Test]
    public async Task LoadAsync_KeepsTheMostStreamedPlaylistsOutOfTheUsersOwnList()
    {
        // They have no id and must never get the rename/delete affordances, which are bound against
        // the Playlists collection.
        _mockPlaylist.Setup(s => s.GetMyPlaylistsAsync()).ReturnsAsync(
            [new PlaylistDto { Id = 1, Name = "Rock", Kind = PlaylistKinds.Custom }]);
        SetUpHome(null, TopStreamedTile("Day"));

        var vm = CreateVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(vm.Playlists, Has.Count.EqualTo(1));
            Assert.That(vm.Playlists.Any(p => p.Kind == PlaylistKinds.TopStreamed), Is.False);
            Assert.That(vm.TopStreamedPlaylists, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task TheMostStreamedPlaylistsDoNotSuppressTheEmptyState()
    {
        // A user with none of their own playlists must still be told to create one, even though the
        // page is no longer visually empty.
        SetUpHome(null, TopStreamedTile("Day"));

        var vm = CreateVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(vm.ShowEmptyState, Is.True);
            Assert.That(vm.ShowTopStreamed, Is.True);
        });
    }

    [Test]
    public async Task OpenPlaylistAsync_OpensAMostStreamedPlaylistByWindowNotById()
    {
        // All five report Id = 0, so navigating on the id would send every one to the same page.
        var vm = CreateVm();

        await vm.OpenPlaylistCommand.ExecuteAsync(TopStreamedTile("Month"));

        _mockNav.Verify(n => n.GoToAsync(
            NavigationRoutes.PlaylistPlayer,
            It.Is<Dictionary<string, object>>(query =>
                (string)query[PlaylistNavigationTarget.TopStreamedWindowKey] == "Month"
                && !query.ContainsKey(PlaylistNavigationTarget.PlaylistIdKey))),
            Times.Once);
    }

    [Test]
    public async Task ShowTopStreamed_IsFalseWhenTheServerReturnsNone()
    {
        var vm = CreateVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.That(vm.ShowTopStreamed, Is.False);
    }

    // ---- Recommended For You -----------------------------------------------------

    [Test]
    public async Task LoadAsync_PopulatesRecommendedFromTheHomePayload()
    {
        SetUpHome(RecommendedTile(songCount: 25));

        var vm = CreateVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(vm.RecommendedPlaylist?.SongCount, Is.EqualTo(25));
            Assert.That(vm.ShowRecommended, Is.True);
        });
    }

    [Test]
    public async Task LoadAsync_ReadsBothGeneratedSectionsFromASingleHomeRequest()
    {
        // The most-streamed tiles ride along in the /home response, so asking the anonymous endpoint
        // as well would duplicate work the server has already done.
        SetUpHome(RecommendedTile(), TopStreamedTile("Day"));

        var vm = CreateVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.That(vm.TopStreamedPlaylists, Has.Count.EqualTo(1));
        _mockPlaylist.Verify(s => s.GetTopStreamedPlaylistsAsync(), Times.Never);
    }

    [Test]
    public async Task LoadAsync_KeepsRecommendedOutOfTheUsersOwnList()
    {
        // It has no Playlists row, so the rename and delete affordances bound against that collection
        // must never reach it.
        SetUpHome(RecommendedTile());

        var vm = CreateVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.That(vm.Playlists, Is.Empty);
    }

    [Test]
    public async Task ShowRecommended_IsFalseWhenTheServerOmitsIt()
    {
        // The server sends nothing when the list has no playable songs. That is the normal empty
        // case, not a failure.
        var vm = CreateVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(vm.RecommendedPlaylist, Is.Null);
            Assert.That(vm.ShowRecommended, Is.False);
        });
    }

    [Test]
    public async Task OpenPlaylistAsync_OpensRecommendedByUserIdNotById()
    {
        // Recommended reports Id = 0, the same as every most-streamed tile, so navigating on the id
        // would send it to the wrong page.
        _mockAuth.SetupGet(a => a.UserId).Returns(42);
        var vm = CreateVm();

        await vm.OpenPlaylistCommand.ExecuteAsync(RecommendedTile());

        _mockNav.Verify(n => n.GoToAsync(
            NavigationRoutes.PlaylistPlayer,
            It.Is<Dictionary<string, object>>(query =>
                (string)query[PlaylistNavigationTarget.RecommendedUserIdKey] == "42"
                && !query.ContainsKey(PlaylistNavigationTarget.PlaylistIdKey))),
            Times.Once);
    }

    [Test]
    public async Task LoadAsync_ExpiredSession_StillShowsTheMostStreamedTiles()
    {
        // /home is authenticated and answers 401, but the most-streamed endpoint is anonymous, so
        // that section must survive a session that has lapsed under the user.
        _mockAuth.SetupGet(a => a.IsLoggedIn).Returns(false);
        _mockPlaylist.Setup(s => s.GetTopStreamedPlaylistsAsync()).ReturnsAsync([TopStreamedTile("Day")]);

        var vm = CreateVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(vm.TopStreamedPlaylists, Has.Count.EqualTo(1));
            Assert.That(vm.ShowRecommended, Is.False, "There is no personal list to show without a session.");
        });
        _mockPlaylist.Verify(s => s.GetHomePlaylistsAsync(), Times.Never);
    }

    [Test]
    public async Task ShowGeneratedPlaylists_IsFalseWhenNeitherSectionHasContent()
    {
        // The "My Playlists" heading hangs off this: with nothing above it, it labels nothing.
        var vm = CreateVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.That(vm.ShowGeneratedPlaylists, Is.False);
    }

    [Test]
    public async Task ShowGeneratedPlaylists_IsTrueWhenOnlyRecommendedHasContent()
    {
        SetUpHome(RecommendedTile());

        var vm = CreateVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.That(vm.ShowGeneratedPlaylists, Is.True);
    }

    [Test]
    public async Task LoadAsync_PopulatesPlaylistsAndSubscriptionFlag()
    {
        _mockAuth.SetupGet(a => a.HasActiveSubscription).Returns(true);
        _mockPlaylist.Setup(s => s.GetMyPlaylistsAsync()).ReturnsAsync(
        [
            new PlaylistDto { Id = 1, Name = "Rock", SongCount = 5, IsSystemGenerated = false, Kind = PlaylistKinds.Custom },
            new PlaylistDto { Id = 2, Name = "Liked Songs", SongCount = 3, IsSystemGenerated = true, Kind = PlaylistKinds.LikedSongs },
        ]);

        var vm = CreateVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.That(vm.Playlists, Has.Count.EqualTo(2));
        Assert.That(vm.HasActiveSubscription, Is.True);
        Assert.That(vm.IsLoading, Is.False);
        Assert.That(vm.ShowPlaylists, Is.True);
        Assert.That(vm.ShowEmptyState, Is.False);
    }

    [Test]
    public async Task LoadAsync_NoItems_SetsEmptyState()
    {
        var vm = CreateVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.That(vm.Playlists, Is.Empty);
        Assert.That(vm.ShowEmptyState, Is.True);
        Assert.That(vm.ShowPlaylists, Is.False);
    }

    [Test]
    public async Task CreatePlaylist_WithoutSubscription_ShowsAlertAndDoesNotCreate()
    {
        _mockAuth.SetupGet(a => a.HasActiveSubscription).Returns(false);
        var vm = CreateVm();
        await vm.LoadCommand.ExecuteAsync(null);

        await vm.CreatePlaylistCommand.ExecuteAsync(null);

        _mockAlert.Verify(a => a.DisplayAlertAsync("Subscription required", It.IsAny<string>(), "OK"), Times.Once);
        _mockPlaylist.Verify(p => p.CreatePlaylistAsync(It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task CreatePlaylist_CancelledPrompt_DoesNothing()
    {
        _mockAuth.SetupGet(a => a.HasActiveSubscription).Returns(true);
        _mockAlert.Setup(a => a.ShowPromptAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int>()))
            .ReturnsAsync((string?)null);

        var vm = CreateVm();
        await vm.LoadCommand.ExecuteAsync(null);

        await vm.CreatePlaylistCommand.ExecuteAsync(null);

        _mockPlaylist.Verify(p => p.CreatePlaylistAsync(It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task CreatePlaylist_Success_ReloadsPlaylists()
    {
        _mockAuth.SetupGet(a => a.HasActiveSubscription).Returns(true);
        _mockAlert.Setup(a => a.ShowPromptAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int>()))
            .ReturnsAsync("Workout");
        _mockPlaylist.Setup(p => p.CreatePlaylistAsync("Workout"))
            .ReturnsAsync(PlaylistOperationResult<PlaylistDto>.Ok(new PlaylistDto { Id = 7, Name = "Workout" }));

        var vm = CreateVm();
        await vm.LoadCommand.ExecuteAsync(null);

        await vm.CreatePlaylistCommand.ExecuteAsync(null);

        _mockPlaylist.Verify(p => p.CreatePlaylistAsync("Workout"), Times.Once);
        _mockPlaylist.Verify(p => p.GetMyPlaylistsAsync(), Times.Exactly(2));
    }

    [Test]
    public async Task CreatePlaylist_ServerRequiresSubscription_ShowsAlert()
    {
        _mockAuth.SetupGet(a => a.HasActiveSubscription).Returns(true);
        _mockAlert.Setup(a => a.ShowPromptAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int>()))
            .ReturnsAsync("Name");
        _mockPlaylist.Setup(p => p.CreatePlaylistAsync(It.IsAny<string>()))
            .ReturnsAsync(PlaylistOperationResult<PlaylistDto>.NeedsSubscription());

        var vm = CreateVm();
        await vm.LoadCommand.ExecuteAsync(null);

        await vm.CreatePlaylistCommand.ExecuteAsync(null);

        Assert.That(vm.HasActiveSubscription, Is.False);
        _mockAlert.Verify(a => a.DisplayAlertAsync("Subscription required", It.IsAny<string>(), "OK"), Times.Once);
    }

    [Test]
    public async Task RenamePlaylist_SystemGenerated_Ignored()
    {
        var vm = CreateVm();
        var system = new PlaylistDto { Id = 2, Name = "Liked", IsSystemGenerated = true };

        await vm.RenamePlaylistCommand.ExecuteAsync(system);

        _mockAlert.Verify(a => a.ShowPromptAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int>()), Times.Never);
        _mockPlaylist.Verify(p => p.RenamePlaylistAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task RenamePlaylist_Success_Reloads()
    {
        _mockAlert.Setup(a => a.ShowPromptAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int>()))
            .ReturnsAsync("New Name");
        _mockPlaylist.Setup(p => p.RenamePlaylistAsync(5, "New Name"))
            .ReturnsAsync(PlaylistOperationResult.Ok());

        var vm = CreateVm();
        var playlist = new PlaylistDto { Id = 5, Name = "Old Name", IsSystemGenerated = false };

        await vm.RenamePlaylistCommand.ExecuteAsync(playlist);

        _mockPlaylist.Verify(p => p.RenamePlaylistAsync(5, "New Name"), Times.Once);
        _mockPlaylist.Verify(p => p.GetMyPlaylistsAsync(), Times.Once);
    }

    [Test]
    public async Task DeletePlaylist_SystemGenerated_Ignored()
    {
        var vm = CreateVm();
        var system = new PlaylistDto { Id = 2, Name = "Liked", IsSystemGenerated = true };

        await vm.DeletePlaylistCommand.ExecuteAsync(system);

        _mockAlert.Verify(a => a.ShowConfirmAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _mockPlaylist.Verify(p => p.DeletePlaylistAsync(It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task DeletePlaylist_Confirmed_DeletesAndReloads()
    {
        _mockAlert.Setup(a => a.ShowConfirmAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);
        _mockPlaylist.Setup(p => p.DeletePlaylistAsync(9))
            .ReturnsAsync(PlaylistOperationResult.Ok());

        var vm = CreateVm();
        var playlist = new PlaylistDto { Id = 9, Name = "Rock", IsSystemGenerated = false };

        await vm.DeletePlaylistCommand.ExecuteAsync(playlist);

        _mockPlaylist.Verify(p => p.DeletePlaylistAsync(9), Times.Once);
        _mockPlaylist.Verify(p => p.GetMyPlaylistsAsync(), Times.Once);
    }

    [Test]
    public async Task DeletePlaylist_NotConfirmed_DoesNothing()
    {
        _mockAlert.Setup(a => a.ShowConfirmAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(false);

        var vm = CreateVm();
        var playlist = new PlaylistDto { Id = 9, Name = "Rock", IsSystemGenerated = false };

        await vm.DeletePlaylistCommand.ExecuteAsync(playlist);

        _mockPlaylist.Verify(p => p.DeletePlaylistAsync(It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task OpenPlaylist_NavigatesToPlaylistPlayer()
    {
        var vm = CreateVm();
        var playlist = new PlaylistDto { Id = 42, Name = "Mix" };

        await vm.OpenPlaylistCommand.ExecuteAsync(playlist);

        // Must be passed as string — Shell.ApplyQueryAttributes throws InvalidCastException
        // when assigning a non-string value to the target string? PlaylistIdParam property.
        _mockNav.Verify(n => n.GoToAsync("playlist-player",
            It.Is<IDictionary<string, object>>(d => (string)d["PlaylistId"] == "42")), Times.Once);
    }

    [Test]
    public async Task OpenPlaylist_Null_DoesNothing()
    {
        var vm = CreateVm();

        await vm.OpenPlaylistCommand.ExecuteAsync(null);

        _mockNav.Verify(n => n.GoToAsync(It.IsAny<string>(), It.IsAny<IDictionary<string, object>>()), Times.Never);
    }
}
