using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Tests.ViewModels;

[TestFixture]
public class SongDisplayOrderSorterTests
{
    [Test]
    public void OrderForLibrary_PlacesNullDisplayOrdersFirst()
    {
        var ordered = SongDisplayOrderSorter.OrderForLibrary(
        [
            new SongDto { Id = 10, SongTitle = "Ranked One", DisplayOrder = 1 },
            new SongDto { Id = 40, SongTitle = "Ranked Two", DisplayOrder = 2 },
            new SongDto { Id = 30, SongTitle = "Null Newest", DisplayOrder = null },
            new SongDto { Id = 20, SongTitle = "Null Older", DisplayOrder = null }
        ]);

        Assert.That(ordered.Select(song => song.SongTitle), Is.EqualTo(new[]
        {
            "Null Newest",
            "Null Older",
            "Ranked One",
            "Ranked Two"
        }));
    }

    [Test]
    public void OrderById_ReturnsAscendingIdOrder()
    {
        var ordered = SongDisplayOrderSorter.OrderById(
        [
            new SongDto { Id = 9, SongTitle = "Nine" },
            new SongDto { Id = 2, SongTitle = "Two" },
            new SongDto { Id = 5, SongTitle = "Five" }
        ]);

        Assert.That(ordered.Select(song => song.Id), Is.EqualTo(new[] { 2, 5, 9 }));
    }
}