using Microsoft.Extensions.Logging;

namespace MusicSalesApp.Maui.Services;

public sealed class RollingFileLoggerOptions
{
    public required string DirectoryPath { get; init; }

    public int RetentionDays { get; init; } = 30;

    public LogLevel MinimumLevel { get; init; } = LogLevel.Information;

    public static RollingFileLoggerOptions CreateDefault()
    {
        var baseDirectory = FileSystem.AppDataDirectory;

#if ANDROID
        var externalFilesDirectory = global::Android.App.Application.Context.GetExternalFilesDir(null)?.AbsolutePath;
        if (!string.IsNullOrWhiteSpace(externalFilesDirectory))
        {
            baseDirectory = externalFilesDirectory;
        }
#endif

        return new RollingFileLoggerOptions
        {
            DirectoryPath = Path.Combine(baseDirectory, "logs"),
            RetentionDays = 30,
            MinimumLevel = LogLevel.Information
        };
    }
}