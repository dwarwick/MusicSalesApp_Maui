namespace MusicSalesApp.Maui.Services;

internal sealed record RollingFileLogEntry(DateTimeOffset Timestamp, string Line);

internal interface IRollingFileLogSink : IAsyncDisposable
{
    Task InitializeAsync(DateTimeOffset now, CancellationToken cancellationToken);

    Task WriteBatchAsync(IReadOnlyList<RollingFileLogEntry> entries, CancellationToken cancellationToken);

    Task FlushAsync(CancellationToken cancellationToken);
}

internal sealed class RollingFileLogSink : IRollingFileLogSink
{
    private readonly RollingFileLoggerOptions _options;
    private StreamWriter? _writer;
    private DateOnly? _writerDate;
    private bool _initialized;

    public RollingFileLogSink(RollingFileLoggerOptions options)
    {
        _options = options;
    }

    public Task InitializeAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialized(now);
        return Task.CompletedTask;
    }

    public async Task WriteBatchAsync(
        IReadOnlyList<RollingFileLogEntry> entries,
        CancellationToken cancellationToken)
    {
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureWriter(entry.Timestamp);
            await _writer!.WriteLineAsync(entry.Line.AsMemory(), cancellationToken).ConfigureAwait(false);
        }
    }

    public Task FlushAsync(CancellationToken cancellationToken)
        => _writer?.FlushAsync(cancellationToken) ?? Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (_writer == null)
        {
            return;
        }

        try
        {
            await _writer.FlushAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
        }

        await _writer.DisposeAsync().ConfigureAwait(false);
        _writer = null;
    }

    private void EnsureInitialized(DateTimeOffset now)
    {
        if (_initialized)
        {
            return;
        }

        Directory.CreateDirectory(_options.DirectoryPath);
        DeleteExpiredLogs(now);
        _initialized = true;
    }

    private void EnsureWriter(DateTimeOffset timestamp)
    {
        EnsureInitialized(timestamp);

        var entryDate = DateOnly.FromDateTime(timestamp.LocalDateTime);
        if (_writer != null && _writerDate == entryDate)
        {
            return;
        }

        _writer?.Dispose();
        _writer = null;

        if (_writerDate.HasValue)
        {
            DeleteExpiredLogs(timestamp);
        }

        var logPath = Path.Combine(_options.DirectoryPath, $"streamtunes-{timestamp:yyyy-MM-dd}.log");
        var stream = new FileStream(
            logPath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.ReadWrite,
            bufferSize: 16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        _writer = new StreamWriter(stream) { AutoFlush = false };
        _writerDate = entryDate;
    }

    private void DeleteExpiredLogs(DateTimeOffset now)
    {
        if (_options.RetentionDays <= 0 || !Directory.Exists(_options.DirectoryPath))
        {
            return;
        }

        var cutoff = now.Date.AddDays(-_options.RetentionDays);
        foreach (var filePath in Directory.EnumerateFiles(_options.DirectoryPath, "streamtunes-*.log"))
        {
            try
            {
                if (File.GetLastWriteTime(filePath).Date < cutoff)
                {
                    File.Delete(filePath);
                }
            }
            catch
            {
                // Retention is best effort and must not disable runtime diagnostics.
            }
        }
    }
}
