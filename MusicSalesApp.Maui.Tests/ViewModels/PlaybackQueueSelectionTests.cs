using Moq;
using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Tests.ViewModels;

[TestFixture]
public class PlaybackQueueSelectionTests
{
    private static readonly List<SongDto> Songs =
    [
        new() { Id = 1, SongTitle = "First" },
        new() { Id = 2, SongTitle = "Second" },
        new() { Id = 3, SongTitle = "Third" },
    ];

    [Test]
    public void TryResolveCurrentSongIndex_WhenThePlayingSongIsInTheList_ReturnsItsIndex()
    {
        var playbackService = GivenCurrentSong(Songs[2]);

        Assert.That(PlaybackQueueSelection.TryResolveCurrentSongIndex(playbackService, Songs), Is.EqualTo(2));
    }

    [Test]
    public void TryResolveCurrentSongIndex_WhenThePlayingSongIsFilteredOut_ReturnsMinusOne()
    {
        // The distinction from ResolveCurrentSongIndex, which answers 0 here. A caller that SCROLLS
        // must not take that as "the top" - the list would lose its place whenever a filter hid the
        // playing song.
        var playbackService = GivenCurrentSong(new SongDto { Id = 99, SongTitle = "Elsewhere" });

        Assert.That(PlaybackQueueSelection.TryResolveCurrentSongIndex(playbackService, Songs), Is.EqualTo(-1));
    }

    [Test]
    public void TryResolveCurrentSongIndex_WithNothingPlaying_ReturnsMinusOne()
    {
        var playbackService = GivenCurrentSong(null);

        Assert.That(PlaybackQueueSelection.TryResolveCurrentSongIndex(playbackService, Songs), Is.EqualTo(-1));
    }

    [Test]
    public void TryResolveCurrentSongIndex_AgainstAnEmptyList_ReturnsMinusOne()
    {
        var playbackService = GivenCurrentSong(Songs[0]);

        Assert.That(PlaybackQueueSelection.TryResolveCurrentSongIndex(playbackService, []), Is.EqualTo(-1));
    }

    [Test]
    public void TryResolveCurrentSongIndex_MatchesOnIdRatherThanReference()
    {
        // The offline layer and the live catalogue hand out different instances of the same song.
        var playbackService = GivenCurrentSong(new SongDto { Id = 2, SongTitle = "Second" });

        Assert.That(PlaybackQueueSelection.TryResolveCurrentSongIndex(playbackService, Songs), Is.EqualTo(1));
    }

    private static IPlaybackService GivenCurrentSong(SongDto? song)
    {
        var mock = new Mock<IPlaybackService>();
        mock.SetupGet(service => service.CurrentSong).Returns(song);
        return mock.Object;
    }
}
