using System.Security.Cryptography;
using System.Text;
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

    public static string GetStablePathHash(SongDto song, Uri remoteUri)
    {
        var keySource = string.IsNullOrWhiteSpace(remoteUri.AbsolutePath)
            ? $"song-{song.Id}"
            : remoteUri.AbsolutePath.ToLowerInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(keySource))).ToLowerInvariant();
    }

    public static bool TryGetRemoteUri(SongDto song, out Uri remoteUri)
    {
        if (string.IsNullOrWhiteSpace(song.StreamUrl))
        {
            remoteUri = null!;
            return false;
        }

        return Uri.TryCreate(song.StreamUrl, UriKind.Absolute, out remoteUri!);
    }
}
