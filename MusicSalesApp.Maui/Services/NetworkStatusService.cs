using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Networking;

namespace MusicSalesApp.Maui.Services;

public sealed class NetworkStatusService : ObservableObject, INetworkStatusService, IDisposable
{
    private readonly IConnectivity _connectivity;
    private bool _isOffline;
    private bool _hasNoNetworkAccess;

    public NetworkStatusService(IConnectivity connectivity)
    {
        _connectivity = connectivity;
        _isOffline = IsNetworkUnavailable(connectivity.NetworkAccess);
        _hasNoNetworkAccess = HasNoAccess(connectivity.NetworkAccess);
        _connectivity.ConnectivityChanged += OnConnectivityChanged;
    }

    public bool IsOffline
    {
        get => _isOffline;
        private set => SetProperty(ref _isOffline, value);
    }

    public bool HasNoNetworkAccess
    {
        get => _hasNoNetworkAccess;
        private set => SetProperty(ref _hasNoNetworkAccess, value);
    }

    public void Dispose()
    {
        _connectivity.ConnectivityChanged -= OnConnectivityChanged;
    }

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        void UpdateState()
        {
            IsOffline = IsNetworkUnavailable(e.NetworkAccess);
            HasNoNetworkAccess = HasNoAccess(e.NetworkAccess);
        }

        try
        {
            if (MainThread.IsMainThread)
            {
                UpdateState();
                return;
            }

            MainThread.BeginInvokeOnMainThread(UpdateState);
        }
        catch (Exception ex) when (
            ex is NotImplementedException
            || ex.GetType().Name == "NotImplementedInReferenceAssemblyException")
        {
            UpdateState();
        }
    }

    private static bool IsNetworkUnavailable(NetworkAccess networkAccess)
        => networkAccess != NetworkAccess.Internet;

    private static bool HasNoAccess(NetworkAccess networkAccess)
        => networkAccess == NetworkAccess.None;
}
