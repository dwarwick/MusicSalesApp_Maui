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

    [ObservableProperty]
    public partial int StreamCount { get; set; }

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
    public partial bool? UserLikeStatus { get; set; }

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
    /// Artwork for the small surfaces - the 48-unit playlist track rows and the 36-unit mini player -
    /// and the source Media3 is handed for lock-screen artwork.
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
