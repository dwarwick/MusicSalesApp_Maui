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
    public string? PersonaImageUrl { get; set; }
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
    public partial string? CachedAlbumArtPath { get; set; }

    [JsonIgnore]
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PersonaImageDisplaySource))]
    public partial string? CachedPersonaImagePath { get; set; }

    /// <summary>
    /// Set while offline. Suppresses the remote URL fallback so no image request is attempted; the UI
    /// shows its built-in placeholder instead of an image that would never load.
    /// </summary>
    [JsonIgnore]
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AlbumArtDisplaySource))]
    [NotifyPropertyChangedFor(nameof(PersonaImageDisplaySource))]
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
    /// Builds a share URL using the song's numeric ID to avoid encoding issues.
    /// The server redirects /share/{id} → /song/{encoded-title} with OG tags.
    /// </summary>
    public static string BuildShareUrl(int songId, string webBaseUrl) =>
        $"{webBaseUrl}/share/{songId}";
}
