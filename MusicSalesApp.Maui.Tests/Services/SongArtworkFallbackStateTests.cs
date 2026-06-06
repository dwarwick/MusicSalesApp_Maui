using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.Tests.Services;

[TestFixture]
public class SongArtworkFallbackStateTests
{
    [TestCase(null, false)]
    [TestCase("", false)]
    [TestCase("   ", false)]
    [TestCase("https://streamtunes.net/art.jpg", true)]
    public void HasAlbumArt_OnlyTrueForNonBlankUrl(string? albumArtUrl, bool expected)
    {
        Assert.That(SongArtworkFallbackState.HasAlbumArt(albumArtUrl), Is.EqualTo(expected));
    }
}
