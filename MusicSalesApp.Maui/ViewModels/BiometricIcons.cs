using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.ViewModels;

/// <summary>
/// The glyph for a given biometric. Kept beside the view models rather than in the service, because
/// which file is on disk is a view concern and <see cref="BiometricMethod"/> is not.
/// </summary>
public static class BiometricIcons
{
    /// <summary>Also the fallback: it reads as "biometric" generally, which is what Android's prompt is.</summary>
    public const string Fingerprint = "fingerprint.png";

    public const string FaceId = "faceid.png";

    public static string For(BiometricMethod method) => method switch
    {
        // Touch ID is a fingerprint, so it shares the Android asset rather than needing its own.
        BiometricMethod.FaceId or BiometricMethod.OpticId => FaceId,
        _ => Fingerprint,
    };
}
