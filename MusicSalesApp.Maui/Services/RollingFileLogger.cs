using Microsoft.Extensions.Logging;

namespace MusicSalesApp.Maui.Services;

internal sealed class RollingFileLogger : ILogger
{
    private readonly string _categoryName;
    private readonly RollingFileLoggerProvider _provider;

    public RollingFileLogger(string categoryName, RollingFileLoggerProvider provider)
    {
        _categoryName = categoryName;
        _provider = provider;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => _provider.IsEnabled(_categoryName, logLevel);

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

        _provider.Write(logLevel, _categoryName, eventId, formatter(state, exception), exception);
    }
}
