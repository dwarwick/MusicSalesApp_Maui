namespace MusicSalesApp.Maui.ViewModels;

/// <summary>
/// Represents the kinds of playlists that can be shown in the MAUI app.
/// Values match the <c>Kind</c> string sent by the server.
/// </summary>
public static class PlaylistKinds
{
    public const string Custom = "Custom";
    public const string LikedSongs = "LikedSongs";
    public const string Recommended = "Recommended";

    /// <summary>
    /// One of the five global "most streamed" playlists. Identified by <see cref="PlaylistDto.Key"/>,
    /// because these have no id of their own.
    /// </summary>
    public const string TopStreamed = "TopStreamed";
}

/// <summary>
/// DTO for a playlist tile/list entry returned by
/// GET /api/mobile/playlists and GET /api/mobile/playlists/home.
/// </summary>
public class PlaylistDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SongCount { get; set; }
    public bool IsSystemGenerated { get; set; }
    public string Kind { get; set; } = PlaylistKinds.Custom;

    /// <summary>
    /// For a <see cref="PlaylistKinds.TopStreamed"/> playlist, its window key ("Day", "Week", ...);
    /// null for every other kind.
    /// </summary>
    /// <remarks>
    /// <b>These playlists all report <see cref="Id"/> = 0</b>, the same value Recommended uses, so
    /// they cannot be told apart by id and must be opened by key. That is what
    /// <see cref="PlaylistNavigationTarget"/> exists to enforce.
    /// </remarks>
    public string? Key { get; set; }

    /// <summary>Server-dictated position when several playlists are listed together. Lower first.</summary>
    public int DisplayOrder { get; set; }

    /// <summary>
    /// UTC time the top-streamed playlists were last ranked, or null for every other kind.
    /// </summary>
    public DateTime? GeneratedAtUtc { get; set; }
}

/// <summary>
/// DTO returned by GET /api/mobile/playlists/home. Either property may be null
/// (server omits empty playlists).
/// </summary>
public class HomePlaylistsDto
{
    public PlaylistDto? Recommended { get; set; }
    public PlaylistDto? LikedSongs { get; set; }

    /// <summary>
    /// The five global "most streamed" playlists, already in display order, empty ones omitted.
    /// </summary>
    /// <remarks>
    /// Unlike the two above, these are not personal and are populated for signed-out callers too.
    /// Read it through <see cref="TopStreamedOrEmpty"/> rather than directly: an older server does not
    /// send the property at all, and System.Text.Json would overwrite the initialiser with null if a
    /// server ever sent an explicit null.
    /// </remarks>
    public List<PlaylistDto>? TopStreamed { get; set; } = new();

    /// <summary>The top-streamed playlists, never null.</summary>
    public List<PlaylistDto> TopStreamedOrEmpty => TopStreamed ?? [];
}

/// <summary>
/// DTO for one song inside a playlist response. Mirrors SongDto plus
/// the SongMetadataId and (optional) UserPlaylistId needed for remove/reorder.
/// </summary>
public class PlaylistSongDto
{
    public int Id { get; set; }
    public int SongMetadataId { get; set; }
    public int? UserPlaylistId { get; set; }
    public string SongTitle { get; set; } = string.Empty;
    public string ArtistName { get; set; } = string.Empty;
    public string Genre { get; set; } = string.Empty;
    public string? AlbumArtUrl { get; set; }

    /// <summary>Small pre-resized rendition; null when none exists. See <see cref="SongDto"/>.</summary>
    public string? AlbumArtThumbUrl { get; set; }

    /// <summary>Larger pre-resized rendition for the player hero; null when none exists.</summary>
    public string? AlbumArtHeroUrl { get; set; }

    /// <summary>Cache-busting counter for the cover art. See <see cref="SongDto"/>.</summary>
    public int AlbumArtVersion { get; set; }

    public string? PersonaImageUrl { get; set; }

    /// <summary>Small pre-resized rendition of the persona image; null when none exists.</summary>
    public string? PersonaImageThumbUrl { get; set; }

    /// <summary>Larger persona-image rendition for the persona page; null when none exists.</summary>
    public string? PersonaImageHeroUrl { get; set; }

    /// <summary>Cache-busting counter for the persona image.</summary>
    public int PersonaImageVersion { get; set; }

    public string? PersonaBio { get; set; }
    public string StreamUrl { get; set; } = string.Empty;
    public int StreamQualifyingSeconds { get; set; }

    /// <summary>
    /// Streams inside this list's period, or null when the list has no period of its own.
    /// </summary>
    /// <remarks>
    /// This is what the top-streamed playlists are RANKED on, whereas <c>StreamCount</c> is the
    /// lifetime total the player keeps live. On "Top 10 Today" the two differ, so showing only the
    /// lifetime figure would render a correctly ordered list that looks mis-sorted.
    /// </remarks>
    public int? PeriodStreamCount { get; set; }
    public double? TrackLengthSeconds { get; set; }
    public bool DisplayOnHomePage { get; set; }
    public int? DisplayOrder { get; set; }
    public int StreamCount { get; set; }
    public bool IsAiGenerated { get; set; }
    public bool IsAiVocals { get; set; }
    public bool IsAiLyrics { get; set; }
    public int? CreatorId { get; set; }
    public int? CreatorUserId { get; set; }
}

/// <summary>
/// DTO returned by GET /api/mobile/playlists/{id}/songs.
/// </summary>
public class PlaylistSongsDto
{
    public int PlaylistId { get; set; }
    public string PlaylistName { get; set; } = string.Empty;
    public bool IsSystemGenerated { get; set; }
    public List<PlaylistSongDto> Songs { get; set; } = new();

    /// <summary>
    /// Heading for each song's <see cref="PlaylistSongDto.PeriodStreamCount"/> - "Today", "This Week"
    /// and so on - or null when the list has no period. Null for everything except the four rolling
    /// top-streamed playlists.
    /// </summary>
    public string? PeriodLabel { get; set; }

    /// <summary>UTC time this playlist was last ranked, or null when it is not a top-streamed one.</summary>
    public DateTime? GeneratedAtUtc { get; set; }
}

/// <summary>
/// Wraps the server response for GET /{id}/available-songs so callers can
/// distinguish "no subscription" from "success with empty list".
/// </summary>
public class AvailableSongsResponse
{
    public List<PlaylistSongDto> Songs { get; set; } = new();
    public bool RequiresSubscription { get; set; }
}

/// <summary>
/// Discriminated-style result used by PlaylistService methods so callers can
/// render the right UI for success / failure / subscription-required.
/// </summary>
public class PlaylistOperationResult
{
    public bool Success { get; init; }
    public bool RequiresSubscription { get; init; }
    public string? ErrorMessage { get; init; }

    public static PlaylistOperationResult Ok() => new() { Success = true };
    public static PlaylistOperationResult NeedsSubscription() =>
        new() { Success = false, RequiresSubscription = true };
    public static PlaylistOperationResult Fail(string message) =>
        new() { Success = false, ErrorMessage = message };
}

public class PlaylistOperationResult<T> : PlaylistOperationResult
{
    public T? Value { get; init; }

    public static PlaylistOperationResult<T> Ok(T value) =>
        new() { Success = true, Value = value };
    public static new PlaylistOperationResult<T> NeedsSubscription() =>
        new() { Success = false, RequiresSubscription = true };
    public static new PlaylistOperationResult<T> Fail(string message) =>
        new() { Success = false, ErrorMessage = message };
}
