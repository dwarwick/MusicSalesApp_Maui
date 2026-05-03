namespace MusicSalesApp.Maui.ViewModels;

public static class SongDisplayOrderSorter
{
    public static List<SongDto> OrderForLibrary(IEnumerable<SongDto> songs)
    {
        return songs
            .OrderBy(song => song.DisplayOrder.HasValue ? 1 : 0)
            .ThenBy(song => song.DisplayOrder ?? int.MaxValue)
            .ThenByDescending(song => song.DisplayOrder.HasValue ? int.MinValue : song.Id)
            .ThenBy(song => song.Id)
            .ToList();
    }

    public static List<SongDto> OrderById(IEnumerable<SongDto> songs)
    {
        return songs
            .OrderBy(song => song.Id)
            .ToList();
    }
}