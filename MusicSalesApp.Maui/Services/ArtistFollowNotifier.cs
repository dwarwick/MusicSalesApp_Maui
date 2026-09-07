namespace MusicSalesApp.Maui.Services;

/// <summary>
/// Announces that the signed-in user started or stopped following an artist.
/// </summary>
/// <remarks>
/// One artist owns many cards. The library can show a dozen songs by the same persona, and the
/// player, the persona section and the card behind it can all be on screen at once - so following
/// from one place has to move every other bell for that artist, or the page contradicts itself.
///
/// <para>
/// Deliberately NOT SignalR. This is one client's own state, not a public count: nobody else may
/// learn who follows whom, which is the whole privacy rule of the feature. The web solves the same
/// problem with a shared set on the parent component; here the sets live in several ViewModels, so
/// a notifier is the equivalent.
/// </para>
/// </remarks>
public interface IArtistFollowNotifier
{
    event EventHandler<ArtistFollowChange>? FollowStateChanged;

    /// <summary>
    /// Tells every listener the state that is now true on the server.
    /// </summary>
    void NotifyFollowStateChanged(int personaId, bool isFollowing);
}

public sealed record ArtistFollowChange(int PersonaId, bool IsFollowing);

/// <inheritdoc />
public sealed class ArtistFollowNotifier : IArtistFollowNotifier
{
    public event EventHandler<ArtistFollowChange>? FollowStateChanged;

    public void NotifyFollowStateChanged(int personaId, bool isFollowing)
    {
        // Raised on whichever thread made the change. Subscribers touch bound properties, so they
        // marshal to the UI thread themselves rather than this deciding for them - the same shape
        // AuthService.AuthStateChanged uses, and it keeps this type free of MAUI platform types so
        // it stays compiled into the test project.
        FollowStateChanged?.Invoke(this, new ArtistFollowChange(personaId, isFollowing));
    }
}
