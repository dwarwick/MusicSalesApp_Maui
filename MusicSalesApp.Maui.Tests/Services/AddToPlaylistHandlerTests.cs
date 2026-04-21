using Moq;
using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Tests.Services;

[TestFixture]
public class AddToPlaylistHandlerTests
{
    private Mock<IAuthService> _auth = null!;
    private Mock<IPlaylistService> _playlists = null!;
    private Mock<IAlertService> _alerts = null!;
    private AddToPlaylistHandler _handler = null!;

    [SetUp]
    public void Setup()
    {
        _auth = new Mock<IAuthService>();
        _playlists = new Mock<IPlaylistService>();
        _alerts = new Mock<IAlertService>();
        _handler = new AddToPlaylistHandler(_auth.Object, _playlists.Object, _alerts.Object);

        _auth.SetupGet(a => a.IsLoggedIn).Returns(true);
        _auth.SetupGet(a => a.HasActiveSubscription).Returns(true);

        // Default: every playlist returns empty song list (song not yet added)
        _playlists.Setup(p => p.GetPlaylistSongsAsync(It.IsAny<int>()))
            .ReturnsAsync((int id) => new PlaylistSongsDto { PlaylistId = id, Songs = [] });
    }

    [Test]
    public async Task ShowAsync_WhenNotLoggedIn_ShowsLoginAlertAndSkipsRest()
    {
        _auth.SetupGet(a => a.IsLoggedIn).Returns(false);

        await _handler.ShowAsync(5, "Song");

        _alerts.Verify(a => a.DisplayAlertAsync(
            AddToPlaylistHandler.LoginTitle,
            AddToPlaylistHandler.LoginMessage,
            "OK"), Times.Once);
        _playlists.VerifyNoOtherCalls();
    }

    [Test]
    public async Task ShowAsync_WhenNotSubscribed_ShowsSubscribeAlert()
    {
        _auth.SetupGet(a => a.HasActiveSubscription).Returns(false);

        await _handler.ShowAsync(5, "Song");

        _alerts.Verify(a => a.DisplayAlertAsync(
            AddToPlaylistHandler.SubscribeTitle,
            AddToPlaylistHandler.SubscribeMessage,
            "OK"), Times.Once);
        _playlists.VerifyNoOtherCalls();
    }

    [Test]
    public async Task ShowAsync_InvalidSongId_DoesNothing()
    {
        await _handler.ShowAsync(0, "Song");

        _alerts.VerifyNoOtherCalls();
        _playlists.VerifyNoOtherCalls();
    }

    [Test]
    public async Task ShowAsync_Cancel_DoesNotAdd()
    {
        _playlists.Setup(p => p.GetMyPlaylistsAsync()).ReturnsAsync(
            [new PlaylistDto { Id = 1, Name = "Mix", IsSystemGenerated = false }]);
        _alerts.Setup(a => a.ShowActionSheetAsync(
                It.IsAny<string>(), "Cancel", null, It.IsAny<string[]>()))
            .ReturnsAsync("Cancel");

        await _handler.ShowAsync(5, "Song");

        _playlists.Verify(p => p.AddSongAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task ShowAsync_PickExistingPlaylist_CallsAddSong()
    {
        _playlists.Setup(p => p.GetMyPlaylistsAsync()).ReturnsAsync(
        [
            new PlaylistDto { Id = 1, Name = "Mix", IsSystemGenerated = false },
            new PlaylistDto { Id = 99, Name = "Liked Songs", IsSystemGenerated = true },
        ]);
        _alerts.Setup(a => a.ShowActionSheetAsync(
                It.IsAny<string>(), "Cancel", null, It.IsAny<string[]>()))
            .ReturnsAsync("Mix");
        _playlists.Setup(p => p.AddSongAsync(1, 5))
            .ReturnsAsync(PlaylistOperationResult.Ok());

        await _handler.ShowAsync(5, "Song");

        _playlists.Verify(p => p.AddSongAsync(1, 5), Times.Once);
        _alerts.Verify(a => a.DisplayAlertAsync(
            "Added to playlist",
            It.Is<string>(s => s.Contains("Song") && s.Contains("Mix")),
            "OK"), Times.Once);
    }

    [Test]
    public async Task ShowAsync_SystemPlaylists_AreFilteredOutOfOptions()
    {
        _playlists.Setup(p => p.GetMyPlaylistsAsync()).ReturnsAsync(
        [
            new PlaylistDto { Id = 1, Name = "Custom", IsSystemGenerated = false },
            new PlaylistDto { Id = 2, Name = "Liked Songs", IsSystemGenerated = true },
            new PlaylistDto { Id = 3, Name = "Recommended", IsSystemGenerated = true },
        ]);
        string[]? capturedButtons = null;
        _alerts.Setup(a => a.ShowActionSheetAsync(
                It.IsAny<string>(), "Cancel", null, It.IsAny<string[]>()))
            .Callback<string, string, string?, string[]>((_, _, _, b) => capturedButtons = b)
            .ReturnsAsync((string?)null);

        await _handler.ShowAsync(5, "Song");

        Assert.That(capturedButtons, Is.Not.Null);
        Assert.That(capturedButtons, Does.Contain("Custom"));
        Assert.That(capturedButtons, Does.Not.Contain("Liked Songs"));
        Assert.That(capturedButtons, Does.Not.Contain("Recommended"));
        Assert.That(capturedButtons, Does.Contain(AddToPlaylistHandler.NewPlaylistOption));
    }

    [Test]
    public async Task ShowAsync_NewPlaylist_CreatesAndAdds()
    {
        _playlists.Setup(p => p.GetMyPlaylistsAsync()).ReturnsAsync([]);
        _alerts.Setup(a => a.ShowActionSheetAsync(
                It.IsAny<string>(), "Cancel", null, It.IsAny<string[]>()))
            .ReturnsAsync(AddToPlaylistHandler.NewPlaylistOption);
        _alerts.Setup(a => a.ShowPromptAsync(
                "New playlist", It.IsAny<string>(), "Create", "Cancel",
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int>()))
            .ReturnsAsync("Road Trip");
        _playlists.Setup(p => p.CreatePlaylistAsync("Road Trip"))
            .ReturnsAsync(PlaylistOperationResult<PlaylistDto>.Ok(
                new PlaylistDto { Id = 42, Name = "Road Trip" }));
        _playlists.Setup(p => p.AddSongAsync(42, 5))
            .ReturnsAsync(PlaylistOperationResult.Ok());

        await _handler.ShowAsync(5, "Song");

        _playlists.Verify(p => p.CreatePlaylistAsync("Road Trip"), Times.Once);
        _playlists.Verify(p => p.AddSongAsync(42, 5), Times.Once);
    }

    [Test]
    public async Task ShowAsync_NewPlaylist_EmptyName_Aborts()
    {
        _playlists.Setup(p => p.GetMyPlaylistsAsync()).ReturnsAsync([]);
        _alerts.Setup(a => a.ShowActionSheetAsync(
                It.IsAny<string>(), "Cancel", null, It.IsAny<string[]>()))
            .ReturnsAsync(AddToPlaylistHandler.NewPlaylistOption);
        _alerts.Setup(a => a.ShowPromptAsync(
                "New playlist", It.IsAny<string>(), "Create", "Cancel",
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int>()))
            .ReturnsAsync("   ");

        await _handler.ShowAsync(5, "Song");

        _playlists.Verify(p => p.CreatePlaylistAsync(It.IsAny<string>()), Times.Never);
        _playlists.Verify(p => p.AddSongAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task ShowAsync_NewPlaylist_CreateFails_ShowsError()
    {
        _playlists.Setup(p => p.GetMyPlaylistsAsync()).ReturnsAsync([]);
        _alerts.Setup(a => a.ShowActionSheetAsync(
                It.IsAny<string>(), "Cancel", null, It.IsAny<string[]>()))
            .ReturnsAsync(AddToPlaylistHandler.NewPlaylistOption);
        _alerts.Setup(a => a.ShowPromptAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int>()))
            .ReturnsAsync("Road Trip");
        _playlists.Setup(p => p.CreatePlaylistAsync("Road Trip"))
            .ReturnsAsync(PlaylistOperationResult<PlaylistDto>.Fail("Name taken"));

        await _handler.ShowAsync(5, "Song");

        _alerts.Verify(a => a.DisplayAlertAsync(
            "Couldn't create playlist", "Name taken", "OK"), Times.Once);
        _playlists.Verify(p => p.AddSongAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task ShowAsync_AddFails_ShowsError()
    {
        _playlists.Setup(p => p.GetMyPlaylistsAsync()).ReturnsAsync(
            [new PlaylistDto { Id = 1, Name = "Mix", IsSystemGenerated = false }]);
        _alerts.Setup(a => a.ShowActionSheetAsync(
                It.IsAny<string>(), "Cancel", null, It.IsAny<string[]>()))
            .ReturnsAsync("Mix");
        _playlists.Setup(p => p.AddSongAsync(1, 5))
            .ReturnsAsync(PlaylistOperationResult.Fail("Already in playlist"));

        await _handler.ShowAsync(5, "Song");

        _alerts.Verify(a => a.DisplayAlertAsync(
            "Couldn't add song", "Already in playlist", "OK"), Times.Once);
    }

    [Test]
    public async Task ShowAsync_AddRequiresSubscription_ShowsSubscribeMessage()
    {
        _playlists.Setup(p => p.GetMyPlaylistsAsync()).ReturnsAsync(
            [new PlaylistDto { Id = 1, Name = "Mix", IsSystemGenerated = false }]);
        _alerts.Setup(a => a.ShowActionSheetAsync(
                It.IsAny<string>(), "Cancel", null, It.IsAny<string[]>()))
            .ReturnsAsync("Mix");
        _playlists.Setup(p => p.AddSongAsync(1, 5))
            .ReturnsAsync(PlaylistOperationResult.NeedsSubscription());

        await _handler.ShowAsync(5, "Song");

        _alerts.Verify(a => a.DisplayAlertAsync(
            "Couldn't add song",
            AddToPlaylistHandler.SubscribeMessage,
            "OK"), Times.Once);
    }

    [Test]
    public async Task ShowAsync_PlaylistAlreadyContainsSong_FiltersItOut()
    {
        _playlists.Setup(p => p.GetMyPlaylistsAsync()).ReturnsAsync(
        [
            new PlaylistDto { Id = 1, Name = "Has It", IsSystemGenerated = false },
            new PlaylistDto { Id = 2, Name = "Does Not", IsSystemGenerated = false },
        ]);
        _playlists.Setup(p => p.GetPlaylistSongsAsync(1)).ReturnsAsync(
            new PlaylistSongsDto { PlaylistId = 1, Songs = [new PlaylistSongDto { SongMetadataId = 5 }] });
        // playlist 2 uses the default (empty) mock from SetUp

        string[]? capturedButtons = null;
        _alerts.Setup(a => a.ShowActionSheetAsync(
                It.IsAny<string>(), "Cancel", null, It.IsAny<string[]>()))
            .Callback<string, string, string?, string[]>((_, _, _, b) => capturedButtons = b)
            .ReturnsAsync((string?)null);

        await _handler.ShowAsync(5, "Song");

        Assert.That(capturedButtons, Is.Not.Null);
        Assert.That(capturedButtons, Does.Not.Contain("Has It"));
        Assert.That(capturedButtons, Does.Contain("Does Not"));
        Assert.That(capturedButtons, Does.Contain(AddToPlaylistHandler.NewPlaylistOption));
    }
}
