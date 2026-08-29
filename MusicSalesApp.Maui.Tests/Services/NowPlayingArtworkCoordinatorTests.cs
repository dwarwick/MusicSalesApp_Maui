using Microsoft.Extensions.Logging.Abstractions;
using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.Tests.Services;

/// <summary>
/// The when/whether/retry/release policy behind Apple lock-screen artwork. Deliberately free of both
/// UIKit and Plugin.MediaManager, which is the whole reason the INowPlayingArtworkTarget seam exists.
/// </summary>
[TestFixture]
public class NowPlayingArtworkCoordinatorTests
{
    private const string CachedFileUri = "file:///var/cache/image-cache/album.jpg";
    private const string RemoteArtworkUrl = "https://storage.test/images/1.jpg?sig=aaa";

    private FakeArtworkLoader _loader = null!;
    private TestNetworkStatusService _networkStatus = null!;
    private ManualTimeProvider _time = null!;
    private NowPlayingArtworkCoordinator _coordinator = null!;

    [SetUp]
    public void SetUp()
    {
        _loader = new FakeArtworkLoader();
        _networkStatus = new TestNetworkStatusService();
        _time = new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        _coordinator = new NowPlayingArtworkCoordinator(
            _loader,
            NullLogger<NowPlayingArtworkCoordinator>.Instance,
            _time,
            _networkStatus);
    }

    [TearDown]
    public void TearDown() => _coordinator.Dispose();

    private async Task ObserveAndSettleAsync(INowPlayingArtworkTarget? target)
    {
        _coordinator.Observe(target);

        if (_coordinator.InFlightLoad is { } load)
        {
            await load;
        }
    }

    [Test]
    public async Task Observe_WithACachedFileUri_SetsTheDecodedImageOnTheTarget()
    {
        var image = new object();
        _loader.Enqueue(NowPlayingArtworkLoadResult.Loaded(image));
        var target = new FakeArtworkTarget { ArtworkUri = CachedFileUri };

        await ObserveAndSettleAsync(target);

        Assert.That(target.Image, Is.SameAs(image));
        Assert.That(_loader.RequestedUris, Is.EqualTo(new[] { CachedFileUri }));
    }

    [Test]
    public async Task Observe_CalledRepeatedlyForTheSameTarget_LoadsOnce()
    {
        // The notification heartbeat calls this roughly once a second for the life of the track.
        _loader.Enqueue(NowPlayingArtworkLoadResult.Loaded(new object()));
        var target = new FakeArtworkTarget { ArtworkUri = CachedFileUri };

        await ObserveAndSettleAsync(target);
        for (var i = 0; i < 20; i++)
        {
            _coordinator.Observe(target);
        }

        Assert.That(_loader.CallCount, Is.EqualTo(1));
    }

    [Test]
    public async Task Observe_WhenTheTargetChanges_ClearsThePreviousTargetsImage()
    {
        // Only one decoded image is ever live, however long the queue is.
        _loader.Enqueue(
            NowPlayingArtworkLoadResult.Loaded(new object()),
            NowPlayingArtworkLoadResult.Loaded(new object()));
        var first = new FakeArtworkTarget { ArtworkUri = CachedFileUri };
        var second = new FakeArtworkTarget { ArtworkUri = CachedFileUri };

        await ObserveAndSettleAsync(first);
        await ObserveAndSettleAsync(second);

        Assert.That(first.Image, Is.Null);
        Assert.That(second.Image, Is.Not.Null);
    }

    [Test]
    public async Task Observe_WhenTheTargetChangesMidLoad_DiscardsAndDisposesTheLateResult()
    {
        var abandoned = new DisposableImage();
        var gate = new TaskCompletionSource();
        _loader.Gate = gate.Task;
        _loader.Enqueue(NowPlayingArtworkLoadResult.Loaded(abandoned));
        var first = new FakeArtworkTarget { ArtworkUri = CachedFileUri };
        var second = new FakeArtworkTarget { ArtworkUri = CachedFileUri };

        _coordinator.Observe(first);
        var abandonedLoad = _coordinator.InFlightLoad!;

        // Observe only queues the load onto the thread pool, so without this the supersede below can
        // land before the first load has started - and then the two in-flight loads claim their
        // results in thread-pool order rather than call order.
        await _loader.FirstLoadStarted;

        _coordinator.Observe(second);
        gate.SetResult();
        await abandonedLoad;

        Assert.That(abandoned.IsDisposed, Is.True, "a superseded image was never handed to the media session");
        Assert.That(first.Image, Is.Null);
        Assert.That(second.Image, Is.Null);
    }

    [Test]
    public void Observe_WithNoArtworkUri_NeverCallsTheLoader()
    {
        _coordinator.Observe(new FakeArtworkTarget { ArtworkUri = string.Empty });

        Assert.That(_loader.CallCount, Is.Zero);
        Assert.That(_coordinator.InFlightLoad, Is.Null);
    }

    [Test]
    public async Task Observe_AfterARetryableFailure_RetriesOnceTheRetryIntervalHasElapsed()
    {
        _loader.Enqueue(
            NowPlayingArtworkLoadResult.Retryable,
            NowPlayingArtworkLoadResult.Loaded(new object()));
        var target = new FakeArtworkTarget { ArtworkUri = CachedFileUri };

        await ObserveAndSettleAsync(target);
        _time.Advance(NowPlayingArtworkCoordinator.RetryInterval);
        await ObserveAndSettleAsync(target);

        Assert.That(_loader.CallCount, Is.EqualTo(2));
        Assert.That(target.Image, Is.Not.Null);
    }

    [Test]
    public async Task Observe_BeforeTheRetryIntervalElapses_DoesNotCallTheLoaderAgain()
    {
        _loader.Enqueue(NowPlayingArtworkLoadResult.Retryable);
        var target = new FakeArtworkTarget { ArtworkUri = CachedFileUri };

        await ObserveAndSettleAsync(target);
        _time.Advance(NowPlayingArtworkCoordinator.RetryInterval - TimeSpan.FromSeconds(1));
        await ObserveAndSettleAsync(target);

        Assert.That(_loader.CallCount, Is.EqualTo(1));
    }

    [Test]
    public async Task Observe_AfterAnUnretryableFailure_NeverRetriesThatTarget()
    {
        // A payload that downloaded perfectly and simply is not an image will never become one.
        // Re-fetching the same bytes on every heartbeat would achieve nothing.
        _loader.Enqueue(NowPlayingArtworkLoadResult.Unavailable);
        var target = new FakeArtworkTarget { ArtworkUri = RemoteArtworkUrl };

        await ObserveAndSettleAsync(target);
        for (var i = 0; i < 5; i++)
        {
            _time.Advance(NowPlayingArtworkCoordinator.RetryInterval);
            await ObserveAndSettleAsync(target);
        }

        Assert.That(_loader.CallCount, Is.EqualTo(1));
    }

    [Test]
    public async Task Observe_AfterMaxAttempts_StopsRetrying()
    {
        _loader.DefaultResult = NowPlayingArtworkLoadResult.Retryable;
        var target = new FakeArtworkTarget { ArtworkUri = CachedFileUri };

        for (var i = 0; i < NowPlayingArtworkCoordinator.MaxAttemptsPerTrack + 5; i++)
        {
            await ObserveAndSettleAsync(target);
            _time.Advance(NowPlayingArtworkCoordinator.RetryInterval);
        }

        Assert.That(_loader.CallCount, Is.EqualTo(NowPlayingArtworkCoordinator.MaxAttemptsPerTrack));
    }

    [Test]
    public async Task Observe_WithARemoteUriWhileOffline_DoesNotAttemptAndDoesNotConsumeAnAttempt()
    {
        // Spending the attempt budget here would permanently blacklist artwork that loads perfectly
        // once connectivity returns, which is exactly the case this has to survive.
        _loader.Enqueue(NowPlayingArtworkLoadResult.Loaded(new object()));
        _networkStatus.SetOffline(true);
        var target = new FakeArtworkTarget { ArtworkUri = RemoteArtworkUrl };

        for (var i = 0; i < 5; i++)
        {
            await ObserveAndSettleAsync(target);
        }

        Assert.That(_loader.CallCount, Is.Zero);

        _networkStatus.SetOffline(false);
        await ObserveAndSettleAsync(target);

        Assert.That(_loader.CallCount, Is.EqualTo(1), "the offline heartbeats must not have spent the budget");
        Assert.That(target.Image, Is.Not.Null);
    }

    [Test]
    public async Task Observe_PassesTheContentVersionToTheLoader()
    {
        // Without it a downloaded hero is cached under version 0 - a duplicate file under the wrong
        // key, which also keeps serving pre-crop artwork after a re-crop overwrites the same blob path.
        _loader.Enqueue(NowPlayingArtworkLoadResult.Loaded(new object()));
        var target = new FakeArtworkTarget { ArtworkUri = RemoteArtworkUrl, ArtworkContentVersion = 7 };

        await ObserveAndSettleAsync(target);

        Assert.That(_loader.RequestedVersions, Is.EqualTo(new[] { 7 }));
    }

    [Test]
    public async Task Observe_WithAFileUriWhileOffline_StillLoads()
    {
        // The offline gate is scheme-specific: a cached file is exactly what offline playback needs.
        _loader.Enqueue(NowPlayingArtworkLoadResult.Loaded(new object()));
        _networkStatus.SetOffline(true);
        var target = new FakeArtworkTarget { ArtworkUri = CachedFileUri };

        await ObserveAndSettleAsync(target);

        Assert.That(target.Image, Is.Not.Null);
    }

    [Test]
    public async Task Observe_WhenTheArtworkUriChangesForTheSameTarget_LoadsTheNewOne()
    {
        // A later queue build resolved a different rendition - an artwork backfill landed.
        _loader.Enqueue(
            NowPlayingArtworkLoadResult.Unavailable,
            NowPlayingArtworkLoadResult.Loaded(new object()));
        var target = new FakeArtworkTarget { ArtworkUri = RemoteArtworkUrl };

        await ObserveAndSettleAsync(target);
        target.ArtworkUri = CachedFileUri;
        await ObserveAndSettleAsync(target);

        Assert.That(_loader.RequestedUris, Is.EqualTo(new[] { RemoteArtworkUrl, CachedFileUri }));
        Assert.That(target.Image, Is.Not.Null);
    }

    [Test]
    public async Task Observe_WithNull_ClearsTheCurrentTargetsImage()
    {
        _loader.Enqueue(NowPlayingArtworkLoadResult.Loaded(new object()));
        var target = new FakeArtworkTarget { ArtworkUri = CachedFileUri };

        await ObserveAndSettleAsync(target);
        await ObserveAndSettleAsync(null);

        Assert.That(target.Image, Is.Null);
    }

    [Test]
    public async Task Observe_WhenTheLoaderThrows_DoesNotPropagateAndIsTreatedAsRetryable()
    {
        _loader.Throw = true;
        var target = new FakeArtworkTarget { ArtworkUri = CachedFileUri };

        await ObserveAndSettleAsync(target);

        Assert.That(target.Image, Is.Null);

        _loader.Throw = false;
        _loader.Enqueue(NowPlayingArtworkLoadResult.Loaded(new object()));
        _time.Advance(NowPlayingArtworkCoordinator.RetryInterval);
        await ObserveAndSettleAsync(target);

        Assert.That(target.Image, Is.Not.Null);
    }

    [Test]
    public async Task Dispose_ClearsTheCurrentTargetsImage()
    {
        _loader.Enqueue(NowPlayingArtworkLoadResult.Loaded(new object()));
        var target = new FakeArtworkTarget { ArtworkUri = CachedFileUri };
        await ObserveAndSettleAsync(target);

        _coordinator.Dispose();

        Assert.That(target.Image, Is.Null);
    }

    [Test]
    public async Task Observe_AfterDispose_DoesNothing()
    {
        _coordinator.Dispose();

        await ObserveAndSettleAsync(new FakeArtworkTarget { ArtworkUri = CachedFileUri });

        Assert.That(_loader.CallCount, Is.Zero);
    }

    [TestCase("hero", "thumb", ExpectedResult = "hero")]
    [TestCase("", "thumb", ExpectedResult = "thumb")]
    [TestCase("   ", "thumb", ExpectedResult = "thumb")]
    [TestCase(null, null, ExpectedResult = "")]
    [TestCase("", "", ExpectedResult = "")]
    public string SelectArtworkUri_PrefersTheLargeRenditionAndFallsBack(string? preferred, string? fallback) =>
        NowPlayingArtworkCoordinator.SelectArtworkUri(preferred, fallback);

    [Test]
    public async Task NoOpNowPlayingArtworkLoader_ReturnsUnavailable()
    {
        var result = await new NoOpNowPlayingArtworkLoader().LoadAsync(CachedFileUri);

        Assert.That(result.HasImage, Is.False);
        Assert.That(result.IsWorthRetrying, Is.False);
    }

    private sealed class FakeArtworkTarget : INowPlayingArtworkTarget
    {
        public string ArtworkUri { get; set; } = string.Empty;

        public int ArtworkContentVersion { get; set; }

        public object? Image { get; set; }
    }

    private sealed class DisposableImage : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose() => IsDisposed = true;
    }

    /// <summary>
    /// Stand-in loader.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It has to be thread-safe, and it has to take its result at call entry.</b> The coordinator
    /// starts a new load without awaiting the previous one, so a superseding test genuinely has two
    /// loads running on the thread pool at once - and they block on the same <see cref="Gate"/>, so
    /// they are released together.
    /// </para>
    /// <para>
    /// Dequeuing after the gate made which caller got which result a coin toss, which is what made
    /// the supersede test fail roughly one run in eight: when the second load won the race it took
    /// the enqueued image and applied it, leaving the first load with nothing to dispose. Taking the
    /// result up front pins each caller to the result its call order earned.
    /// </para>
    /// </remarks>
    private sealed class FakeArtworkLoader : INowPlayingArtworkLoader
    {
        private readonly Queue<NowPlayingArtworkLoadResult> _results = new();
        private readonly TaskCompletionSource _firstLoadStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Lock _sync = new();

        public int CallCount { get; private set; }

        public List<string> RequestedUris { get; } = [];

        public NowPlayingArtworkLoadResult DefaultResult { get; set; } = NowPlayingArtworkLoadResult.Unavailable;

        /// <summary>Held open to keep a load in flight while the test moves the current item on.</summary>
        public Task? Gate { get; set; }

        public bool Throw { get; set; }

        /// <summary>
        /// Completes once a load has entered the loader and claimed its result.
        /// </summary>
        /// <remarks>
        /// A test that supersedes an in-flight load must await this first. Without it the supersede
        /// can land before the first load has even begun - <c>Observe</c> only queues the work onto
        /// the thread pool - and then the two loads claim their results in whatever order the pool
        /// happens to run them.
        /// </remarks>
        public Task FirstLoadStarted => _firstLoadStarted.Task;

        public void Enqueue(params NowPlayingArtworkLoadResult[] results)
        {
            lock (_sync)
            {
                foreach (var result in results)
                {
                    _results.Enqueue(result);
                }
            }
        }

        public List<int> RequestedVersions { get; } = [];

        public async Task<NowPlayingArtworkLoadResult> LoadAsync(
            string artworkUri,
            int contentVersion = 0,
            CancellationToken cancellationToken = default)
        {
            NowPlayingArtworkLoadResult result;
            lock (_sync)
            {
                CallCount++;
                RequestedUris.Add(artworkUri);
                RequestedVersions.Add(contentVersion);
                result = _results.Count > 0 ? _results.Dequeue() : DefaultResult;
            }

            _firstLoadStarted.TrySetResult();

            if (Gate is { } gate)
            {
                await gate;
            }

            if (Throw)
            {
                throw new InvalidOperationException("decode failed");
            }

            return result;
        }
    }
}
