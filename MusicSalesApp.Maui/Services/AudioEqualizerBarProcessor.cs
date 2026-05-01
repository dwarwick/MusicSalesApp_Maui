namespace MusicSalesApp.Maui.Services;

public readonly record struct EqualizerFrequencyBand(string Name, float MinFrequencyHz, float MaxFrequencyHz)
{
    public bool Contains(float frequencyHz)
    {
        return frequencyHz >= MinFrequencyHz
            && (float.IsPositiveInfinity(MaxFrequencyHz) || frequencyHz < MaxFrequencyHz);
    }
}

/// <summary>
/// Converts raw FFT frames into smoothed 0..1 bar levels for a compact equalizer UI.
/// </summary>
public sealed class AudioEqualizerBarProcessor
{
    private const float MaxDecibels = 45.2f;
    private const int DefaultSamplingRateMilliHertz = 44_100_000;

    public static IReadOnlyList<EqualizerFrequencyBand> DefaultBands { get; } =
    [
        new("Sub-bass", 20f, 60f),
        new("Bass", 60f, 120f),
        new("Low mid", 120f, 250f),
        new("Mid", 250f, 500f),
        new("Upper mid", 500f, 1_000f),
        new("Presence", 1_000f, 2_000f),
        new("High mid", 2_000f, 4_000f),
        new("Brilliance", 4_000f, 8_000f),
        new("Treble", 8_000f, 12_000f),
        new("Air", 12_000f, float.PositiveInfinity)
    ];

    private readonly float[] _levels;
    private readonly float _attackFactor;
    private readonly float _decayFactor;
    private readonly float _noiseFloor;
    private readonly EqualizerFrequencyBand[] _bands;

    public AudioEqualizerBarProcessor(
        IReadOnlyList<EqualizerFrequencyBand>? bands = null,
        float attackFactor = 0.65f,
        float decayFactor = 0.84f,
        float noiseFloor = 0.03f)
    {
        if (bands is { Count: 0 })
            throw new ArgumentOutOfRangeException(nameof(bands));
        if (attackFactor <= 0f || attackFactor > 1f)
            throw new ArgumentOutOfRangeException(nameof(attackFactor));
        if (decayFactor <= 0f || decayFactor >= 1f)
            throw new ArgumentOutOfRangeException(nameof(decayFactor));
        if (noiseFloor < 0f || noiseFloor >= 1f)
            throw new ArgumentOutOfRangeException(nameof(noiseFloor));

        _bands = bands?.ToArray() ?? [.. DefaultBands];
        _levels = new float[_bands.Length];
        _attackFactor = attackFactor;
        _decayFactor = decayFactor;
        _noiseFloor = noiseFloor;
    }

    public int BarCount => _levels.Length;

    public IReadOnlyList<EqualizerFrequencyBand> Bands => _bands;

    public float[] ProcessFft(byte[]? fft)
    {
        return ProcessFft(fft, DefaultSamplingRateMilliHertz);
    }

    public float[] ProcessFft(byte[]? fft, int samplingRate)
    {
        if (fft == null || fft.Length < 4)
        {
            ApplyDecay();
            return CloneLevels();
        }

        var targets = CalculateTargetLevels(fft, NormalizeSamplingRateHz(samplingRate));
        for (var barIndex = 0; barIndex < _levels.Length; barIndex++)
        {
            var target = targets[barIndex];
            _levels[barIndex] = target > _levels[barIndex]
                ? _levels[barIndex] + ((target - _levels[barIndex]) * _attackFactor)
                : MathF.Max(target, _levels[barIndex] * _decayFactor);

            if (_levels[barIndex] < _noiseFloor)
            {
                _levels[barIndex] = 0f;
            }
        }

        return CloneLevels();
    }

    public void Reset() => Array.Clear(_levels);

    private float[] CalculateTargetLevels(byte[] fft, float samplingRateHz)
    {
        var magnitudes = ExtractMagnitudes(fft);
        if (magnitudes.Length == 0)
        {
            return new float[_levels.Length];
        }

        var bars = new float[_levels.Length];
        for (var barIndex = 0; barIndex < bars.Length; barIndex++)
        {
            var peakMagnitude = 0f;
            var matchedBand = false;
            for (var binIndex = 0; binIndex < magnitudes.Length; binIndex++)
            {
                var frequencyHz = GetBinFrequencyHz(binIndex, fft.Length, samplingRateHz);
                if (!_bands[barIndex].Contains(frequencyHz))
                {
                    continue;
                }

                matchedBand = true;
                peakMagnitude = MathF.Max(peakMagnitude, magnitudes[binIndex]);
            }

            bars[barIndex] = matchedBand ? NormalizeMagnitude(peakMagnitude) : 0f;
        }

        return bars;
    }

    private static float[] ExtractMagnitudes(byte[] fft)
    {
        var pairCount = Math.Max(0, (fft.Length - 2) / 2);
        var magnitudes = new float[pairCount];

        for (var pairIndex = 0; pairIndex < pairCount; pairIndex++)
        {
            var offset = 2 + (pairIndex * 2);
            var real = unchecked((sbyte)fft[offset]);
            var imaginary = unchecked((sbyte)fft[offset + 1]);
            magnitudes[pairIndex] = MathF.Sqrt((real * real) + (imaginary * imaginary));
        }

        return magnitudes;
    }

    private static float NormalizeSamplingRateHz(int samplingRate)
    {
        if (samplingRate <= 0)
        {
            return DefaultSamplingRateMilliHertz / 1000f;
        }

        return samplingRate > 1_000_000
            ? samplingRate / 1000f
            : samplingRate;
    }

    private static float GetBinFrequencyHz(int binIndex, int captureSize, float samplingRateHz)
    {
        if (captureSize <= 0)
        {
            return 0f;
        }

        return ((binIndex + 1) * samplingRateHz) / captureSize;
    }

    private static float NormalizeMagnitude(float magnitude)
    {
        if (magnitude <= 0f)
        {
            return 0f;
        }

        var normalized = (20f * MathF.Log10(magnitude + 1f)) / MaxDecibels;
        return Math.Clamp(normalized, 0f, 1f);
    }

    private void ApplyDecay()
    {
        for (var barIndex = 0; barIndex < _levels.Length; barIndex++)
        {
            _levels[barIndex] *= _decayFactor;
            if (_levels[barIndex] < _noiseFloor)
            {
                _levels[barIndex] = 0f;
            }
        }
    }

    private float[] CloneLevels()
    {
        var clone = new float[_levels.Length];
        Array.Copy(_levels, clone, clone.Length);
        return clone;
    }
}