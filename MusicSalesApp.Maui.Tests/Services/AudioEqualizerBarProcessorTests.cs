using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.Tests.Services;

[TestFixture]
public class AudioEqualizerBarProcessorTests
{
    private const int SamplingRateMilliHertz = 44_100_000;
    private const int CaptureSize = 1024;

    [Test]
    public void Constructor_WithEmptyBandList_Throws()
    {
        Assert.That(() => new AudioEqualizerBarProcessor([]), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void ProcessFft_ReturnsDefaultBandCountAndClampedLevels()
    {
        var processor = new AudioEqualizerBarProcessor();

        var levels = processor.ProcessFft(CreateSingleFrequencyFft(700f), SamplingRateMilliHertz);

        Assert.That(levels, Has.Length.EqualTo(10));
        Assert.That(levels, Has.All.InRange(0f, 1f));
        Assert.That(levels.Any(level => level > 0f), Is.True);
    }

    [Test]
    public void ProcessFft_ZeroSamplingRate_FallsBackToDefaultRate()
    {
        var processor = new AudioEqualizerBarProcessor();
        var fft = CreateSingleFrequencyFft(700f);

        var defaultLevels = processor.ProcessFft(fft);

        processor.Reset();
        var zeroRateLevels = processor.ProcessFft(fft, 0);

        Assert.That(zeroRateLevels, Has.Length.EqualTo(defaultLevels.Length));
        for (var index = 0; index < defaultLevels.Length; index++)
        {
            Assert.That(zeroRateLevels[index], Is.EqualTo(defaultLevels[index]).Within(0.0001f));
        }
    }

    [Test]
    public void ProcessFft_RepeatedFrameRisesTowardTarget()
    {
        var processor = new AudioEqualizerBarProcessor();

        var firstFrame = processor.ProcessFft(CreateSingleFrequencyFft(700f), SamplingRateMilliHertz);
        var secondFrame = processor.ProcessFft(CreateSingleFrequencyFft(700f), SamplingRateMilliHertz);

        Assert.That(firstFrame.Max(), Is.LessThan(1f));
        Assert.That(secondFrame.Max(), Is.GreaterThan(firstFrame.Max()));
    }

    [Test]
    public void ProcessFft_LowFrequencyActivatesLeftMostBand()
    {
        var processor = new AudioEqualizerBarProcessor();

        var levels = processor.ProcessFft(CreateSingleFrequencyFft(45f), SamplingRateMilliHertz);

        Assert.That(Array.IndexOf(levels, levels.Max()), Is.EqualTo(0));
    }

    [Test]
    public void ProcessFft_MidFrequencyActivatesExpectedBand()
    {
        var processor = new AudioEqualizerBarProcessor();

        var levels = processor.ProcessFft(CreateSingleFrequencyFft(700f), SamplingRateMilliHertz);

        Assert.That(Array.IndexOf(levels, levels.Max()), Is.EqualTo(4));
    }

    [Test]
    public void ProcessFft_HighFrequencyActivatesRightMostBand()
    {
        var processor = new AudioEqualizerBarProcessor();

        var levels = processor.ProcessFft(CreateSingleFrequencyFft(14_000f), SamplingRateMilliHertz);

        Assert.That(Array.IndexOf(levels, levels.Max()), Is.EqualTo(9));
    }

    [Test]
    public void ProcessFft_NullFrameDecaysExistingLevels()
    {
        var processor = new AudioEqualizerBarProcessor();

        var activeFrame = processor.ProcessFft(CreateSingleFrequencyFft(700f), SamplingRateMilliHertz);
        var decayedFrame = processor.ProcessFft(null);

        Assert.That(decayedFrame, Has.Length.EqualTo(activeFrame.Length));
        for (var index = 0; index < activeFrame.Length; index++)
        {
            Assert.That(decayedFrame[index], Is.LessThanOrEqualTo(activeFrame[index]));
        }
    }

    [Test]
    public void ProcessFft_RepeatedSilenceEventuallyClearsLevels()
    {
        var processor = new AudioEqualizerBarProcessor();
        processor.ProcessFft(CreateSingleFrequencyFft(700f), SamplingRateMilliHertz);

        float[] levels = [];
        for (var iteration = 0; iteration < 24; iteration++)
        {
            levels = processor.ProcessFft(null);
        }

        Assert.That(levels, Has.All.EqualTo(0f));
    }

    [Test]
    public void Reset_ClearsCurrentLevels()
    {
        var processor = new AudioEqualizerBarProcessor();
        processor.ProcessFft(CreateSingleFrequencyFft(700f), SamplingRateMilliHertz);

        processor.Reset();
        var levels = processor.ProcessFft(null);

        Assert.That(levels, Has.All.EqualTo(0f));
    }

    private static byte[] CreateSingleFrequencyFft(float frequencyHz)
    {
        var fft = new byte[CaptureSize];
        var samplingRateHz = SamplingRateMilliHertz / 1000f;
        var binIndex = (int)Math.Round((frequencyHz * CaptureSize) / samplingRateHz);
        binIndex = Math.Clamp(binIndex, 1, (CaptureSize / 2) - 1);

        var offset = 2 + ((binIndex - 1) * 2);
        fft[offset] = unchecked((byte)120);
        fft[offset + 1] = 0;
        return fft;
    }
}