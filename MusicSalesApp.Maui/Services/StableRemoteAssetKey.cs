using System.Security.Cryptography;
using System.Text;

namespace MusicSalesApp.Maui.Services;

/// <summary>
/// Derives cache keys for remote assets served from Azure Blob storage behind a SAS URL.
///
/// The server regenerates the SAS query string on every API call, so the *full* URL of a given blob
/// differs on each fetch even though the blob itself never moved. Keying a cache on the whole URL
/// therefore misses 100% of the time across sessions. Every key here is derived from
/// <see cref="Uri.AbsolutePath"/> alone, which is stable for the life of the blob.
/// </summary>
internal static class StableRemoteAssetKey
{
    public static bool TryGetAbsoluteUri(string? url, out Uri remoteUri)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            remoteUri = null!;
            return false;
        }

        return Uri.TryCreate(url, UriKind.Absolute, out remoteUri!);
    }

    /// <summary>
    /// Hex-encoded SHA-256 of the URL's path, ignoring the rotating SAS query string.
    /// <paramref name="fallbackSeed"/> is hashed instead when the URI carries no usable path.
    /// </summary>
    /// <param name="contentVersion">
    /// A counter the server increments whenever it rewrites the blob at this path, folded into the
    /// key so the old cached copy is abandoned rather than served forever.
    ///
    /// <para>
    /// Cover art under the GUID naming scheme lives at a fixed path that a re-crop overwrites in
    /// place - same path, new pixels - so the path alone cannot tell the two apart.
    /// </para>
    ///
    /// <para>
    /// <b>Zero must produce the same hash as if this parameter did not exist.</b> Audio caching
    /// shares this method and has no version of its own; changing its keys would orphan every
    /// track a user has downloaded and silently re-download their entire offline library.
    /// </para>
    /// </param>
    public static string GetPathHash(Uri remoteUri, string fallbackSeed, int contentVersion = 0)
    {
        var keySource = string.IsNullOrWhiteSpace(remoteUri.AbsolutePath)
            ? fallbackSeed
            : remoteUri.AbsolutePath.ToLowerInvariant();

        if (contentVersion > 0)
        {
            keySource = $"{keySource}|v{contentVersion}";
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(keySource))).ToLowerInvariant();
    }

    /// <summary>
    /// File extension from the URL's path, or <paramref name="defaultExtension"/> when the path has none.
    /// Anything implausibly long is rejected, since a blob path may legitimately contain dots.
    /// </summary>
    public static string GetExtension(Uri remoteUri, string defaultExtension)
    {
        var extension = Path.GetExtension(remoteUri.AbsolutePath);
        return string.IsNullOrWhiteSpace(extension) || extension.Length > 10
            ? defaultExtension
            : extension.ToLowerInvariant();
    }
}
