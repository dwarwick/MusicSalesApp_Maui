using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Services;

/// <summary>
/// A <see cref="IMusicService.GetSongsAsync()"/> result that carries where it came from.
///
/// <see cref="IMusicService.LastSongsSource"/> and <see cref="IMusicService.LastSongsError"/> are
/// per-service state, so a caller reading them after its own await can observe a *different* caller's
/// load. That is not hypothetical: a single connectivity change reloads the library, home and playlist
/// player at once, and the moment they overlap is exactly the moment one may be live while another
/// falls back to the cache - the window where reading the wrong one hides the offline banner or skips
/// the cached like counts.
///
/// Returning the outcome with the list makes each caller's read atomic, while staying a plain
/// <c>List&lt;SongDto&gt;</c> for every caller (and every test double) that doesn't care.
/// </summary>
public sealed class SongCatalogList : List<SongDto>
{
    public SongCatalogList(IEnumerable<SongDto> songs, SongCatalogSource source, string? error)
        : base(songs)
    {
        Source = source;
        Error = error;
    }

    public SongCatalogSource Source { get; }

    public string? Error { get; }
}

/// <summary>Where one specific catalog load came from, and what went wrong if anything did.</summary>
public readonly record struct SongCatalogOutcome(SongCatalogSource Source, string? Error)
{
    /// <summary>
    /// Reads the outcome tagged onto <paramref name="songs"/>, falling back to the service's last-load
    /// properties for any implementation that doesn't tag - which keeps the undecorated
    /// <see cref="MusicService"/> and loose test doubles behaving exactly as before.
    /// </summary>
    public static SongCatalogOutcome For(IReadOnlyList<SongDto> songs, IMusicService musicService)
        => songs is SongCatalogList tagged
            ? new SongCatalogOutcome(tagged.Source, tagged.Error)
            : new SongCatalogOutcome(musicService.LastSongsSource, musicService.LastSongsError);
}
