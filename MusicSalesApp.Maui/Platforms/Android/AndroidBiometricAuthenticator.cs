using AndroidX.Biometric;
using Microsoft.Extensions.Logging;
using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.Platforms.Android;

/// <summary>
/// Adapts the shipping <see cref="BiometricHelper"/> to <see cref="IBiometricAuthenticator"/>.
///
/// <para>
/// <see cref="AuthenticateAsync"/> is a straight delegation on purpose. The prompt's copy, its
/// threading, and its decision not to complete the task on a failed-but-retryable read are behaviour
/// people are already using, so this adds a seam above it rather than reimplementing it.
/// </para>
/// </summary>
public sealed class AndroidBiometricAuthenticator : IBiometricAuthenticator
{
    private readonly ILogger<AndroidBiometricAuthenticator> _logger;

    public AndroidBiometricAuthenticator(ILogger<AndroidBiometricAuthenticator> logger) => _logger = logger;

    public Task<(bool Success, string Error)> AuthenticateAsync() => BiometricHelper.AuthenticateAsync();

    /// <summary>
    /// Asks the platform whether a prompt would have anything to show, and <b>fails open</b>.
    /// </summary>
    /// <remarks>
    /// Before this existed the button appeared on every Android device that had credentials saved,
    /// and a device with nothing enrolled simply failed at the prompt. Narrowing that to the two
    /// definitive answers is the whole improvement; treating any other status as unavailable would
    /// be a regression, because it would hide the button on a device where the prompt still works.
    /// So only NO_HARDWARE and NONE_ENROLLED hide it. HW_UNAVAILABLE is transient, and
    /// SECURITY_UPDATE_REQUIRED / UNSUPPORTED / STATUS_UNKNOWN are all answers the prompt itself
    /// reports better than a hidden control can.
    /// </remarks>
    public Task<BiometricAvailability> GetAvailabilityAsync() => Task.Run(() =>
    {
        try
        {
            // The application context, not the activity: availability is a device fact and this is
            // called from page-load paths that can run before an activity is attached.
            var context = Microsoft.Maui.ApplicationModel.Platform.AppContext;
            var status = BiometricManager.From(context).CanAuthenticate(
                BiometricManager.Authenticators.BiometricStrong | BiometricManager.Authenticators.BiometricWeak);

            if (status is BiometricManager.BiometricErrorNoHardware or BiometricManager.BiometricErrorNoneEnrolled)
            {
                _logger.LogInformation("Biometric sign-in is not offered on this device (status {Status})", status);
                return BiometricAvailability.Unavailable;
            }

            return Available;
        }
        catch (Exception ex)
        {
            // Same reasoning as above: an unanswered question is not a "no". Reporting available
            // leaves the user exactly where they were before this check was added.
            _logger.LogWarning(ex, "Could not read biometric availability; offering it anyway");
            return Available;
        }
    });

    /// <summary>
    /// Android's prompt accepts fingerprint and face together and does not say which the device has,
    /// so the copy names both - and matches, word for word, what the account screen already shipped.
    /// </summary>
    private static BiometricAvailability Available =>
        new(true, BiometricMethod.Fingerprint, "your fingerprint or face", "Fingerprint");
}
