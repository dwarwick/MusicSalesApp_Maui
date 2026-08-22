using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Tests.ViewModels;

[TestFixture]
public class BiometricIconsTests
{
    [TestCase(BiometricMethod.FaceId, "faceid.png")]
    [TestCase(BiometricMethod.OpticId, "faceid.png")]
    [TestCase(BiometricMethod.TouchId, "fingerprint.png")]
    [TestCase(BiometricMethod.Fingerprint, "fingerprint.png")]
    [TestCase(BiometricMethod.None, "fingerprint.png")]
    public void ForPicksTheGlyphThatMatchesTheHardware(BiometricMethod method, string expected)
    {
        // Touch ID really is a fingerprint, so it shares Android's asset rather than needing its own.
        Assert.That(BiometricIcons.For(method), Is.EqualTo(expected));
    }
}
