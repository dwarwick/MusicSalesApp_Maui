namespace MusicSalesApp.Maui.Services;

/// <summary>
/// Which biometric the device actually offers, for the one decision the names cannot drive: the
/// icon. Android reports <see cref="Fingerprint"/> for a prompt that may accept a face as well,
/// because its API does not say which is enrolled.
/// </summary>
public enum BiometricMethod
{
    None,
    Fingerprint,
    FaceId,
    TouchId,
    OpticId,
}

/// <summary>
/// What this device can offer, and what to call it on screen.
///
/// <para>
/// The name is carried rather than derived at the call site because only the platform knows it:
/// Android's prompt covers fingerprint and face together, while Apple's is specifically Face ID or
/// Touch ID and the user expects to see that exact name. Copy that says "fingerprint" on a Face ID
/// phone reads as a different feature that is not working.
/// </para>
/// </summary>
/// <param name="Method">Which biometric this is, for choosing an icon.</param>
/// <param name="IsAvailable">
/// Whether a prompt would have something to show. False only for the definitive answers - no
/// hardware, or nothing enrolled. Anything ambiguous reports true and lets the prompt itself
/// produce the error, which is what shipped on Android before this check existed.
/// </param>
/// <param name="DisplayName">Reads inside a sentence: "sign in with <c>your fingerprint or face</c>".</param>
/// <param name="ShortName">Titles a control: "Turn Off <c>Fingerprint</c> Sign-In".</param>
public readonly record struct BiometricAvailability(
    bool IsAvailable,
    BiometricMethod Method,
    string DisplayName,
    string ShortName)
{
    /// <summary>No biometric sign-in here. The names still read correctly in copy that is built before the check.</summary>
    public static BiometricAvailability Unavailable => new(false, BiometricMethod.None, "biometrics", "Biometric");
}

/// <summary>
/// The device's biometric prompt, behind an interface so the platform-neutral half of the feature -
/// which is all of <see cref="AuthService"/> except the prompt itself - can be tested.
///
/// <para>
/// Before this existed the prompt was a <c>private static</c> method inside <c>AuthService</c>
/// guarded by <c>#if ANDROID</c>, so on the test project's platform-less build it compiled down to a
/// hard-coded failure. That left <c>BiometricLoginAsync</c> with no test coverage at all: there was
/// no seam to stand a double in.
/// </para>
/// </summary>
public interface IBiometricAuthenticator
{
    /// <summary>
    /// Whether to offer biometric sign-in on this device at all. Called on the login screen's
    /// appearance and on the account screen's load, so implementations keep it cheap and off the
    /// main thread.
    /// </summary>
    Task<BiometricAvailability> GetAvailabilityAsync();

    /// <summary>
    /// Show the prompt and wait for the user. <c>Error</c> is surfaced to the user verbatim, so it
    /// is a sentence, not a code - including for a cancellation, which is a normal outcome here
    /// rather than an exception.
    /// </summary>
    Task<(bool Success, string Error)> AuthenticateAsync();
}

/// <summary>
/// Windows, Mac Catalyst, and the test project's platform-less build. Preserves the exact answer
/// <c>AuthService.PromptBiometricAsync</c>'s <c>#else</c> branch used to give.
/// </summary>
public sealed class UnsupportedBiometricAuthenticator : IBiometricAuthenticator
{
    public Task<BiometricAvailability> GetAvailabilityAsync()
        => Task.FromResult(BiometricAvailability.Unavailable);

    public Task<(bool Success, string Error)> AuthenticateAsync()
        => Task.FromResult((false, "Biometric authentication is not supported on this platform."));
}
