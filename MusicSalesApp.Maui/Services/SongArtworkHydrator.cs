using Microsoft.Maui.ApplicationModel;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Services;

/// <summary>
/// Points each song's artwork at its locally cached copy where one exists, so covers render offline
/// and without a network round-trip online.
/// </summary>
public interface ISongArtworkHydrator
{
    Task HydrateAsync(IReadOnlyList<SongDto> songs, CancellationToken cancellationToken = default);
}

public sealed class SongArtworkHydrator : ISongArtworkHydrator
{
    private readonly IImageCacheService _imageCacheService;
    private readonly INetworkStatusService? _networkStatusService;

    public SongArtworkHydrator(
        IImageCacheService imageCacheService,
        INetworkStatusService? networkStatusService = null)
    {
        _imageCacheService = imageCacheService;
        _networkStatusService = networkStatusService;
    }

    public async Task HydrateAsync(IReadOnlyList<SongDto> songs, CancellationToken cancellationToken = default)
    {
        if (songs.Count == 0)
        {
            return;
        }

        // Only suppress the remote URL when there is certainly no network. On a constrained connection
        // the remote image usually still loads, and blanking it would be a visible regression.
        var hasNoNetwork = _networkStatusService?.HasNoNetworkAccess == true;

        // Resolving paths is a File.Exists per image, so it happens off the UI thread even though it is
        // individually cheap - a full library refresh is hundreds of stat calls.
        var resolved = await Task.Run(
            () => songs
                .Select(song => (
                    Song: song,
                    AlbumArtPath: _imageCacheService.TryGetCachedImagePath(song.AlbumArtUrl),
                    PersonaImagePath: _imageCacheService.TryGetCachedImagePath(song.PersonaImageUrl)))
                .ToList(),
            cancellationToken).ConfigureAwait(false);

        // Applying happens on the UI thread: these are ObservableProperty setters feeding live
        // CollectionView bindings, and raising PropertyChanged off-thread crashes the list.
        RunOnMainThread(() =>
        {
            foreach (var (song, albumArtPath, personaImagePath) in resolved)
            {
                song.CachedAlbumArtPath = albumArtPath;
                song.CachedPersonaImagePath = personaImagePath;
                song.SuppressRemoteArtwork = hasNoNetwork;
            }
        });
    }

    private static void RunOnMainThread(Action action)
    {
        try
        {
            if (MainThread.IsMainThread)
            {
                action();
                return;
            }

            MainThread.BeginInvokeOnMainThread(action);
        }
        catch (Exception ex) when (
            ex is NotImplementedException
            || ex.GetType().Name == "NotImplementedInReferenceAssemblyException")
        {
            // Unit tests run against the reference assembly, where MainThread throws. Same guard as
            // NetworkStatusService.
            action();
        }
    }
}
