using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace MusicSalesApp.Maui.Services;

public sealed class RollingFileLoggerProvider : ILoggerProvider, IAsyncDisposable
{
    internal const int QueueCapacity = 1024;
    internal const int MaximumBatchSize = 64;
    internal static readonly TimeSpan MaximumBatchDelay = TimeSpan.FromMilliseconds(250);
    // Bounded window the synchronous Dispose() waits for the writer to drain buffered entries,
    // so the final log lines before shutdown/crash reach disk without blocking teardown for long.
    internal static readonly TimeSpan DisposeDrainTimeout = TimeSpan.FromMilliseconds(750);

    private readonly RollingFileLoggerOptions _options;
    private readonly ConcurrentDictionary<string, RollingFileLogger> _loggers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Channel<RollingFileLogEntry> _entries;
    private readonly Channel<FlushRequest> _flushRequests;
    private readonly SemaphoreSlim _workerSignal = new(0, 1);
    private readonly IRollingFileLogSink _sink;
    private readonly Task _writerTask;
    private int _disposeRequested;

    public RollingFileLoggerProvider(RollingFileLoggerOptions options)
        : this(options, new RollingFileLogSink(options))
    {
    }

    internal RollingFileLoggerProvider(RollingFileLoggerOptions options, IRollingFileLogSink sink)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(sink);

        _options = options;
        _sink = sink;
        _entries = Channel.CreateBounded<RollingFileLogEntry>(new BoundedChannelOptions(QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
        _flushRequests = Channel.CreateUnbounded<FlushRequest>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

        _writerTask = Task.Run(ProcessEntriesAsync);
    }

    public ILogger CreateLogger(string categoryName)
        => _loggers.GetOrAdd(categoryName, category => new RollingFileLogger(category, this));

    public void Dispose()
    {
        RequestDispose();
        try
        {
            // Drain buffered entries before returning so shutdown/crash diagnostics aren't lost.
            // Bounded so a stuck writer can never hang application teardown.
            _writerTask.Wait(DisposeDrainTimeout);
        }
        catch
        {
            // Logger teardown must never throw during shutdown.
        }

        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        RequestDispose();
        await _writerTask.ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    internal bool IsEnabled(string categoryName, LogLevel logLevel)
    {
        if (Volatile.Read(ref _disposeRequested) != 0 ||
            logLevel < _options.MinimumLevel ||
            logLevel == LogLevel.None)
        {
            return false;
        }

        return _options.CategoryFilter?.Invoke(categoryName, logLevel) ?? true;
    }

    internal void Write(LogLevel logLevel, string categoryName, EventId eventId, string message, Exception? exception)
    {
        if (!IsEnabled(categoryName, logLevel))
        {
            return;
        }

        var timestamp = DateTimeOffset.Now;
        _entries.Writer.TryWrite(new RollingFileLogEntry(
            timestamp,
            FormatLogLine(timestamp, logLevel, categoryName, eventId, message, exception)));
        SignalWorker();
    }

    internal Task FlushAsync(CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _disposeRequested) != 0)
        {
            return _writerTask.WaitAsync(cancellationToken);
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_flushRequests.Writer.TryWrite(new FlushRequest(completion)))
        {
            return _writerTask.WaitAsync(cancellationToken);
        }

        SignalWorker();
        return completion.Task.WaitAsync(cancellationToken);
    }

    private async Task ProcessEntriesAsync()
    {
        try
        {
            await TryInitializeSinkAsync().ConfigureAwait(false);

            while (true)
            {
                if (_flushRequests.Reader.TryRead(out var flushRequest))
                {
                    await ProcessFlushRequestsAsync(flushRequest).ConfigureAwait(false);
                    continue;
                }

                if (_entries.Reader.TryRead(out var firstEntry))
                {
                    await WriteBufferedBatchAsync(firstEntry).ConfigureAwait(false);
                    continue;
                }

                if (_entries.Reader.Completion.IsCompleted && _flushRequests.Reader.Completion.IsCompleted)
                {
                    break;
                }

                await _workerSignal.WaitAsync().ConfigureAwait(false);
            }

            await DrainEntriesAsync().ConfigureAwait(false);
            await TryFlushSinkAsync().ConfigureAwait(false);
        }
        finally
        {
            CompleteOutstandingFlushRequests();
            await _sink.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task WriteBufferedBatchAsync(RollingFileLogEntry firstEntry)
    {
        var batch = new List<RollingFileLogEntry>(MaximumBatchSize) { firstEntry };
        var batchStartedTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();

        while (batch.Count < MaximumBatchSize)
        {
            while (batch.Count < MaximumBatchSize && _entries.Reader.TryRead(out var entry))
            {
                batch.Add(entry);
            }

            if (batch.Count >= MaximumBatchSize ||
                System.Diagnostics.Stopwatch.GetElapsedTime(batchStartedTimestamp) >= MaximumBatchDelay ||
                _flushRequests.Reader.TryPeek(out _) ||
                _entries.Reader.Completion.IsCompleted)
            {
                break;
            }

            var remainingDelay = MaximumBatchDelay - System.Diagnostics.Stopwatch.GetElapsedTime(batchStartedTimestamp);
            if (remainingDelay <= TimeSpan.Zero)
            {
                break;
            }

            await _workerSignal.WaitAsync(remainingDelay).ConfigureAwait(false);
        }

        await TryWriteBatchAsync(batch).ConfigureAwait(false);
        await TryFlushSinkAsync().ConfigureAwait(false);
    }

    private async Task ProcessFlushRequestsAsync(FlushRequest firstRequest)
    {
        var requests = new List<FlushRequest> { firstRequest };
        while (_flushRequests.Reader.TryRead(out var request))
        {
            requests.Add(request);
        }

        await DrainEntriesAsync().ConfigureAwait(false);
        await TryFlushSinkAsync().ConfigureAwait(false);

        foreach (var flushRequest in requests)
        {
            flushRequest.Completion.TrySetResult();
        }
    }

    private async Task DrainEntriesAsync()
    {
        var batch = new List<RollingFileLogEntry>(MaximumBatchSize);
        while (_entries.Reader.TryRead(out var entry))
        {
            batch.Add(entry);
            if (batch.Count < MaximumBatchSize)
            {
                continue;
            }

            await TryWriteBatchAsync(batch).ConfigureAwait(false);
            batch.Clear();
        }

        if (batch.Count > 0)
        {
            await TryWriteBatchAsync(batch).ConfigureAwait(false);
        }
    }

    private async Task TryInitializeSinkAsync()
    {
        try
        {
            await _sink.InitializeAsync(DateTimeOffset.Now, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // A later write retries initialization. Logging must never take down the app.
        }
    }

    private async Task TryWriteBatchAsync(IReadOnlyList<RollingFileLogEntry> batch)
    {
        try
        {
            await _sink.WriteBatchAsync(batch, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // Logging failures are intentionally isolated from app work.
        }
    }

    private async Task TryFlushSinkAsync()
    {
        try
        {
            await _sink.FlushAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // A flush failure must not fault callers or app shutdown.
        }
    }

    private void CompleteOutstandingFlushRequests()
    {
        while (_flushRequests.Reader.TryRead(out var request))
        {
            request.Completion.TrySetResult();
        }
    }

    private void RequestDispose()
    {
        if (Interlocked.Exchange(ref _disposeRequested, 1) != 0)
        {
            return;
        }

        _loggers.Clear();
        _entries.Writer.TryComplete();
        _flushRequests.Writer.TryComplete();
        SignalWorker();
    }

    private void SignalWorker()
    {
        try
        {
            _workerSignal.Release();
        }
        catch (SemaphoreFullException)
        {
            // A wake-up is already pending; one signal is enough for the single reader.
        }
    }

    private static string FormatLogLine(
        DateTimeOffset timestamp,
        LogLevel logLevel,
        string categoryName,
        EventId eventId,
        string message,
        Exception? exception)
    {
        var line = $"{timestamp:O} [{logLevel}] {categoryName}({eventId.Id}:{eventId.Name}) {message}";
        return exception == null ? line : line + Environment.NewLine + exception;
    }

    private sealed record FlushRequest(TaskCompletionSource Completion);
}
