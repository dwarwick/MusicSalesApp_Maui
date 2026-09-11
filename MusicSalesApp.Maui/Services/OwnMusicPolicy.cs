using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Services;

/// <summary>
/// Whether a song belongs to the signed-in user.
/// </summary>
/// <remarks>
/// Four separate features turn on this one question, and each used to answer it for itself:
///
/// <list type="bullet">
/// <item>The follow bell is hidden, because following your own artist is meaningless and the server
/// refuses it with <c>CannotFollowSelf</c> - a 400, like every other domain refusal.</item>
/// <item>The tip button is hidden, because tipping yourself is not a thing.</item>
/// <item>The preview limit does not apply: creators hear their own songs in full.</item>
/// <item>No stream is recorded, so a creator cannot inflate their own count by listening.</item>
/// </list>
///
/// <para>
/// The copies disagreed, which is the reason this exists. Three of them tested only
/// <c>CreatorUserId</c>, so a song whose <c>Creator</c> navigation was not eager-loaded matched on
/// <c>CreatorId</c> alone - and a creator could get no follow bell on a track while still being
/// offered a tip button and cut off at the sixty-second preview of their own music.
/// </para>
///
/// <para>
/// A static policy in the shape of <see cref="PreviewAccessPolicy"/>, so the rule has one testable
/// home rather than a copy per surface.
/// </para>
/// </remarks>
public static class OwnMusicPolicy
{
    /// <summary>
    /// Whether <paramref name="song"/> is the signed-in user's own music.
    /// </summary>
    public static bool IsOwnMusic(SongDto? song, IAuthService? authService) =>
        song is not null && IsOwnMusic(song.CreatorId, song.CreatorUserId, authService);

    /// <summary>
    /// Whether the music behind these two identifiers is the signed-in user's own.
    /// </summary>
    /// <remarks>
    /// Takes the ids rather than a <see cref="SongDto"/> because not every caller has one - the tip
    /// button is handed the pair directly.
    ///
    /// <para>
    /// Either identifier matching is enough: a song carries the creator row it belongs to and the
    /// account behind it, and which of the two arrives populated depends on the query path that
    /// produced it.
    /// </para>
    ///
    /// <para>
    /// <b>Every comparison is explicitly guarded on HasValue.</b> Both sides are <c>int?</c>, so a
    /// bare <c>creatorUserId == auth.UserId</c> is true when BOTH are null - which is the
    /// signed-out case, and would hide the bell from everyone who is not logged in. The server
    /// sends <c>CreatorUserId</c> as <c>Creator?.UserId</c>, so null genuinely does arrive whenever
    /// that navigation was not eager-loaded.
    /// </para>
    ///
    /// <para>
    /// Null therefore reads as "not mine", which fails in the safe direction: the control is
    /// offered, and the server refuses it if the guess was wrong. The opposite default would
    /// silently withhold it from songs nobody owns.
    /// </para>
    ///
    /// <para>
    /// Note there is no <c>IsCreator</c> test. Two of the old copies had one, but it is redundant:
    /// an account whose id matches the song's creator IS that song's creator, whatever a cached
    /// role flag says - and relying on the flag meant a stale one took a creator's own music away
    /// from them.
    /// </para>
    /// </remarks>
    public static bool IsOwnMusic(int? creatorId, int? creatorUserId, IAuthService? authService)
    {
        if (authService is null || !authService.IsLoggedIn)
        {
            return false;
        }

        if (authService.CreatorId.HasValue
            && creatorId.HasValue
            && authService.CreatorId.Value == creatorId.Value)
        {
            return true;
        }

        return authService.UserId.HasValue
               && creatorUserId.HasValue
               && authService.UserId.Value == creatorUserId.Value;
    }
}
