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
}

/// <summary>
/// DTO returned by GET /api/mobile/playlists/home. Either property may be null
/// (server omits empty playlists).
/// </summary>
public class HomePlaylistsDto
{
    public PlaylistDto? Recommended { get; set; }
    public PlaylistDto? LikedSongs { get; set; }
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
    public string? PersonaImageUrl { get; set; }
    public string? PersonaBio { get; set; }
    public string StreamUrl { get; set; } = string.Empty;
    public int StreamQualifyingSeconds { get; set; }
    public double? TrackLengthSeconds { get; set; }
    public bool DisplayOnHomePage { get; set; }
    public int? DisplayOrder { get; set; }
    public int StreamCount { get; set; }
    public bool IsAiGenerated { get; set; }
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
