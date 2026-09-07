using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Tests.ViewModels;

/// <summary>
/// The one expression that decides whether a follow bell is offered.
/// </summary>
/// <remarks>
/// <see cref="SongDto.CanFollowArtist"/> gates the bell on all three surfaces - the song card and
/// both players bind it rather than deciding for themselves - so its terms and its change
/// notification are worth pinning here rather than three times over.
/// </remarks>
[TestFixture]
public class SongDtoFollowStateTests
{
    [Test]
    public void CanFollowArtist_IsFalseWithoutAnArtistEntity()
    {
        // A song whose artist is only free text. An inert bell would invite a tap that can never
        // work, so it is hidden entirely.
        var song = new SongDto { Id = 1, PersonaId = null };

        Assert.That(song.CanFollowArtist, Is.False);
    }

    [Test]
    public void CanFollowArtist_IsFalseForYourOwnMusic()
    {
        var song = new SongDto { Id = 1, PersonaId = 10, IsOwnArtist = true };

        Assert.That(song.CanFollowArtist, Is.False);
    }

    [Test]
    public void CanFollowArtist_IsTrueForAnotherArtistsSong()
    {
        var song = new SongDto { Id = 1, PersonaId = 10 };

        Assert.That(song.CanFollowArtist, Is.True);
    }

    [Test]
    public void IsOwnArtist_InvalidatesCanFollowArtist()
    {
        // Asserted at the PropertyChanged level on purpose, not by re-reading CanFollowArtist.
        //
        // It is a computed property, so a test that sets the flag and then reads it recomputes from
        // the current fields and passes whether or not any notification was raised. Only a XAML
        // binding can tell the difference, and it tells it by never updating - the bell would stay
        // on screen over your own song. The same class of bug as the biometric status line.
        //
        // The flag is stamped by the coordinator AFTER the card has bound, which is exactly when
        // the notification is the only thing that can re-hide the bell.
        var song = new SongDto { Id = 1, PersonaId = 10 };
        var raised = new List<string?>();
        song.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        song.IsOwnArtist = true;

        Assert.That(raised, Does.Contain(nameof(SongDto.CanFollowArtist)));
    }
}
