#if !ANDROID
namespace MusicSalesApp.Maui.Services;

/// <summary>
/// The one media item the OS "now playing" surface is currently showing, reduced to the two members
/// the artwork loader needs.
///
/// <para>
/// This seam exists so the coordination policy - when to load, when to retry, when to release - can be
/// unit tested without UIKit and without Plugin.MediaManager. The only implementations are the adapter
/// inside <c>MediaManagerPlaybackRuntime</c> and the test double.
/// </para>
/// </summary>
public interface INowPlayingArtworkTarget
{
    /// <summary>A file:// URI, an http(s) URL, or empty when this song has no artwork at all.</summary>
    string ArtworkUri { get; }

    /// <summary>
    /// Content version of the rendition <see cref="ArtworkUri"/> came from. Required to write a
    /// downloaded image into the image cache under the correct key; see
    /// <see cref="PlaybackMediaItem.AlbumImageContentVersion"/>.
    /// </summary>
    int ArtworkContentVersion { get; }

    /// <summary>
    /// The decoded platform image the OS reads - a UIImage on Apple, reached through
    /// <c>IMediaItem.DisplayImage</c>. Assigning null clears it.
    /// </summary>
    object? Image { get; set; }
}

/// <summary>
/// Decodes an artwork URI into the platform image object the OS now-playing surface wants.
/// </summary>
public interface INowPlayingArtworkLoader
{
    Task<NowPlayingArtworkLoadResult> LoadAsync(
        string artworkUri,
        int contentVersion = 0,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The decoded image, where there is one, and whether a later attempt is worth making.
///
/// <para>
/// The retry flag matters for the same reason it does on <see cref="ImageCacheOutcome"/>: a dropped
/// request deserves another go a few seconds later, whereas a payload that downloaded perfectly and
/// simply is not a decodable image will never become one, and retrying it just re-downloads the same
/// bytes on every notification heartbeat.
/// </para>
/// </summary>
public readonly record struct NowPlayingArtworkLoadResult(object? Image, bool IsWorthRetrying)
{
    /// <summary>Decoded and ready to hand to the OS.</summary>
    public static NowPlayingArtworkLoadResult Loaded(object image) => new(image, false);

    /// <summary>Nothing usable, and no later attempt will change that.</summary>
    public static NowPlayingArtworkLoadResult Unavailable => new(null, false);

    /// <summary>Nothing this time - a network or I/O failure. Worth another attempt.</summary>
    public static NowPlayingArtworkLoadResult Retryable => new(null, true);

    public bool HasImage => Image is not null;
}

/// <summary>
/// Windows, and anywhere else with no OS now-playing surface to feed.
/// </summary>
public sealed class NoOpNowPlayingArtworkLoader : INowPlayingArtworkLoader
{
    public Task<NowPlayingArtworkLoadResult> LoadAsync(
        string artworkUri,
        int contentVersion = 0,
        CancellationToken cancellationToken = default)
        => Task.FromResult(NowPlayingArtworkLoadResult.Unavailable);
}
#endif
