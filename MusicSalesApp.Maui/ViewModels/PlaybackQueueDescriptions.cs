namespace MusicSalesApp.Maui.ViewModels;

internal static class PlaybackQueueDescriptions
{
    public const string FeaturedSongs = "Featured Songs";
    public const string UnfilteredMediaLibrary = "Unfiltered media library";

    public static string Artist(string artistName) => $"Artist {artistName}";

    public static string Genre(string genreName) => $"Genre {genreName}";

    public static string SongPage(SongDto song) => $"Song page: {song.SongTitle}";

    public static string Playlist(string playlistTitle) => $"Playlist {playlistTitle}";

    public static string FilteredMediaLibrary(IEnumerable<string> filters) =>
        $"Filtered media library ({string.Join("; ", filters)})";
}
