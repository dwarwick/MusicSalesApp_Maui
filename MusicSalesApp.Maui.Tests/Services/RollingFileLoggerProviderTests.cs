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
    public void Log_WritesToRuntimeLogFile()
    {
        using var provider = new RollingFileLoggerProvider(new RollingFileLoggerOptions
        {
            DirectoryPath = _logDirectory,
            RetentionDays = 30,
            MinimumLevel = LogLevel.Information
        });
        var logger = provider.CreateLogger("TestCategory");

        logger.LogInformation("Playback diagnostic {Value}", 123);

        var logFile = Directory.GetFiles(_logDirectory, "streamtunes-*.log").Single();
        var contents = File.ReadAllText(logFile);
        Assert.That(contents, Does.Contain("Playback diagnostic 123"));
        Assert.That(contents, Does.Contain("TestCategory"));
    }

    [Test]
    public void Constructor_DeletesLogsOlderThanRetentionWindow()
    {
        Directory.CreateDirectory(_logDirectory);
        var oldLog = Path.Combine(_logDirectory, "streamtunes-old.log");
        var recentLog = Path.Combine(_logDirectory, "streamtunes-recent.log");
        File.WriteAllText(oldLog, "old");
        File.WriteAllText(recentLog, "recent");
        File.SetLastWriteTime(oldLog, DateTime.Now.AddDays(-31));
        File.SetLastWriteTime(recentLog, DateTime.Now.AddDays(-2));

        using var provider = new RollingFileLoggerProvider(new RollingFileLoggerOptions
        {
            DirectoryPath = _logDirectory,
            RetentionDays = 30,
            MinimumLevel = LogLevel.Information
        });

        Assert.That(File.Exists(oldLog), Is.False);
        Assert.That(File.Exists(recentLog), Is.True);
    }

    [Test]
    public void IsEnabled_RespectsMinimumLevel()
    {
        using var provider = new RollingFileLoggerProvider(new RollingFileLoggerOptions
        {
            DirectoryPath = _logDirectory,
            RetentionDays = 30,
            MinimumLevel = LogLevel.Warning
        });
        var logger = provider.CreateLogger("TestCategory");

        logger.LogInformation("ignored");
        logger.LogWarning("written");

        var logFile = Directory.GetFiles(_logDirectory, "streamtunes-*.log").Single();
        var contents = File.ReadAllText(logFile);
        Assert.That(contents, Does.Not.Contain("ignored"));
        Assert.That(contents, Does.Contain("written"));
    }

    [Test]
    public void Log_UsesCategoryFilterWhenProvided()
    {
        using var provider = new RollingFileLoggerProvider(new RollingFileLoggerOptions
        {
            DirectoryPath = _logDirectory,
            RetentionDays = 30,
            MinimumLevel = LogLevel.Information,
            CategoryFilter = (categoryName, _) => categoryName == "AllowedCategory"
        });
        var allowedLogger = provider.CreateLogger("AllowedCategory");
        var ignoredLogger = provider.CreateLogger("IgnoredCategory");

        allowedLogger.LogInformation("written");
        ignoredLogger.LogInformation("ignored");

        var logFile = Directory.GetFiles(_logDirectory, "streamtunes-*.log").Single();
        var contents = File.ReadAllText(logFile);
        Assert.That(contents, Does.Contain("written"));
        Assert.That(contents, Does.Not.Contain("ignored"));
    }
}
