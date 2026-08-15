#if !ANDROID
using Microsoft.Extensions.Logging;

namespace MusicSalesApp.Maui.Services;

/// <summary>
/// Keeps the OS now-playing surface (iOS lock screen / Control Center) supplied with a decoded
/// artwork image for whichever track is currently playing.
///
/// <para>
/// Android needs nothing like this: Media3 is handed a bare artwork URI and resolves it itself.
/// Plugin.MediaManager's Apple notification manager instead reads <c>IMediaItem.DisplayImage</c> - an
/// already-decoded UIImage - and never looks at <c>ImageUri</c>. Nothing populates that property for
/// the <c>Play(IMediaItem)</c> overloads this app uses, because only the <c>Play(string)</c> and
/// <c>Play(FileInfo)</c> overloads run the metadata extractor. This class fills the gap.
/// </para>
///
/// <para>
/// The load is asynchronous and nothing waits on it. Plugin.MediaManager rebuilds
/// <c>MPNowPlayingInfoCenter.NowPlaying</c> from <c>Queue.Current</c> roughly once a second, so
/// assigning the image at any later moment is picked up on the next tick. That same heartbeat is what
/// drives retries, which is why <see cref="Observe"/> is built to be called constantly and to cost a
/// string compare and a null check once the artwork is in place.
/// </para>
/// </summary>
public sealed class NowPlayingArtworkCoordinator : IDisposable
{
    /// <summary>
    /// Attempts per track before giving up. Only attempts that actually reached the loader count -
    /// see the network gate in <see cref="Observe"/>.
    /// </summary>
    internal const int MaxAttemptsPerTrack = 4;

    /// <summary>
    /// Spacing between attempts, measured from when the previous one <em>finished</em>. Measuring from
    /// the start instead would collapse to no spacing at all whenever a fetch takes longer than this,
    /// which on a slow link it routinely does.
    /// </summary>
    internal static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(4);

    private readonly INowPlayingArtworkLoader _loader;
    private readonly ILogger<NowPlayingArtworkCoordinator> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly INetworkStatusService? _networkStatusService;
    private readonly object _sync = new();

    private INowPlayingArtworkTarget? _currentTarget;
    private string? _observedUri;
    private int _attemptCount;
    private DateTimeOffset _nextAttemptUtc = DateTimeOffset.MinValue;
    private CancellationTokenSource? _loadCts;
    private bool _loadInFlight;
    private bool _disposed;

    public NowPlayingArtworkCoordinator(
        INowPlayingArtworkLoader loader,
        ILogger<NowPlayingArtworkCoordinator> logger,
        INetworkStatusService? networkStatusService = null)
        : this(loader, logger, TimeProvider.System, networkStatusService)
    {
    }

    internal NowPlayingArtworkCoordinator(
        INowPlayingArtworkLoader loader,
        ILogger<NowPlayingArtworkCoordinator> logger,
        TimeProvider timeProvider,
        INetworkStatusService? networkStatusService = null)
    {
        _loader = loader;
        _logger = logger;
        _timeProvider = timeProvider;
        _networkStatusService = networkStatusService;
    }

    /// <summary>The load started by the most recent <see cref="Observe"/> call, or null. Test hook.</summary>
    internal Task? InFlightLoad { get; private set; }

    /// <summary>
    /// Point the coordinator at the item the OS is showing right now. Idempotent, never blocks, and
    /// safe to call from the notification heartbeat - which is exactly what makes an asynchronous
    /// load self-healing.
    /// </summary>
    public void Observe(INowPlayingArtworkTarget? target)
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            if (!ReferenceEquals(target, _currentTarget))
            {
                // The track moved on. Abandoning the previous fetch matters as much as starting the
                // next one: without it, one slow transfer holds the gate shut and every later track
                // goes without artwork until it times out.
                CancelInFlightLoad();
                ReleaseCurrentImage();
                _currentTarget = target;
                ResetAttempts(observedUri: null);
            }

            if (target is null)
            {
                return;
            }

            var artworkUri = target.ArtworkUri;
            if (string.IsNullOrWhiteSpace(artworkUri))
            {
                return;
            }

            if (!string.Equals(artworkUri, _observedUri, StringComparison.Ordinal))
            {
                // A later queue build resolved a different rendition - an artwork backfill landed.
                // Treat it as a fresh subject rather than a retry.
                CancelInFlightLoad();
                ResetAttempts(artworkUri);
            }
            else if (target.Image is not null)
            {
                // The steady state, hit once a second for the life of the track.
                return;
            }

            if (_loadInFlight || _attemptCount >= MaxAttemptsPerTrack)
            {
                return;
            }

            // A remote fetch cannot succeed with no network at all. Skipping *without consuming an
            // attempt* is deliberate: spending the budget here would permanently blacklist artwork
            // that will load perfectly once connectivity returns and the next heartbeat arrives. A
            // file:// URI is still attempted while offline - that is the whole point of the cache.
            if (_networkStatusService?.HasNoNetworkAccess == true && IsRemote(artworkUri))
            {
                return;
            }

            var now = _timeProvider.GetUtcNow();
            if (now < _nextAttemptUtc)
            {
                return;
            }

            _loadInFlight = true;
            _attemptCount++;
            _loadCts = new CancellationTokenSource();
            var cancellationToken = _loadCts.Token;

            // Task.Run rather than a bare async call: Observe can be reached from MediaItemChanged on
            // the main thread, and this guarantees no loader implementation can block it regardless of
            // how that loader is written.
            InFlightLoad = Task.Run(
                () => LoadAndApplyAsync(target, artworkUri, cancellationToken),
                CancellationToken.None);
        }
    }

    /// <summary>
    /// Which of the two artwork URIs a runtime carries should be loaded. The preferred one is the
    /// large rendition; see the remarks on <see cref="PlaybackMediaItem.AlbumImageUri"/>.
    /// </summary>
    public static string SelectArtworkUri(string? preferredUri, string? fallbackUri)
    {
        if (!string.IsNullOrWhiteSpace(preferredUri))
        {
            return preferredUri;
        }

        return string.IsNullOrWhiteSpace(fallbackUri) ? string.Empty : fallbackUri;
    }

    private async Task LoadAndApplyAsync(
        INowPlayingArtworkTarget target,
        string artworkUri,
        CancellationToken cancellationToken)
    {
        NowPlayingArtworkLoadResult result;
        try
        {
            result = await _loader
                .LoadAsync(artworkUri, target.ArtworkContentVersion, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            result = NowPlayingArtworkLoadResult.Retryable;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Now playing artwork load failed. Scheme={ArtworkScheme}; ArtworkPath={ArtworkPath}",
                DescribeScheme(artworkUri),
                DescribePath(artworkUri));
            result = NowPlayingArtworkLoadResult.Retryable;
        }

        lock (_sync)
        {
            _loadInFlight = false;

            if (_disposed ||
                !ReferenceEquals(target, _currentTarget) ||
                !string.Equals(target.ArtworkUri, artworkUri, StringComparison.Ordinal))
            {
                // Superseded while we were loading. This image was never handed to the media session,
                // so unlike the applied one it is safe to dispose eagerly - see ReleaseCurrentImage.
                // The retry clock is deliberately left alone: it belongs to whatever is current now.
                (result.Image as IDisposable)?.Dispose();
                return;
            }

            // Measured from completion, so a fetch that ran longer than RetryInterval still gets
            // spacing before the next attempt rather than firing again immediately.
            _nextAttemptUtc = _timeProvider.GetUtcNow() + RetryInterval;

            if (!result.HasImage)
            {
                if (!result.IsWorthRetrying)
                {
                    // Downloaded fine and still is not a decodable image. Re-fetching the same bytes
                    // on every heartbeat would achieve nothing, so stop for this track.
                    _attemptCount = MaxAttemptsPerTrack;
                }

                return;
            }

            if (!TryAssignImage(target, result.Image))
            {
                (result.Image as IDisposable)?.Dispose();
                return;
            }

            _logger.LogInformation(
                "Now playing artwork applied. Scheme={ArtworkScheme}; ArtworkPath={ArtworkPath}; AttemptCount={AttemptCount}",
                DescribeScheme(artworkUri),
                DescribePath(artworkUri),
                _attemptCount);
        }
    }

    /// <summary>
    /// Assigning runs through the media library's property setter, which raises PropertyChanged on
    /// whatever thread we are on. A throwing handler there must not escape: nothing awaits the
    /// fire-and-forget load, so it would surface as an unobserved task exception with no log line.
    /// </summary>
    private bool TryAssignImage(INowPlayingArtworkTarget target, object? image)
    {
        try
        {
            target.Image = image;
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not hand now playing artwork to the media session.");
            return false;
        }
    }

    private void ResetAttempts(string? observedUri)
    {
        _observedUri = observedUri;
        _attemptCount = 0;
        _nextAttemptUtc = DateTimeOffset.MinValue;
    }

    private void CancelInFlightLoad()
    {
        if (_loadCts is null)
        {
            return;
        }

        try
        {
            _loadCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        _loadCts.Dispose();
        _loadCts = null;

        // A cancelled load must not leave the gate shut against the track that replaced it.
        _loadInFlight = false;
    }

    /// <summary>
    /// Drops the reference to the decoded image so only one is ever live, whatever the queue length.
    ///
    /// <para>
    /// Deliberately does not dispose it. Plugin.MediaManager's one-second timer calls
    /// <c>UpdateNotification()</c> on a thread this class neither owns nor synchronises with, and that
    /// method wraps <c>DisplayImage</c> in a fresh <c>MPMediaItemArtwork</c>. Disposing the managed
    /// peer opens a window where that constructor sees a dead handle and throws on a pool thread -
    /// an unhandled exception, i.e. a crash - to reclaim a few megabytes a fraction of a second
    /// sooner. Dropping the reference is enough; the peer finalises and the native image is released.
    /// </para>
    /// </summary>
    private void ReleaseCurrentImage()
    {
        if (_currentTarget is null)
        {
            return;
        }

        TryAssignImage(_currentTarget, null);
        _currentTarget = null;
    }

    private static bool IsRemote(string artworkUri) =>
        Uri.TryCreate(artworkUri, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    // Artwork URLs are SAS-signed: the query string is a credential, so it never reaches the log.
    private static string DescribeScheme(string artworkUri) =>
        Uri.TryCreate(artworkUri, UriKind.Absolute, out var uri) ? uri.Scheme : "unknown";

    private static string DescribePath(string artworkUri) =>
        Uri.TryCreate(artworkUri, UriKind.Absolute, out var uri) ? uri.AbsolutePath : string.Empty;

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            CancelInFlightLoad();
            ReleaseCurrentImage();
        }
    }
}
#endif
