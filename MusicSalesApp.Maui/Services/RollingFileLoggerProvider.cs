using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace MusicSalesApp.Maui.Services;

public sealed class RollingFileLoggerProvider : ILoggerProvider
{
    private readonly RollingFileLoggerOptions _options;
    private readonly ConcurrentDictionary<string, RollingFileLogger> _loggers = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _writeLock = new();

    public RollingFileLoggerProvider(RollingFileLoggerOptions options)
    {
        _options = options;
        Directory.CreateDirectory(_options.DirectoryPath);
        DeleteExpiredLogs(DateTimeOffset.Now);
    }

    public ILogger CreateLogger(string categoryName)
    {
        return _loggers.GetOrAdd(categoryName, category => new RollingFileLogger(category, this));
    }

    public void Dispose()
    {
        _loggers.Clear();
    }

    internal bool IsEnabled(LogLevel logLevel) => logLevel >= _options.MinimumLevel && logLevel != LogLevel.None;

    internal void Write(LogLevel logLevel, string categoryName, EventId eventId, string message, Exception? exception)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var now = DateTimeOffset.Now;
        var line = FormatLogLine(now, logLevel, categoryName, eventId, message, exception);
        var logPath = Path.Combine(_options.DirectoryPath, $"streamtunes-{now:yyyy-MM-dd}.log");

        lock (_writeLock)
        {
            Directory.CreateDirectory(_options.DirectoryPath);
            DeleteExpiredLogs(now);
            File.AppendAllText(logPath, line + Environment.NewLine);
        }
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
                var lastWriteDate = File.GetLastWriteTime(filePath).Date;
                if (lastWriteDate < cutoff)
                {
                    File.Delete(filePath);
                }
            }
            catch
            {
            }
        }
    }

    private static string FormatLogLine(DateTimeOffset timestamp, LogLevel logLevel, string categoryName, EventId eventId, string message, Exception? exception)
    {
        var line = $"{timestamp:O} [{logLevel}] {categoryName}({eventId.Id}:{eventId.Name}) {message}";
        return exception == null
            ? line
            : line + Environment.NewLine + exception;
    }
}