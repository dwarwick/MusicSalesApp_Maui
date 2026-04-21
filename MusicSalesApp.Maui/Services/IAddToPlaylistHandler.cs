namespace MusicSalesApp.Maui.Services;

/// <summary>
/// Shared UX for the "Add to Playlist" button used across the MusicLibrary,
/// SongPlayer and PlaylistPlayer pages. Shows an action sheet of the user's
/// custom playlists plus a "+ New playlist..." option that prompts for a name
/// and creates it on the fly. Non-subscribers are routed to a friendly message.
/// </summary>
public interface IAddToPlaylistHandler
{
    Task ShowAsync(int songMetadataId, string songTitle);
}
