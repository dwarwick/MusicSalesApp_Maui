using System.ComponentModel;
using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.Tests.Services;

/// <summary>
/// Test double for <see cref="INetworkStatusService"/>. The real implementation marshals its
/// PropertyChanged to the main thread, which is unavailable under NUnit; this raises it inline so
/// subscribers can be exercised synchronously.
/// </summary>
public sealed class TestNetworkStatusService : INetworkStatusService
{
    private bool _isOffline;
    private bool _hasNoNetworkAccess;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsOffline
    {
        get => _isOffline;
        set => SetOffline(value);
    }

    public bool HasNoNetworkAccess => _hasNoNetworkAccess;

    /// <summary>Airplane mode: no network at all, which is both "offline" and "no access".</summary>
    public void SetOffline(bool isOffline) => SetState(isOffline, isOffline);

    /// <summary>
    /// Connected but degraded (<c>ConstrainedInternet</c>/<c>Unknown</c>): pessimistically "offline",
    /// yet the server is very likely reachable, so server-only UI must stay visible.
    /// </summary>
    public void SetConstrained() => SetState(isOffline: true, hasNoNetworkAccess: false);

    private void SetState(bool isOffline, bool hasNoNetworkAccess)
    {
        if (_isOffline != isOffline)
        {
            _isOffline = isOffline;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsOffline)));
        }

        if (_hasNoNetworkAccess != hasNoNetworkAccess)
        {
            _hasNoNetworkAccess = hasNoNetworkAccess;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasNoNetworkAccess)));
        }
    }
}
