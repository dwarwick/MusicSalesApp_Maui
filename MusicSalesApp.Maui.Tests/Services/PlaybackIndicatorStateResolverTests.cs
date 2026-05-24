using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Tests.Services;

[TestFixture]
public class PlaybackIndicatorStateResolverTests
{
    [Test]
    public void Resolve_ReturnsPlayIcon_WhenSongIsNotPlaying()
    {
        var result = PlaybackIndicatorStateResolver.Resolve(isCurrentSongPlaying: false);

        Assert.That(result, Is.EqualTo(PlaybackIndicatorVisualState.PlayIcon));
    }

    [Test]
    public void Resolve_ReturnsEqualizer_WhenSongIsPlaying()
    {
        var result = PlaybackIndicatorStateResolver.Resolve(isCurrentSongPlaying: true);

        Assert.That(result, Is.EqualTo(PlaybackIndicatorVisualState.Equalizer));
    }

    [Test]
    public void ShouldToggleCurrentSong_ReturnsTrue_WhenSongMatchesCurrentSong()
    {
        var currentSong = new SongDto { Id = 42 };

        var result = PlaybackIndicatorStateResolver.ShouldToggleCurrentSong(42, currentSong);

        Assert.That(result, Is.True);
    }

    [Test]
    public void ShouldToggleCurrentSong_ReturnsFalse_WhenSongDoesNotMatchCurrentSong()
    {
        var currentSong = new SongDto { Id = 42 };

        var result = PlaybackIndicatorStateResolver.ShouldToggleCurrentSong(99, currentSong);

        Assert.That(result, Is.False);
    }

    [Test]
    public void ResolveLevels_ReturnsFallback_WhenLevelsMissing()
    {
        var result = PlaybackIndicatorStateResolver.ResolveLevels(null, animationPhase: 0d);

        Assert.That(result, Has.Count.EqualTo(AudioEqualizerBarProcessor.DefaultBands.Count));
        Assert.That(result.All(level => level is >= 0.18f and <= 0.76f), Is.True);
    }

    [Test]
    public void ResolveLevels_ReturnsProvidedLevels_WhenAvailable()
    {
        var levels = new[] { 0.1f, 0.2f, 0.3f };

        var result = PlaybackIndicatorStateResolver.ResolveLevels(levels, animationPhase: 0d);

        Assert.That(result, Is.SameAs(levels));
    }

    [Test]
    public void ResolveLevels_AnimatesFallbackAcrossPhases()
    {
        var firstFrame = PlaybackIndicatorStateResolver.ResolveLevels(null, animationPhase: 0d).ToArray();
        var secondFrame = PlaybackIndicatorStateResolver.ResolveLevels(null, animationPhase: 1.2d).ToArray();

        Assert.That(firstFrame, Has.Length.EqualTo(AudioEqualizerBarProcessor.DefaultBands.Count));
        Assert.That(secondFrame, Has.Length.EqualTo(AudioEqualizerBarProcessor.DefaultBands.Count));
        Assert.That(firstFrame.SequenceEqual(secondFrame), Is.False);
    }
}