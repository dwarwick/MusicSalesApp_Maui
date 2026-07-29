using System.ComponentModel;

namespace MusicSalesApp.Maui.Services;

public interface INetworkStatusService : INotifyPropertyChanged
{
    /// <summary>
    /// True whenever the platform reports anything other than <c>NetworkAccess.Internet</c>, which also
    /// covers <c>Unknown</c> and <c>ConstrainedInternet</c>. Deliberately pessimistic: right for a
    /// "you may be offline" banner, wrong for deciding whether a request can succeed.
    /// </summary>
    bool IsOffline { get; }

    /// <summary>
    /// True only when the platform reports <c>NetworkAccess.None</c> - it is certain there is no network.
    ///
    /// This is what server-only UI (tip, report, add to playlist, playlist editing) gates on. On
    /// <c>Unknown</c>/<c>ConstrainedInternet</c> the server is usually still reachable, so hiding those
    /// controls would take away working features; it also matches the check
    /// <see cref="OfflineAwareMusicService"/> uses to decide whether to skip a live call, so the UI and
    /// the service layer can never disagree about what "offline" means.
    /// </summary>
    bool HasNoNetworkAccess { get; }
}

/// <summary>
/// Both properties move independently - going from <c>None</c> to <c>ConstrainedInternet</c> changes
/// <see cref="INetworkStatusService.HasNoNetworkAccess"/> while leaving
/// <see cref="INetworkStatusService.IsOffline"/> alone - so subscribers have to listen for either.
/// </summary>
internal static class NetworkStatusChange
{
    public static bool AffectsConnectivity(string? propertyName)
        => propertyName is null
            or nameof(INetworkStatusService.IsOffline)
            or nameof(INetworkStatusService.HasNoNetworkAccess);
}
