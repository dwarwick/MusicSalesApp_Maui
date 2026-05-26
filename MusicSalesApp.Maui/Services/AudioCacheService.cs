using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Services;

public interface IAudioCacheService
{
    string GetImmediatePlaybackUri(SongDto song);

    Task<string> ResolvePlaybackUriAsync(SongDto song, CancellationToken cancellationToken = default);
}

public class AudioCacheService : IAudioCacheService
{
    public const string AudioDownloadClientName = "AudioDownload";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AudioCacheService> _logger;
    private readonly string _cacheDirectory;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _downloadLocks = new();

    public AudioCacheService(
        IHttpClientFactory httpClientFactory,
        ILogger<AudioCacheService> logger)
        : this(httpClientFactory, logger, Path.Combine(FileSystem.Current.CacheDirectory, "audio-playback"))
    {
    }

    public AudioCacheService(
        IHttpClientFactory httpClientFactory,
        ILogger<AudioCacheService> logger,
        string cacheDirectory)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _cacheDirectory = cacheDirectory;
    }

    public string GetImmediatePlaybackUri(SongDto song)
    {
        if (!TryGetRemoteUri(song, out var remoteUri))
        {
            return song.StreamUrl ?? string.Empty;
        }

        var cachePath = GetCachePath(song, remoteUri);
        return HasCachedFile(cachePath)
            ? cachePath
            : song.StreamUrl ?? string.Empty;
    }

    public async Task<string> ResolvePlaybackUriAsync(SongDto song, CancellationToken cancellationToken = default)
    {
        if (!TryGetRemoteUri(song, out var remoteUri))
        {
            return song.StreamUrl ?? string.Empty;
        }

        var cachePath = GetCachePath(song, remoteUri);
        if (HasCachedFile(cachePath))
        {
            return cachePath;
        }

        var downloadLock = _downloadLocks.GetOrAdd(cachePath, _ => new SemaphoreSlim(1, 1));
        await downloadLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (HasCachedFile(cachePath))
            {
                return cachePath;
            }

            Directory.CreateDirectory(_cacheDirectory);

            var client = _httpClientFactory.CreateClient(AudioDownloadClientName);
            using var response = await client.GetAsync(remoteUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Audio cache download failed for song {SongId}. StatusCode={StatusCode}; StreamUrl={StreamUrl}",
                    song.Id,
                    response.StatusCode,
                    song.StreamUrl);
                return song.StreamUrl ?? string.Empty;
            }

            var tempPath = cachePath + ".tmp";
            DeleteFileIfPresent(tempPath);

            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var destination = File.Create(tempPath))
            {
                await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
                await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(tempPath, cachePath, true);

            _logger.LogInformation(
                "Audio cache download completed for song {SongId}. CachePath={CachePath}",
                song.Id,
                cachePath);

            return cachePath;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Audio cache download cancelled for song {SongId}", song.Id);
            return song.StreamUrl ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Audio cache download failed for song {SongId}", song.Id);
            return song.StreamUrl ?? string.Empty;
        }
        finally
        {
            DeleteFileIfPresent(cachePath + ".tmp");
            downloadLock.Release();
        }
    }

    private static void DeleteFileIfPresent(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static bool HasCachedFile(string cachePath)
    {
        return File.Exists(cachePath) && new FileInfo(cachePath).Length > 0;
    }

    private static bool TryGetRemoteUri(SongDto song, out Uri remoteUri)
    {
        if (string.IsNullOrWhiteSpace(song.StreamUrl))
        {
            remoteUri = null!;
            return false;
        }

        return Uri.TryCreate(song.StreamUrl, UriKind.Absolute, out remoteUri!);
    }

    private string GetCachePath(SongDto song, Uri remoteUri)
    {
        var extension = Path.GetExtension(Uri.UnescapeDataString(remoteUri.AbsolutePath));
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".mp3";
        }

        var keySource = string.IsNullOrWhiteSpace(remoteUri.AbsolutePath)
            ? $"song-{song.Id}"
            : remoteUri.AbsolutePath.ToLowerInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(keySource))).ToLowerInvariant();

        return Path.Combine(_cacheDirectory, $"{song.Id}-{hash}{extension}");
    }
}