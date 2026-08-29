using System.Globalization;
using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.ViewModels;

/// <summary>
/// Works out how to open a playlist in the player.
/// </summary>
/// <remarks>
/// <para>
/// Exists because <b>not every playlist can be opened by id</b>. A custom playlist and Liked Songs
/// are real rows and carry one; the Recommended list and the five "most streamed" ones are generated
/// server-side, have no row, and all report <c>Id = 0</c>. Navigating those by id sends six different
/// tiles to the same broken page.
/// </para>
/// <para>
/// Both Home and My Playlists route through here so that rule lives in one place rather than being
/// re-derived per call site - and so it is unit-testable, which a method on a ViewModel that reaches
/// for <c>INavigationService</c> is not.
/// </para>
/// </remarks>
public static class PlaylistNavigationTarget
{
    /// <summary>Query key for a real playlist's id.</summary>
    public const string PlaylistIdKey = "PlaylistId";

    /// <summary>Query key for the Recommended list, which is scoped to one user.</summary>
    public const string RecommendedUserIdKey = "RecommendedUserId";

    /// <summary>Query key for a "most streamed" window.</summary>
    public const string TopStreamedWindowKey = "TopStreamedWindow";

    /// <summary>
    /// The route and query for <paramref name="playlist"/>, or <c>null</c> when it cannot be opened -
    /// an unknown kind, or a top-streamed tile that arrived without its key.
    /// </summary>
    /// <param name="playlist">The tile that was tapped.</param>
    /// <param name="currentUserId">
    /// The signed-in user, needed only by the Recommended list. Null when signed out, which is fine:
    /// the top-streamed playlists do not need it.
    /// </param>
    public static (string Route, Dictionary<string, object> Query)? For(PlaylistDto? playlist, int? currentUserId)
    {
        if (playlist is null)
        {
            return null;
        }

        // Shell.ApplyQueryAttributes does a direct cast for non-string values and the target
        // properties are string?, so every value goes across as a string.
        switch (playlist.Kind)
        {
            case PlaylistKinds.TopStreamed:
                return string.IsNullOrWhiteSpace(playlist.Key)
                    ? null
                    : (NavigationRoutes.PlaylistPlayer, new Dictionary<string, object>
                    {
                        [TopStreamedWindowKey] = playlist.Key
                    });

            case PlaylistKinds.Recommended:
                return currentUserId is null or 0
                    ? null
                    : (NavigationRoutes.PlaylistPlayer, new Dictionary<string, object>
                    {
                        [RecommendedUserIdKey] = currentUserId.Value.ToString(CultureInfo.InvariantCulture)
                    });

            default:
                // Custom and Liked Songs are real rows. A zero id here means the server sent a
                // generated list under a kind this build does not know about - better to do nothing
                // than to open playlist 0.
                return playlist.Id == 0
                    ? null
                    : (NavigationRoutes.PlaylistPlayer, new Dictionary<string, object>
                    {
                        [PlaylistIdKey] = playlist.Id.ToString(CultureInfo.InvariantCulture)
                    });
        }
    }
}
