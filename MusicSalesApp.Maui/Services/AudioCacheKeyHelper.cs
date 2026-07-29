using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Services;

internal static class AudioCacheKeyHelper
{
    /// <summary>
    /// Smallest byte count a completed audio download can plausibly have. Even a one-second
    /// low-bitrate MP3 is several kilobytes; anything under this floor is an error payload or
    /// truncated write masquerading as a song, and caching it poisons playback.
    /// </summary>
    public const long MinPlayableAudioBytes = 4096;

    public static string GetStableCacheKey(SongDto song)
    {
        if (!TryGetRemoteUri(song, out var remoteUri))
        {
            return $"song-{song.Id}";
        }

        return $"song-{song.Id}-{GetStablePathHash(song, remoteUri)}";
    }

    /// <summary>
    /// Hashes only the URL path, so a rotated SAS query string still resolves to the same cached audio.
    /// Delegates to <see cref="StableRemoteAssetKey"/> so audio and image cache keys cannot drift apart.
    /// The output is load-bearing: changing it orphans every track already on disk.
    /// </summary>
    public static string GetStablePathHash(SongDto song, Uri remoteUri)
        => StableRemoteAssetKey.GetPathHash(remoteUri, $"song-{song.Id}");

    public static bool TryGetRemoteUri(SongDto song, out Uri remoteUri)
        => StableRemoteAssetKey.TryGetAbsoluteUri(song.StreamUrl, out remoteUri);
}
