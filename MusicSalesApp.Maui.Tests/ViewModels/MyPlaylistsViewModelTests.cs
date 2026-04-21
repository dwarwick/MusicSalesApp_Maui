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

        _mockPlaylist.Setup(s => s.GetMyPlaylistsAsync()).ReturnsAsync([]);
    }

    private MyPlaylistsViewModel CreateVm() =>
        new(_mockPlaylist.Object, _mockAuth.Object, _mockAlert.Object, _mockNav.Object);

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
