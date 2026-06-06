namespace MusicSalesApp.Maui.Services;

public static class SongArtworkFallbackState
{
    public static bool HasAlbumArt(string? albumArtUrl)
        => !string.IsNullOrWhiteSpace(albumArtUrl);
}
