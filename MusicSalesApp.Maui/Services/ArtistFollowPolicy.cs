using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Services;

/// <summary>
/// Whether a song belongs to the signed-in user, so the follow bell can be left off it.
/// </summary>
/// <remarks>
/// Following your own artist is meaningless, and the server refuses it outright with
/// <c>CannotFollowSelf</c> - a 400, like every other domain refusal. Without this the bell renders
/// live on your own song, flips optimistically under the finger, and is put back a round trip
/// later with nothing on screen to explain why. The web app has never offered it: the library
/// zeroes the persona id through its own IsOwnSong, and the song player through _isCreatorOfSong.
///
/// <para>
/// A static policy in the shape of <see cref="PreviewAccessPolicy"/> so the rule has one testable
/// home rather than a copy per surface - there are three, and the card is easy to forget.
/// </para>
/// </remarks>
public static class ArtistFollowPolicy
{
    /// <summary>
    /// Whether <paramref name="song"/> is the signed-in user's own music.
    /// </summary>
    /// <remarks>
    /// Checks both identifiers, the way <c>TipFlowHandler.CanShowTipButton</c> does for the tip
    /// button - the closest analogue, being the other control hidden on your own song. Either one
    /// matching is enough: a song carries the creator row it belongs to and the account behind it,
    /// and which of the two arrives populated depends on the query path that produced the DTO.
    ///
    /// <para>
    /// <b>Every comparison is explicitly guarded on HasValue.</b> Both sides are <c>int?</c>, so a
    /// bare <c>song.CreatorUserId == auth.UserId</c> is true when BOTH are null - which is the
    /// signed-out case, and would hide the bell from everyone who is not logged in. The server
    /// sends <c>CreatorUserId</c> as <c>Creator?.UserId</c>, so null genuinely does arrive whenever
    /// that navigation was not eager-loaded.
    /// </para>
    ///
    /// <para>
    /// Null therefore reads as "not mine", which fails in the safe direction: the bell is offered,
    /// and the server refuses it if the guess was wrong. The opposite default would silently
    /// withhold the control from songs nobody owns.
    /// </para>
    /// </remarks>
    public static bool IsOwnArtist(SongDto? song, IAuthService? authService)
    {
        if (song is null || authService is null || !authService.IsLoggedIn)
        {
            return false;
        }

        if (authService.CreatorId.HasValue
            && song.CreatorId.HasValue
            && authService.CreatorId.Value == song.CreatorId.Value)
        {
            return true;
        }

        return authService.UserId.HasValue
               && song.CreatorUserId.HasValue
               && authService.UserId.Value == song.CreatorUserId.Value;
    }
}
