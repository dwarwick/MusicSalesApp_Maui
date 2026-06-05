using Microsoft.Extensions.Logging;
using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.Platforms.Android;

public sealed class AndroidLogcatLoggerProvider(LogLevel minimumLevel = LogLevel.Information) : ILoggerProvider
{
    internal const string LogTag = "StreamTunes";
    private const int MaxLogcatMessageLength = 3500;

    public ILogger CreateLogger(string categoryName) => new AndroidLogcatLogger(categoryName, minimumLevel);

    public void Dispose()
    {
    }

    private sealed class AndroidLogcatLogger(string categoryName, LogLevel minimumLevel) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) =>
            PlaybackDiagnosticsLoggerFilter.ShouldLog(categoryName, logLevel, minimumLevel);

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = $"{categoryName}({eventId.Id}:{eventId.Name}) {formatter(state, exception)}";
            if (exception != null)
            {
                message += Environment.NewLine + exception;
            }

            WriteChunked(logLevel, message);
        }

        private static void WriteChunked(LogLevel logLevel, string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                Write(logLevel, string.Empty);
                return;
            }

            for (var offset = 0; offset < message.Length; offset += MaxLogcatMessageLength)
            {
                var length = Math.Min(MaxLogcatMessageLength, message.Length - offset);
                Write(logLevel, message.Substring(offset, length));
            }
        }

        private static void Write(LogLevel logLevel, string message)
        {
            switch (logLevel)
            {
                case LogLevel.Trace:
                case LogLevel.Debug:
                    global::Android.Util.Log.Debug(LogTag, message);
                    break;
                case LogLevel.Warning:
                    global::Android.Util.Log.Warn(LogTag, message);
                    break;
                case LogLevel.Error:
                case LogLevel.Critical:
                    global::Android.Util.Log.Error(LogTag, message);
                    break;
                default:
                    global::Android.Util.Log.Info(LogTag, message);
                    break;
            }
        }
    }
}
