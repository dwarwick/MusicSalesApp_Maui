namespace MusicSalesApp.Maui.Services;

internal enum PlaybackIndicatorVisualState
{
    PlayIcon,
    Equalizer
}

internal static class PlaybackIndicatorStateResolver
{
    private const float MinFallbackLevel = 0.18f;
    private const float MaxFallbackLevel = 0.76f;

    internal static PlaybackIndicatorVisualState Resolve(bool isCurrentSongPlaying)
    {
        return isCurrentSongPlaying
            ? PlaybackIndicatorVisualState.Equalizer
            : PlaybackIndicatorVisualState.PlayIcon;
    }

    internal static bool HasLiveLevels(IReadOnlyList<float>? levels)
    {
        return levels is { Count: > 0 };
    }

    internal static IReadOnlyList<float> ResolveLevels(IReadOnlyList<float>? levels, double animationPhase)
    {
        if (levels is { Count: > 0 } liveLevels)
        {
            return liveLevels;
        }

        return CreateAnimatedFallbackLevels(animationPhase);
    }

    private static float[] CreateAnimatedFallbackLevels(double animationPhase)
    {
        var barCount = AudioEqualizerBarProcessor.DefaultBands.Count;
        var resolvedLevels = new float[barCount];

        for (var barIndex = 0; barIndex < barCount; barIndex++)
        {
            var leadingWave = (Math.Sin(animationPhase + (barIndex * 0.62d)) + 1d) / 2d;
            var trailingWave = (Math.Sin((animationPhase * 1.73d) - (barIndex * 0.41d)) + 1d) / 2d;
            var combinedWave = (leadingWave * 0.65d) + (trailingWave * 0.35d);
            resolvedLevels[barIndex] = (float)(MinFallbackLevel + ((MaxFallbackLevel - MinFallbackLevel) * combinedWave));
        }

        return resolvedLevels;
    }
}