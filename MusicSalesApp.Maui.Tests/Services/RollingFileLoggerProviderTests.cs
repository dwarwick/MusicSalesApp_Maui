using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.Tests.Services;

[TestFixture]
public class RollingFileLoggerProviderTests
{
    private string _logDirectory = string.Empty;

    [SetUp]
    public void Setup()
    {
        _logDirectory = Path.Combine(Path.GetTempPath(), "StreamTunesLoggerTests", Guid.NewGuid().ToString("N"));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_logDirectory))
        {
            Directory.Delete(_logDirectory, recursive: true);
        }
    }

    [Test]
    public async Task Log_WritesToRuntimeLogFile_AfterFlush()
    {
        await using var provider = CreateProvider();
        var logger = provider.CreateLogger("TestCategory");

        logger.LogInformation("Playback diagnostic {Value}", 123);
        await provider.FlushAsync();

        var logFile = Directory.GetFiles(_logDirectory, "streamtunes-*.log").Single();
        var contents = await ReadSharedTextAsync(logFile);
        Assert.Multiple(() =>
        {
            Assert.That(contents, Does.Contain("Playback diagnostic 123"));
            Assert.That(contents, Does.Contain("TestCategory"));
        });
    }

    [Test]
    public async Task WriterStartup_DeletesLogsOlderThanRetentionWindow()
    {
        Directory.CreateDirectory(_logDirectory);
        var oldLog = Path.Combine(_logDirectory, "streamtunes-old.log");
        var recentLog = Path.Combine(_logDirectory, "streamtunes-recent.log");
        await File.WriteAllTextAsync(oldLog, "old");
        await File.WriteAllTextAsync(recentLog, "recent");
        File.SetLastWriteTime(oldLog, DateTime.Now.AddDays(-31));
        File.SetLastWriteTime(recentLog, DateTime.Now.AddDays(-2));

        await using var provider = CreateProvider();
        await provider.FlushAsync();

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(oldLog), Is.False);
            Assert.That(File.Exists(recentLog), Is.True);
        });
    }

    [Test]
    public async Task IsEnabled_RespectsMinimumLevel()
    {
        await using var provider = CreateProvider(LogLevel.Warning);
        var logger = provider.CreateLogger("TestCategory");

        logger.LogInformation("ignored");
        logger.LogWarning("written");
        await provider.FlushAsync();

        var contents = await ReadSharedTextAsync(Directory.GetFiles(_logDirectory, "streamtunes-*.log").Single());
        Assert.Multiple(() =>
        {
            Assert.That(contents, Does.Not.Contain("ignored"));
            Assert.That(contents, Does.Contain("written"));
        });
    }

    [Test]
    public async Task Log_UsesCategoryFilterWhenProvided()
    {
        var options = new RollingFileLoggerOptions
        {
            DirectoryPath = _logDirectory,
            RetentionDays = 30,
            MinimumLevel = LogLevel.Information,
            CategoryFilter = (categoryName, _) => categoryName == "AllowedCategory"
        };
        await using var provider = new RollingFileLoggerProvider(options);
        var allowedLogger = provider.CreateLogger("AllowedCategory");
        var ignoredLogger = provider.CreateLogger("IgnoredCategory");

        allowedLogger.LogInformation("written");
        ignoredLogger.LogInformation("ignored");
        await provider.FlushAsync();

        var contents = await ReadSharedTextAsync(Directory.GetFiles(_logDirectory, "streamtunes-*.log").Single());
        Assert.Multiple(() =>
        {
            Assert.That(contents, Does.Contain("written"));
            Assert.That(contents, Does.Not.Contain("ignored"));
        });
    }

    [Test]
    public async Task Log_DoesNotWaitForBlockedSink()
    {
        var sink = new RecordingLogSink { BlockWrites = true };
        await using var provider = new RollingFileLoggerProvider(CreateOptions(), sink);
        var logger = provider.CreateLogger("TestCategory");

        logger.LogInformation("first");
        await sink.WriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var stopwatch = Stopwatch.StartNew();
        logger.LogInformation("caller stays responsive");
        stopwatch.Stop();

        Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromMilliseconds(100)));
        sink.ReleaseWrites();
        await provider.FlushAsync();
    }

    [Test]
    public async Task QueueOverflow_DropsOldestEntriesAndRetainsNewest()
    {
        var sink = new RecordingLogSink { BlockWrites = true };
        await using var provider = new RollingFileLoggerProvider(CreateOptions(), sink);
        var logger = provider.CreateLogger("TestCategory");

        logger.LogInformation("seed");
        await sink.WriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        for (var index = 0; index < RollingFileLoggerProvider.QueueCapacity + 20; index++)
        {
            logger.LogInformation("overflow-{Index}", index);
        }

        sink.ReleaseWrites();
        await provider.FlushAsync();

        var lines = sink.Lines.ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(lines.Any(line => line.Contains("overflow-0", StringComparison.Ordinal)), Is.False);
            Assert.That(lines.Any(line => line.Contains($"overflow-{RollingFileLoggerProvider.QueueCapacity + 19}", StringComparison.Ordinal)), Is.True);
        });
    }

    [Test]
    public async Task Flush_WritesBatchesNoLargerThanConfiguredMaximum()
    {
        var sink = new RecordingLogSink();
        await using var provider = new RollingFileLoggerProvider(CreateOptions(), sink);
        var logger = provider.CreateLogger("TestCategory");
        for (var index = 0; index < 130; index++)
        {
            logger.LogInformation("batch-{Index}", index);
        }

        await provider.FlushAsync();

        Assert.That(sink.BatchSizes, Is.Not.Empty);
        Assert.That(sink.BatchSizes, Has.All.LessThanOrEqualTo(RollingFileLoggerProvider.MaximumBatchSize));
        Assert.That(sink.BatchSizes.Sum(), Is.EqualTo(130));
    }

    [Test]
    public async Task WriterFailure_DoesNotFaultLoggingAndLaterBatchesRecover()
    {
        var sink = new RecordingLogSink { FailuresRemaining = 1 };
        await using var provider = new RollingFileLoggerProvider(CreateOptions(), sink);
        var logger = provider.CreateLogger("TestCategory");

        logger.LogInformation("failed-batch");
        await provider.FlushAsync();
        logger.LogInformation("recovered-batch");
        await provider.FlushAsync();

        Assert.That(sink.Lines.Any(line => line.Contains("recovered-batch", StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public async Task Dispose_DoesNotSynchronouslyWaitForBlockedWriter()
    {
        var sink = new RecordingLogSink { BlockWrites = true };
        var provider = new RollingFileLoggerProvider(CreateOptions(), sink);
        provider.CreateLogger("TestCategory").LogInformation("blocked");
        await sink.WriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var stopwatch = Stopwatch.StartNew();
        provider.Dispose();
        stopwatch.Stop();

        Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromMilliseconds(100)));
        sink.ReleaseWrites();
        await provider.DisposeAsync();
    }

    private RollingFileLoggerProvider CreateProvider(LogLevel minimumLevel = LogLevel.Information)
        => new(CreateOptions(minimumLevel));

    private static async Task<string> ReadSharedTextAsync(string path)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    private RollingFileLoggerOptions CreateOptions(LogLevel minimumLevel = LogLevel.Information)
        => new()
        {
            DirectoryPath = _logDirectory,
            RetentionDays = 30,
            MinimumLevel = minimumLevel
        };

    private sealed class RecordingLogSink : IRollingFileLogSink
    {
        private readonly TaskCompletionSource _releaseWrites = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _failuresRemaining;

        public ConcurrentQueue<string> Lines { get; } = new();

        public ConcurrentQueue<int> BatchSizes { get; } = new();

        public TaskCompletionSource WriteStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool BlockWrites { get; init; }

        public int FailuresRemaining
        {
            get => Volatile.Read(ref _failuresRemaining);
            set => Volatile.Write(ref _failuresRemaining, value);
        }

        public Task InitializeAsync(DateTimeOffset now, CancellationToken cancellationToken) => Task.CompletedTask;

        public async Task WriteBatchAsync(IReadOnlyList<RollingFileLogEntry> entries, CancellationToken cancellationToken)
        {
            WriteStarted.TrySetResult();
            if (BlockWrites)
            {
                await _releaseWrites.Task.WaitAsync(cancellationToken);
            }

            if (Interlocked.Decrement(ref _failuresRemaining) >= 0)
            {
                throw new IOException("Test write failure");
            }

            BatchSizes.Enqueue(entries.Count);
            foreach (var entry in entries)
            {
                Lines.Enqueue(entry.Line);
            }
        }

        public Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void ReleaseWrites() => _releaseWrites.TrySetResult();
    }
}
