using Microsoft.Extensions.Logging;

namespace MusicSalesApp.Maui.Services;

public static class PlaybackDiagnosticsLoggerFilter
{
    public const string PlaybackServiceCategoryPrefix = "MusicSalesApp.Maui.Services.PlaybackService";
    public const string QueuePreparationServiceCategoryPrefix = "MusicSalesApp.Maui.Services.QueuePreparationService";
    public const string AndroidMedia3CategoryPrefix = "MusicSalesApp.Maui.Platforms.Android.AndroidMedia3";
    public const string AndroidPlaybackSessionCategoryPrefix = "MusicSalesApp.Maui.Platforms.Android.PlaybackMediaSessionService";
    public const string AndroidAudioVisualizerCategoryPrefix = "MusicSalesApp.Maui.Platforms.Android.AudioVisualizerService";

    private static readonly string[] DiagnosticCategoryPrefixes =
    [
        PlaybackServiceCategoryPrefix,
        QueuePreparationServiceCategoryPrefix,
        AndroidMedia3CategoryPrefix,
        AndroidPlaybackSessionCategoryPrefix,
        AndroidAudioVisualizerCategoryPrefix
    ];

    public static bool ShouldLog(string categoryName, LogLevel logLevel, LogLevel diagnosticMinimumLevel)
    {
        if (logLevel == LogLevel.None)
        {
            return false;
        }

        if (logLevel >= LogLevel.Warning)
        {
            return true;
        }

        return logLevel >= diagnosticMinimumLevel && IsDiagnosticCategory(categoryName);
    }

    public static bool IsDiagnosticCategory(string categoryName)
    {
        if (string.IsNullOrWhiteSpace(categoryName))
        {
            return false;
        }

        return DiagnosticCategoryPrefixes.Any(prefix =>
            categoryName.StartsWith(prefix, StringComparison.Ordinal));
    }
}
