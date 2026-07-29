using Moq;
using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.Tests.Services;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Tests.ViewModels;

/// <summary>
/// Every ViewModel that hosts a server-only control (report a song, tip a creator, add to playlist,
/// edit a playlist) must hide it while offline rather than let it fail on tap.
/// </summary>
[TestFixture]
public class OfflineServerActionVisibilityTests
{
    private TestNetworkStatusService _networkStatus = null!;

    [SetUp]
    public void SetUp() => _networkStatus = new TestNetworkStatusService();

    private SongPlayerViewModel CreateSongPlayerViewModel() => new(
        new Mock<IMusicService>().Object,
        new Mock<IAlertService>().Object,
        new Mock<IAuthService>().Object,
        new Mock<INavigationService>().Object,
        new Mock<IPlaybackService>().Object,
        new Mock<IMediaPlaybackOnboardingService>().Object,
        new Mock<ISignalRService>().Object,
        new Mock<IAppConfig>().Object,
        new Mock<IBillingService>().Object,
        _networkStatus);

    private MyPlaylistsViewModel CreateMyPlaylistsViewModel() => new(
        new Mock<IPlaylistService>().Object,
        new Mock<IAuthService>().Object,
        new Mock<IAlertService>().Object,
        new Mock<INavigationService>().Object,
        _networkStatus);

    private PlaylistPlayerViewModel CreatePlaylistPlayerViewModel() => new(
        new Mock<IMusicService>().Object,
        new Mock<IAlertService>().Object,
        new Mock<IAuthService>().Object,
        new Mock<INavigationService>().Object,
        new Mock<IPlaybackService>().Object,
        new Mock<IMediaPlaybackOnboardingService>().Object,
        new Mock<ISignalRService>().Object,
        new Mock<IAppConfig>().Object,
        new Mock<IBillingService>().Object,
        new Mock<IPlaylistService>().Object,
        _networkStatus);

    // --- SongPlayerViewModel (report button) ---

    [Test]
    public void SongPlayer_CanUseServerActions_TracksConnectivity()
    {
        var viewModel = CreateSongPlayerViewModel();
        Assert.That(viewModel.CanUseServerActions, Is.True);

        _networkStatus.SetOffline(true);

        Assert.That(viewModel.CanUseServerActions, Is.False);
    }

    [Test]
    public void SongPlayer_RaisesPropertyChangedWhenConnectivityFlips()
    {
        var viewModel = CreateSongPlayerViewModel();
        var raised = new List<string?>();
        viewModel.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        _networkStatus.SetOffline(true);

        Assert.That(raised, Does.Contain(nameof(SongPlayerViewModel.CanUseServerActions)));
    }

    // --- MyPlaylistsViewModel (create / rename / delete) ---

    [Test]
    public void MyPlaylists_ActivateAfterCleanup_ResubscribesToConnectivity()
    {
        // The page can be navigated away from and back to on the same ViewModel instance, so the
        // subscription has to be re-attachable rather than one-shot.
        var viewModel = CreateMyPlaylistsViewModel();
        viewModel.Cleanup();
        viewModel.Activate();
        var raised = new List<string?>();
        viewModel.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        _networkStatus.SetOffline(true);

        Assert.That(raised, Does.Contain(nameof(MyPlaylistsViewModel.CanUseServerActions)));
    }

    [Test]
    public void MyPlaylists_AfterCleanup_StopsReactingToConnectivity()
    {
        var viewModel = CreateMyPlaylistsViewModel();
        viewModel.Cleanup();
        var raised = new List<string?>();
        viewModel.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        _networkStatus.SetOffline(true);

        Assert.That(raised, Is.Empty);
    }

    [Test]
    public void MyPlaylists_CanUseServerActions_TracksConnectivity()
    {
        var viewModel = CreateMyPlaylistsViewModel();
        Assert.That(viewModel.CanUseServerActions, Is.True);

        _networkStatus.SetOffline(true);

        Assert.That(viewModel.CanUseServerActions, Is.False);
    }

    [Test]
    public void MyPlaylists_OfflineEmptyState_DoesNotClaimTheUserHasNoPlaylists()
    {
        // Offline, an empty list means the server was unreachable - saying "no playlists yet" reads as
        // if the user's playlists had been deleted.
        var viewModel = CreateMyPlaylistsViewModel();
        Assert.That(viewModel.EmptyStateTitle, Is.EqualTo("No playlists yet"));

        _networkStatus.SetOffline(true);

        Assert.That(viewModel.EmptyStateTitle, Is.EqualTo("You're offline"));
        Assert.That(viewModel.EmptyStateDetail, Does.Contain("reconnect").IgnoreCase);
    }

    // --- PlaylistPlayerViewModel (remove track / reorder) ---

    [Test]
    public void PlaylistPlayer_CanEditPlaylist_RequiresBothOwnershipAndAConnection()
    {
        var viewModel = CreatePlaylistPlayerViewModel();
        viewModel.IsUserPlaylist = true;
        Assert.That(viewModel.CanEditPlaylist, Is.True);

        _networkStatus.SetOffline(true);

        Assert.That(viewModel.CanEditPlaylist, Is.False);
    }

    [Test]
    public void PlaylistPlayer_CanEditPlaylist_StaysFalseForSystemPlaylists()
    {
        var viewModel = CreatePlaylistPlayerViewModel();
        viewModel.IsUserPlaylist = false;

        Assert.That(viewModel.CanEditPlaylist, Is.False);
    }

    [Test]
    public void PlaylistPlayer_IsReorderEnabled_RequiresASubscriptionAndAConnection()
    {
        var viewModel = CreatePlaylistPlayerViewModel();
        viewModel.IsUserPlaylist = true;
        viewModel.HasActiveSubscription = true;
        Assert.That(viewModel.IsReorderEnabled, Is.True);

        _networkStatus.SetOffline(true);

        Assert.That(viewModel.IsReorderEnabled, Is.False);
    }

    [Test]
    public void PlaylistPlayer_ShowsAnOfflineEditingNoticeOnlyForOwnedPlaylists()
    {
        // IsUserPlaylist is set after the connectivity flip on purpose: the flip also kicks off a
        // reload, which re-derives IsUserPlaylist from the reloaded playlist.
        var viewModel = CreatePlaylistPlayerViewModel();
        _networkStatus.SetOffline(true);

        viewModel.IsUserPlaylist = true;
        Assert.That(viewModel.ShowOfflineEditingNotice, Is.True);

        viewModel.IsUserPlaylist = false;
        Assert.That(viewModel.ShowOfflineEditingNotice, Is.False);
    }

    [Test]
    public void PlaylistPlayer_ShowsNoOfflineEditingNoticeWhileOnline()
    {
        var viewModel = CreatePlaylistPlayerViewModel();

        viewModel.IsUserPlaylist = true;

        Assert.That(viewModel.ShowOfflineEditingNotice, Is.False);
    }

    [Test]
    public void PlaylistPlayer_RaisesPropertyChangedForEveryOfflineDependentProperty()
    {
        var viewModel = CreatePlaylistPlayerViewModel();
        var raised = new List<string?>();
        viewModel.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        _networkStatus.SetOffline(true);

        Assert.Multiple(() =>
        {
            Assert.That(raised, Does.Contain(nameof(PlaylistPlayerViewModel.CanUseServerActions)));
            Assert.That(raised, Does.Contain(nameof(PlaylistPlayerViewModel.CanEditPlaylist)));
            Assert.That(raised, Does.Contain(nameof(PlaylistPlayerViewModel.IsReorderEnabled)));
            Assert.That(raised, Does.Contain(nameof(PlaylistPlayerViewModel.ShowOfflineEditingNotice)));
        });
    }

    // --- A constrained connection is not "no network" ---

    [Test]
    public void SongPlayer_OnAConstrainedConnection_KeepsServerActionsAvailable()
    {
        // IsOffline is "not Internet", which also covers ConstrainedInternet and Unknown. Hiding the
        // controls on those would take away features that still work, so the gates use
        // HasNoNetworkAccess instead - the same check the service layer makes.
        var viewModel = CreateSongPlayerViewModel();

        _networkStatus.SetConstrained();

        Assert.Multiple(() =>
        {
            Assert.That(_networkStatus.IsOffline, Is.True, "the banner-level flag is still pessimistic");
            Assert.That(viewModel.CanUseServerActions, Is.True);
        });
    }

    [Test]
    public void MyPlaylists_OnAConstrainedConnection_KeepsServerActionsAvailable()
    {
        var viewModel = CreateMyPlaylistsViewModel();

        _networkStatus.SetConstrained();

        Assert.That(viewModel.CanUseServerActions, Is.True);
    }

    [Test]
    public void PlaylistPlayer_OnAConstrainedConnection_StaysEditable()
    {
        // IsUserPlaylist is set after the flip on purpose: the flip also kicks off a reload, which
        // re-derives IsUserPlaylist from the reloaded playlist.
        var viewModel = CreatePlaylistPlayerViewModel();
        _networkStatus.SetConstrained();

        viewModel.IsUserPlaylist = true;

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.CanEditPlaylist, Is.True);
            Assert.That(viewModel.ShowOfflineEditingNotice, Is.False);
        });
    }

    [Test]
    public void PlaylistPlayer_LosingAccessAfterAConstrainedConnection_StillHidesTheControls()
    {
        // IsOffline never changes across this transition, so a subscriber filtering only on that
        // property name would miss it and leave the controls visible with no network.
        var viewModel = CreatePlaylistPlayerViewModel();
        _networkStatus.SetConstrained();
        viewModel.IsUserPlaylist = true;

        _networkStatus.SetOffline(true);

        Assert.That(viewModel.CanUseServerActions, Is.False);
    }

    [Test]
    public void SongPlayer_RegainingAccessFromAirplaneModeToAConstrainedConnection_RestoresTheControls()
    {
        var viewModel = CreateSongPlayerViewModel();
        _networkStatus.SetOffline(true);
        Assert.That(viewModel.CanUseServerActions, Is.False);

        _networkStatus.SetConstrained();

        Assert.That(viewModel.CanUseServerActions, Is.True);
    }

    // --- HomeViewModel (report button) is covered here too, via its required NetworkStatus dependency ---

    [Test]
    public void ViewModelsWithoutANetworkStatusService_DefaultToAllowingServerActions()
    {
        // Trailing-optional injection means a test or call site that omits it keeps today's behaviour.
        var songPlayer = new SongPlayerViewModel(
            new Mock<IMusicService>().Object,
            new Mock<IAlertService>().Object,
            new Mock<IAuthService>().Object,
            new Mock<INavigationService>().Object,
            new Mock<IPlaybackService>().Object,
            new Mock<IMediaPlaybackOnboardingService>().Object,
            new Mock<ISignalRService>().Object,
            new Mock<IAppConfig>().Object,
            new Mock<IBillingService>().Object);

        Assert.That(songPlayer.CanUseServerActions, Is.True);
    }
}
