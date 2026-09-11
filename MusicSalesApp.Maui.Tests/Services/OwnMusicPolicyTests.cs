using Moq;
using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Tests.Services;

/// <summary>
/// Whether a song is the signed-in user's own, which is what hides the follow bell.
/// </summary>
[TestFixture]
public class OwnMusicPolicyTests
{
    private const int MyUserId = 500;
    private const int MyCreatorId = 90;

    private Mock<IAuthService> _auth;

    [SetUp]
    public void SetUp()
    {
        _auth = new Mock<IAuthService>();
        _auth.SetupGet(a => a.IsLoggedIn).Returns(true);
        _auth.SetupGet(a => a.UserId).Returns(MyUserId);
        _auth.SetupGet(a => a.IsCreator).Returns(true);
        _auth.SetupGet(a => a.CreatorId).Returns(MyCreatorId);
    }

    private static SongDto Song(int? creatorId, int? creatorUserId) =>
        new() { Id = 1, PersonaId = 10, CreatorId = creatorId, CreatorUserId = creatorUserId };

    [Test]
    public void IsOwnArtist_TrueWhenTheCreatorUserIdMatches()
    {
        Assert.That(OwnMusicPolicy.IsOwnMusic(Song(null, MyUserId), _auth.Object), Is.True);
    }

    [Test]
    public void IsOwnArtist_TrueWhenTheCreatorIdMatches()
    {
        // Either identifier is enough: which one arrives populated depends on the query path that
        // built the DTO, and the server sends CreatorUserId as Creator?.UserId.
        Assert.That(OwnMusicPolicy.IsOwnMusic(Song(MyCreatorId, null), _auth.Object), Is.True);
    }

    [Test]
    public void IsOwnArtist_FalseForSomeoneElsesSong()
    {
        Assert.That(OwnMusicPolicy.IsOwnMusic(Song(91, 501), _auth.Object), Is.False);
    }

    [Test]
    public void IsOwnArtist_FalseWhenSignedOut()
    {
        _auth.SetupGet(a => a.IsLoggedIn).Returns(false);

        Assert.That(OwnMusicPolicy.IsOwnMusic(Song(MyCreatorId, MyUserId), _auth.Object), Is.False);
    }

    [Test]
    public void IsOwnArtist_FalseWhenBothSidesAreNull()
    {
        // THE trap. Both sides are int?, so `song.CreatorUserId == auth.UserId` is true when both
        // are null - which would hide the bell from every listener who is not a creator, on every
        // song whose Creator navigation was not eager-loaded.
        _auth.SetupGet(a => a.UserId).Returns((int?)null);
        _auth.SetupGet(a => a.CreatorId).Returns((int?)null);

        Assert.That(OwnMusicPolicy.IsOwnMusic(Song(null, null), _auth.Object), Is.False);
    }

    [Test]
    public void IsOwnArtist_FalseWhenTheSongCarriesNoOwner()
    {
        // Null on the song alone. Fails in the safe direction: the bell is offered and the server
        // refuses it with a 400 if the guess was wrong.
        Assert.That(OwnMusicPolicy.IsOwnMusic(Song(null, null), _auth.Object), Is.False);
    }

    [Test]
    public void IsOwnArtist_FalseWhenTheCallerHasNoCreatorIdentity()
    {
        // An ordinary listener. Nothing is theirs, so every bell stays available.
        _auth.SetupGet(a => a.IsCreator).Returns(false);
        _auth.SetupGet(a => a.CreatorId).Returns((int?)null);

        Assert.That(OwnMusicPolicy.IsOwnMusic(Song(MyCreatorId, 501), _auth.Object), Is.False);
    }

    [Test]
    public void IsOwnArtist_FalseForANullSongOrNullAuth()
    {
        Assert.Multiple(() =>
        {
            Assert.That(OwnMusicPolicy.IsOwnMusic(null, _auth.Object), Is.False);
            Assert.That(OwnMusicPolicy.IsOwnMusic(Song(MyCreatorId, MyUserId), null), Is.False);
        });
    }
}
