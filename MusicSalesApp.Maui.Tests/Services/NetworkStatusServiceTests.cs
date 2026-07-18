using Microsoft.Maui.Networking;
using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.Tests.Services;

[TestFixture]
public class NetworkStatusServiceTests
{
    [TestCase(NetworkAccess.Internet, false)]
    [TestCase(NetworkAccess.None, true)]
    [TestCase(NetworkAccess.Local, true)]
    [TestCase(NetworkAccess.ConstrainedInternet, true)]
    public void Constructor_MapsCurrentNetworkAccess(NetworkAccess networkAccess, bool expectedOffline)
    {
        var connectivity = new TestConnectivity { NetworkAccess = networkAccess };

        using var service = new NetworkStatusService(connectivity);

        Assert.That(service.IsOffline, Is.EqualTo(expectedOffline));
    }

    [Test]
    public void ConnectivityChanged_UpdatesOfflineStateInBothDirections()
    {
        var connectivity = new TestConnectivity { NetworkAccess = NetworkAccess.Internet };
        using var service = new NetworkStatusService(connectivity);
        var observedStates = new List<bool>();
        service.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(NetworkStatusService.IsOffline))
            {
                observedStates.Add(service.IsOffline);
            }
        };

        connectivity.NetworkAccess = NetworkAccess.None;
        connectivity.RaiseConnectivityChanged();
        connectivity.NetworkAccess = NetworkAccess.Internet;
        connectivity.RaiseConnectivityChanged();

        Assert.That(observedStates, Is.EqualTo(new[] { true, false }));
    }

    [Test]
    public void Dispose_StopsConnectivityUpdates()
    {
        var connectivity = new TestConnectivity { NetworkAccess = NetworkAccess.Internet };
        var service = new NetworkStatusService(connectivity);

        service.Dispose();
        connectivity.NetworkAccess = NetworkAccess.None;
        connectivity.RaiseConnectivityChanged();

        Assert.That(service.IsOffline, Is.False);
    }
}
