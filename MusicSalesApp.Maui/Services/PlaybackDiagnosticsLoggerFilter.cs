using Microsoft.Extensions.Logging;

namespace MusicSalesApp.Maui.Services;

public static class PlaybackDiagnosticsLoggerFilter
{
    public const string PlaybackServiceCategoryPrefix = "MusicSalesApp.Maui.Services.PlaybackService";
    public const string QueuePreparationServiceCategoryPrefix = "MusicSalesApp.Maui.Services.QueuePreparationService";
    public const string AndroidMedia3CategoryPrefix = "MusicSalesApp.Maui.Platforms.Android.AndroidMedia3";
    public const string AndroidPlaybackSessionCategoryPrefix = "MusicSalesApp.Maui.Platforms.Android.PlaybackMediaSessionService";
    public const string AndroidAudioVisualizerCategoryPrefix = "MusicSalesApp.Maui.Platforms.Android.AudioVisualizerService";

    // Apple lock-screen artwork and transport controls. Both success paths are Information-only, so
    // without these prefixes a working load and a load that never ran look identical in the device
    // log. Three constants rather than one because the class names do not share a usable prefix -
    // "...Services.NowPlayingArtwork" does not match AppleNowPlayingArtworkLoader.
    public const string NowPlayingArtworkCategoryPrefix = "MusicSalesApp.Maui.Services.NowPlayingArtwork";
    public const string AppleNowPlayingArtworkCategoryPrefix = "MusicSalesApp.Maui.Services.AppleNowPlayingArtworkLoader";
    public const string AppleRemoteCommandCategoryPrefix = "MusicSalesApp.Maui.Services.AppleRemoteCommandBridge";

    // Entitlement categories. Everything interesting about billing and session restore is logged at
    // Information — "Connected to Google Play Billing", "Purchase acknowledged successfully", the
    // offer lookup, and the pending-restore retry. Without these prefixes the file log records only
    // the Warning-level failures, so a *successful* subscription leaves no trace at all and an
    // absence of billing lines cannot be read as an absence of billing activity. The volume is
    // negligible next to the playback diagnostics already being written.
    public const string GooglePlayBillingCategoryPrefix = "MusicSalesApp.Maui.Platforms.Android.GooglePlayBillingService";
    public const string AppStoreBillingCategoryPrefix = "MusicSalesApp.Maui.Platforms.iOS.AppStoreBillingService";
    public const string AuthServiceCategoryPrefix = "MusicSalesApp.Maui.Services.AuthService";

    // Why a biometric prompt was not offered. Both implementations report an unavailable device at
    // Information ("status 11" is NONE_ENROLLED, "status 12" is NO_HARDWARE), and that line is the
    // whole answer to "why is my fingerprint button missing?" - the availability check deliberately
    // fails open, so an unavailable device is the only case that hides the control. Without these
    // prefixes the log would show a device that was asked and a device that was never asked as the
    // same silence. Two constants because the platform classes share no usable prefix.
    public const string AndroidBiometricCategoryPrefix = "MusicSalesApp.Maui.Platforms.Android.AndroidBiometricAuthenticator";
    public const string AppleBiometricCategoryPrefix = "MusicSalesApp.Maui.Services.AppleBiometricAuthenticator";

    // Why Sign in with Apple did or did not complete. The whole path below Warning is Information -
    // whether this was a first authorization (the only time Apple sends an email at all) and whether
    // the user dismissed the sheet. Without this prefix a successful sign-in, a cancelled one, and a
    // sheet that never opened all look identical in the file log.
    public const string AppleSignInCategoryPrefix = "MusicSalesApp.Maui.Services.AppleSignInService";

    // Push registration. The whole success path is Information - which token was obtained, and that
    // the server accepted it - while only a REJECTED token logs at Warning. Without these prefixes a
    // device that registered cleanly and a device that never registered at all write exactly the
    // same nothing, and the FCM token cannot be read off the device to test a send with. One prefix
    // covers PushApiService and PushNotificationCoordinator, which share it; the two platform
    // registration services do not.
    public const string PushCategoryPrefix = "MusicSalesApp.Maui.Services.Push";
    public const string AndroidPushRegistrationCategoryPrefix = "MusicSalesApp.Maui.Platforms.Android.AndroidPushRegistrationService";
    public const string ApplePushRegistrationCategoryPrefix = "MusicSalesApp.Maui.Platforms.iOS.ApplePushRegistrationService";

    private static readonly string[] DiagnosticCategoryPrefixes =
    [
        PlaybackServiceCategoryPrefix,
        QueuePreparationServiceCategoryPrefix,
        AndroidMedia3CategoryPrefix,
        AndroidPlaybackSessionCategoryPrefix,
        AndroidAudioVisualizerCategoryPrefix,
        NowPlayingArtworkCategoryPrefix,
        AppleNowPlayingArtworkCategoryPrefix,
        AppleRemoteCommandCategoryPrefix,
        GooglePlayBillingCategoryPrefix,
        AppStoreBillingCategoryPrefix,
        AuthServiceCategoryPrefix,
        AndroidBiometricCategoryPrefix,
        AppleBiometricCategoryPrefix,
        AppleSignInCategoryPrefix,
        PushCategoryPrefix,
        AndroidPushRegistrationCategoryPrefix,
        ApplePushRegistrationCategoryPrefix
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
