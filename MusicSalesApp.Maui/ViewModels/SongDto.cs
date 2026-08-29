using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MusicSalesApp.Maui.ViewModels;

/// <summary>
/// DTO matching the SongListItemDto returned by GET /api/music/songs.
/// LikeCount/DislikeCount are populated separately from the bulk likes endpoint.
/// Extends ObservableObject so SignalR-driven property updates refresh the UI.
/// </summary>
public partial class SongDto : ObservableObject
{
    public int Id { get; set; }
    public string SongTitle { get; set; } = string.Empty;
    public string ArtistName { get; set; } = string.Empty;
    public string Genre { get; set; } = string.Empty;
    public string? AlbumArtUrl { get; set; }

    /// <summary>
    /// Small pre-resized rendition of the cover art. Null against a server that predates the
    /// feature, or for a song whose renditions have not been generated yet.
    /// </summary>
    public string? AlbumArtThumbUrl { get; set; }

    /// <summary>Larger pre-resized rendition, for the song cards and the player hero.</summary>
    public string? AlbumArtHeroUrl { get; set; }

    /// <summary>
    /// Server-side counter for the cover art, incremented whenever it is rewritten. Folded into the
    /// image cache key, because cover art under the GUID naming scheme keeps a fixed blob path that
    /// a re-crop overwrites in place - without this the cache would serve the pre-crop image until
    /// the OS purged it.
    /// </summary>
    public int AlbumArtVersion { get; set; }

    public string? PersonaImageUrl { get; set; }

    /// <summary>Small pre-resized rendition of the persona image.</summary>
    public string? PersonaImageThumbUrl { get; set; }

    /// <summary>
    /// Larger pre-resized rendition of the persona image, for the 120-DIP persona page - which needs
    /// 360 px on a 3x screen, more than the thumb carries.
    /// </summary>
    public string? PersonaImageHeroUrl { get; set; }

    /// <summary>As <see cref="AlbumArtVersion"/>, for the persona image.</summary>
    public int PersonaImageVersion { get; set; }

    public string? PersonaBio { get; set; }

    /// <summary>
    /// The persona's own website, or null when they have not given one.
    /// </summary>
    /// <remarks>
    /// Stored exactly as the creator typed it - the server does nothing but Trim it - so it may
    /// well arrive without a scheme. Do not hand it to Launcher.OpenAsync unguarded.
    /// </remarks>
    public string? PersonaWebsiteUrl { get; set; }

    /// <summary>
    /// The blob path of this song's word-level lyric timings, or null when there are none a
    /// listener may see. Fetched from <c>api/music/{path}?v={LyricsVersion}</c>.
    /// </summary>
    /// <remarks>
    /// The server resolves this and sends null for anything unpublished, so its presence IS the
    /// permission - there is no status to re-check here.
    /// </remarks>
    public string? LyricsTimingsPath { get; set; }

    /// <summary>As <see cref="AlbumArtVersion"/>, for the lyric timings.</summary>
    /// <remarks>
    /// Part of the cache key, not decoration. The timings blob path never changes between
    /// re-publishes, so without this a corrected set would never replace the one on disk.
    /// </remarks>
    public int LyricsVersion { get; set; }

    public string StreamUrl { get; set; } = string.Empty;
    public int StreamQualifyingSeconds { get; set; }
    public double? TrackLengthSeconds { get; set; }
    public bool DisplayOnHomePage { get; set; }
    public int? DisplayOrder { get; set; }
    public bool IsAiGenerated { get; set; }
    public bool IsAiVocals { get; set; }
    public bool IsAiLyrics { get; set; }
    public int? CreatorId { get; set; }
    public int? CreatorUserId { get; set; }

    /// <summary>
    /// This song's 1-based position in the list it is being shown in.
    /// </summary>
    /// <remarks>
    /// Assigned by the owning view model rather than derived in the row, because a DataTemplate has
    /// no access to its own index and IndexOf per row is quadratic. Not serialized: it belongs to a
    /// particular listing, not to the song.
    /// </remarks>
    [JsonIgnore]
    [ObservableProperty]
    public partial int TrackNumber { get; set; }

    /// <summary>
    /// Whether this row is the track the player is currently on.
    /// </summary>
    /// <remarks>
    /// Carried on the DTO rather than computed in the row, because a DataTemplate cannot see the
    /// playback service and the alternative - a converter reaching for a service locator per row -
    /// re-evaluates for every song on every track change. The owning view model sets this on the
    /// two songs whose state actually changed. Not serialized: it is view state, and the offline
    /// catalogue store persists this type verbatim.
    /// </remarks>
    [JsonIgnore]
    [ObservableProperty]
    public partial bool IsNowPlaying { get; set; }

    [ObservableProperty]
    public partial int StreamCount { get; set; }

    /// <summary>
    /// Streams inside the period of the list this song is being shown in, or null when the list has
    /// no period.
    /// </summary>
    /// <remarks>
    /// Set only by the four rolling "most streamed" playlists, which are RANKED on this while
    /// <see cref="StreamCount"/> is the lifetime total kept live by SignalR. Showing only the lifetime
    /// figure there would render a correctly ordered list that looks mis-sorted.
    ///
    /// <para>
    /// Not serialized: it belongs to the list being viewed, not to the song, and the offline catalogue
    /// store persists this type verbatim.
    /// </para>
    /// </remarks>
    [JsonIgnore]
    [ObservableProperty]
    public partial int? PeriodStreamCount { get; set; }

    // Populated from bulk likes endpoint, not from songs API
    [ObservableProperty]
    public partial int LikeCount { get; set; }

    [ObservableProperty]
    public partial int DislikeCount { get; set; }

    /// <summary>
    /// Per-user like status: true = liked, false = disliked, null = none.
    /// Populated from bulk user-status endpoint.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRate))]
    public partial bool? UserLikeStatus { get; set; }

    /// <summary>
    /// Whether the current user has streamed this song, which is what entitles them to rate it.
    ///
    /// Populated from the bulk user-status endpoint alongside <see cref="UserLikeStatus"/>, and set
    /// locally the moment playback passes the qualifying threshold so the buttons come alive part-way
    /// through a listen rather than waiting for the next catalogue load. Not JsonIgnored, so it rides
    /// along in the offline catalogue snapshot the same way the like status does.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRate))]
    public partial bool HasStreamed { get; set; }

    /// <summary>
    /// Whether the thumbs are live for this song.
    ///
    /// Setting an opinion needs a stream; clearing one never does, so an existing rating stays
    /// actionable either way. Mirrors the asymmetry the server enforces in SongLikeService.
    /// </summary>
    public bool CanRate => HasStreamed || UserLikeStatus != null;

    /// <summary>
    /// Pre-built share URL for this song (e.g. https://domain/song/Encoded%20Title).
    /// Set by the ViewModel after loading songs.
    /// </summary>
    public string ShareUrl { get; set; } = string.Empty;

    // --- Artwork resolution ---
    //
    // AlbumArtUrl/PersonaImageUrl stay the canonical remote URLs; they are still needed to download the
    // image and to derive its cache key. The properties below layer a locally cached copy on top, and
    // are set by ISongArtworkHydrator. JsonIgnore keeps per-device paths out of the offline catalog.
    //
    // Note the fallback shape: with none of these set, DisplaySource == Url, so a code path that
    // forgets to hydrate degrades to the pre-existing behaviour rather than to blank artwork.

    [JsonIgnore]
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AlbumArtDisplaySource))]
    [NotifyPropertyChangedFor(nameof(AlbumArtThumbDisplaySource))]
    [NotifyPropertyChangedFor(nameof(AlbumArtHeroDisplaySource))]
    public partial string? CachedAlbumArtPath { get; set; }

    [JsonIgnore]
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PersonaImageDisplaySource))]
    [NotifyPropertyChangedFor(nameof(PersonaImageThumbDisplaySource))]
    public partial string? CachedPersonaImagePath { get; set; }

    /// <summary>
    /// A locally cached copy of the timings, when one exists. JsonIgnore for the same reason the
    /// artwork paths are: a per-device path has no meaning in the shared offline catalog.
    /// </summary>
    [JsonIgnore]
    [ObservableProperty]
    public partial string? CachedLyricsPath { get; set; }

    [JsonIgnore]
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AlbumArtThumbDisplaySource))]
    [NotifyPropertyChangedFor(nameof(AlbumArtHeroDisplaySource))]
    public partial string? CachedAlbumArtThumbPath { get; set; }

    [JsonIgnore]
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AlbumArtHeroDisplaySource))]
    public partial string? CachedAlbumArtHeroPath { get; set; }

    [JsonIgnore]
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PersonaImageThumbDisplaySource))]
    [NotifyPropertyChangedFor(nameof(PersonaImageHeroDisplaySource))]
    public partial string? CachedPersonaImageThumbPath { get; set; }

    [JsonIgnore]
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PersonaImageHeroDisplaySource))]
    public partial string? CachedPersonaImageHeroPath { get; set; }

    /// <summary>
    /// Set while offline. Suppresses the remote URL fallback so no image request is attempted; the UI
    /// shows its built-in placeholder instead of an image that would never load.
    /// </summary>
    [JsonIgnore]
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AlbumArtDisplaySource))]
    [NotifyPropertyChangedFor(nameof(PersonaImageDisplaySource))]
    [NotifyPropertyChangedFor(nameof(AlbumArtThumbDisplaySource))]
    [NotifyPropertyChangedFor(nameof(AlbumArtHeroDisplaySource))]
    [NotifyPropertyChangedFor(nameof(PersonaImageThumbDisplaySource))]
    [NotifyPropertyChangedFor(nameof(PersonaImageHeroDisplaySource))]
    public partial bool SuppressRemoteArtwork { get; set; }

    /// <summary>Local cached album art if present, otherwise the remote URL (unless suppressed).</summary>
    [JsonIgnore]
    public string? AlbumArtDisplaySource =>
        CachedAlbumArtPath ?? (SuppressRemoteArtwork ? null : AlbumArtUrl);

    /// <summary>Local cached persona image if present, otherwise the remote URL (unless suppressed).</summary>
    [JsonIgnore]
    public string? PersonaImageDisplaySource =>
        CachedPersonaImagePath ?? (SuppressRemoteArtwork ? null : PersonaImageUrl);

    /// <summary>
    /// Artwork for the small surfaces - the 48-unit playlist track rows and the 36-unit mini player.
    ///
    /// <para>
    /// Media3's notification artwork prefers the same rendition but does not read this property:
    /// <c>PlaybackService.ResolveAlbumImageUri</c> re-derives the chain from <c>IImageCacheService</c>
    /// so it can emit a <c>file://</c> URI rather than a bare path.
    /// </para>
    ///
    /// <para>
    /// Each step down the chain is a real fallback, not a formality: the cached thumb, then the
    /// cached full-size copy an older build of the app may have left behind, then the remote thumb,
    /// and finally <see cref="AlbumArtUrl"/>. That last step is what keeps this app working against
    /// a server that has not been updated yet, where every rendition URL arrives null.
    /// </para>
    /// </summary>
    [JsonIgnore]
    public string? AlbumArtThumbDisplaySource =>
        CachedAlbumArtThumbPath
        ?? CachedAlbumArtPath
        ?? (SuppressRemoteArtwork ? null : (AlbumArtThumbUrl ?? AlbumArtUrl));

    /// <summary>
    /// Artwork for the large surfaces - the 150-unit song cards and the 180-unit player hero.
    ///
    /// <para>
    /// Falls back to the thumb before the full-size original: the hero rendition is only cached
    /// when budget allows, and an upscaled thumb shows immediately from disk where the full-size
    /// blob would mean a slow download of roughly ten times the bytes.
    /// </para>
    /// </summary>
    [JsonIgnore]
    public string? AlbumArtHeroDisplaySource =>
        CachedAlbumArtHeroPath
        ?? CachedAlbumArtThumbPath
        ?? CachedAlbumArtPath
        ?? (SuppressRemoteArtwork ? null : (AlbumArtHeroUrl ?? AlbumArtThumbUrl ?? AlbumArtUrl));

    /// <summary>Persona artwork for the small artist chips - 20 to 24 DIP.</summary>
    [JsonIgnore]
    public string? PersonaImageThumbDisplaySource =>
        CachedPersonaImageThumbPath
        ?? CachedPersonaImagePath
        ?? (SuppressRemoteArtwork ? null : (PersonaImageThumbUrl ?? PersonaImageUrl));

    /// <summary>
    /// Persona artwork for the 120-DIP persona page, which needs 360 px on a 3x screen - more than
    /// the 320 px thumb carries, so it prefers the larger rendition and falls back down the chain.
    /// </summary>
    [JsonIgnore]
    public string? PersonaImageHeroDisplaySource =>
        CachedPersonaImageHeroPath
        ?? CachedPersonaImageThumbPath
        ?? CachedPersonaImagePath
        ?? (SuppressRemoteArtwork
            ? null
            : (PersonaImageHeroUrl ?? PersonaImageThumbUrl ?? PersonaImageUrl));

    /// <summary>
    /// Builds a share URL using the song's numeric ID to avoid encoding issues.
    /// The server redirects /share/{id} → /song/{encoded-title} with OG tags.
    /// </summary>
    public static string BuildShareUrl(int songId, string webBaseUrl) =>
        $"{webBaseUrl}/share/{songId}";
}
