#if IOS
using Foundation;
using LocalAuthentication;
using Microsoft.Extensions.Logging;

namespace MusicSalesApp.Maui.Services;

/// <summary>
/// Face ID / Touch ID, through LocalAuthentication.
/// </summary>
/// <remarks>
/// <para>
/// The policy is <see cref="LAPolicy.DeviceOwnerAuthenticationWithBiometrics"/> - biometrics only,
/// no device-passcode fallback - because that is what Android does. Its prompt is built from
/// BIOMETRIC_STRONG | BIOMETRIC_WEAK with no DEVICE_CREDENTIAL, so a device with nothing enrolled
/// has no biometric sign-in rather than a passcode prompt. Diverging here would mean the same
/// feature guarded two different things on the two platforms.
/// </para>
/// <para>
/// This needs <c>NSFaceIDUsageDescription</c> in Info.plist. Without it the first evaluation on a
/// Face ID device terminates the app rather than failing.
/// </para>
/// </remarks>
public sealed class AppleBiometricAuthenticator : IBiometricAuthenticator
{
    private const LAPolicy Policy = LAPolicy.DeviceOwnerAuthenticationWithBiometrics;

    private readonly ILogger<AppleBiometricAuthenticator> _logger;

    public AppleBiometricAuthenticator(ILogger<AppleBiometricAuthenticator> logger) => _logger = logger;

    public Task<BiometricAvailability> GetAvailabilityAsync()
    {
        using var context = new LAContext();

        if (!context.CanEvaluatePolicy(Policy, out var error))
        {
            _logger.LogInformation(
                "Biometric sign-in is not offered on this device ({Error})", Describe(error));
            return Task.FromResult(BiometricAvailability.Unavailable);
        }

        // BiometryType is only populated once CanEvaluatePolicy has run on this same context, so it
        // is read here and never before the check.
        var (method, name) = context.BiometryType switch
        {
            LABiometryType.FaceId => (BiometricMethod.FaceId, "Face ID"),
            LABiometryType.TouchId => (BiometricMethod.TouchId, "Touch ID"),
            // No Optic ID case: LABiometryType.OpticId needs iOS 17 and this app supports 15.0, so
            // naming it here is a CA1416 warning. Nothing is lost - Optic ID is Vision Pro, and
            // UIDeviceFamily is iPhone and iPad only, so the value cannot arrive.
            _ => (BiometricMethod.None, "biometrics"),
        };

        // Logged on the way out too, not just on refusal: an absence of lines has to mean "never
        // asked", or the log cannot answer why the button is missing.
        _logger.LogInformation("Biometric sign-in is available ({Method})", method);

        // Apple's names are proper nouns, so the sentence form and the button form are the same word.
        return Task.FromResult(new BiometricAvailability(true, method, name, name));
    }

    public Task<(bool Success, string Error)> AuthenticateAsync()
    {
        // A fresh context per evaluation: LAContext caches its result, so a reused one can answer a
        // later prompt from an earlier success without asking the user anything.
        var context = new LAContext
        {
            // No fallback affordance - the policy above cannot honour it anyway - and a cancel
            // button worded like Android's.
            LocalizedFallbackTitle = string.Empty,
            LocalizedCancelTitle = "Cancel",
        };

        if (!context.CanEvaluatePolicy(Policy, out var canEvaluateError))
        {
            var reason = Describe(canEvaluateError);
            context.Dispose();
            return Task.FromResult((false, reason));
        }

        // RunContinuationsAsynchronously matters here: the reply arrives on LocalAuthentication's
        // own queue, and AuthService continues straight from this task into the login request. Left
        // synchronous, that HTTP call would run on Apple's callback queue.
        var completion = new TaskCompletionSource<(bool, string)>(TaskCreationOptions.RunContinuationsAsynchronously);

        context.EvaluatePolicy(Policy, "Sign in to StreamTunes", (succeeded, evaluationError) =>
        {
            try
            {
                completion.TrySetResult(succeeded
                    ? (true, string.Empty)
                    : (false, Describe(evaluationError)));
            }
            finally
            {
                context.Dispose();
            }
        });

        return completion.Task;
    }

    /// <summary>
    /// Turns an <see cref="LAStatus"/> into something worth showing someone. These reach the user
    /// verbatim - <c>LoginViewModel</c> puts the string straight on the screen - so they are
    /// sentences, and a cancellation reads as the ordinary event it is.
    /// </summary>
    private static string Describe(NSError? error) => (LAStatus)(long)(error?.Code ?? 0) switch
    {
        LAStatus.UserCancel or LAStatus.AppCancel => "Authentication cancelled.",
        LAStatus.SystemCancel => "Authentication was interrupted. Please try again.",
        LAStatus.AuthenticationFailed => "Could not recognise you. Please try again or use your password.",
        LAStatus.BiometryNotEnrolled => "No Face ID or Touch ID is set up on this device.",
        LAStatus.BiometryNotAvailable => "Face ID and Touch ID are unavailable for this app. You can turn them back on in Settings.",
        LAStatus.BiometryLockout => "Too many failed attempts. Unlock your device with its passcode, then try again.",
        LAStatus.PasscodeNotSet => "Set a device passcode to use biometric sign-in.",
        LAStatus.UserFallback => "Please sign in with your password.",
        _ => "Biometric authentication is not available on this device.",
    };
}
#endif
