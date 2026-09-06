using Microsoft.Extensions.Logging;
using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.Tests.Services;

[TestFixture]
public class PlaybackDiagnosticsLoggerFilterTests
{
    [TestCase(LogLevel.Warning)]
    [TestCase(LogLevel.Error)]
    [TestCase(LogLevel.Critical)]
    public void ShouldLog_AtWarningOrAbove_LogsRegardlessOfCategory(LogLevel logLevel)
    {
        var logged = PlaybackDiagnosticsLoggerFilter.ShouldLog("Some.Unrelated.Category", logLevel, LogLevel.Information);

        Assert.That(logged, Is.True);
    }

    [Test]
    public void ShouldLog_AtLogLevelNone_NeverLogs()
    {
        var logged = PlaybackDiagnosticsLoggerFilter.ShouldLog(
            PlaybackDiagnosticsLoggerFilter.PlaybackServiceCategoryPrefix, LogLevel.None, LogLevel.Trace);

        Assert.That(logged, Is.False);
    }

    [Test]
    public void ShouldLog_InformationFromAnUnrelatedCategory_DoesNotLog()
    {
        // This is the rule that makes an absence of log lines uninformative for any category not
        // listed below — worth pinning so it is a deliberate choice rather than a surprise.
        var logged = PlaybackDiagnosticsLoggerFilter.ShouldLog(
            "MusicSalesApp.Maui.Services.SomeOtherService", LogLevel.Information, LogLevel.Information);

        Assert.That(logged, Is.False);
    }

    [Test]
    public void ShouldLog_InformationBelowTheDiagnosticMinimum_DoesNotLog()
    {
        var logged = PlaybackDiagnosticsLoggerFilter.ShouldLog(
            PlaybackDiagnosticsLoggerFilter.PlaybackServiceCategoryPrefix, LogLevel.Debug, LogLevel.Information);

        Assert.That(logged, Is.False);
    }

    [TestCase(PlaybackDiagnosticsLoggerFilter.PlaybackServiceCategoryPrefix)]
    [TestCase(PlaybackDiagnosticsLoggerFilter.QueuePreparationServiceCategoryPrefix)]
    [TestCase(PlaybackDiagnosticsLoggerFilter.AndroidMedia3CategoryPrefix)]
    [TestCase(PlaybackDiagnosticsLoggerFilter.AndroidPlaybackSessionCategoryPrefix)]
    [TestCase(PlaybackDiagnosticsLoggerFilter.AndroidAudioVisualizerCategoryPrefix)]
    [TestCase(PlaybackDiagnosticsLoggerFilter.NowPlayingArtworkCategoryPrefix)]
    [TestCase("MusicSalesApp.Maui.Services.NowPlayingArtworkCoordinator")]
    [TestCase(PlaybackDiagnosticsLoggerFilter.AppleNowPlayingArtworkCategoryPrefix)]
    [TestCase(PlaybackDiagnosticsLoggerFilter.AppleRemoteCommandCategoryPrefix)]
    public void ShouldLog_InformationFromAPlaybackCategory_Logs(string categoryPrefix)
    {
        var logged = PlaybackDiagnosticsLoggerFilter.ShouldLog(categoryPrefix, LogLevel.Information, LogLevel.Information);

        Assert.That(logged, Is.True);
    }

    [TestCase(PlaybackDiagnosticsLoggerFilter.GooglePlayBillingCategoryPrefix)]
    [TestCase(PlaybackDiagnosticsLoggerFilter.AppStoreBillingCategoryPrefix)]
    [TestCase(PlaybackDiagnosticsLoggerFilter.AuthServiceCategoryPrefix)]
    public void ShouldLog_InformationFromAnEntitlementCategory_Logs(string categoryPrefix)
    {
        // A successful subscription logs entirely at Information ("Connected to Google Play
        // Billing", "Purchase acknowledged successfully"). Dropping those made a working purchase
        // flow indistinguishable from one that never ran.
        var logged = PlaybackDiagnosticsLoggerFilter.ShouldLog(categoryPrefix, LogLevel.Information, LogLevel.Information);

        Assert.That(logged, Is.True);
    }

    [TestCase(PlaybackDiagnosticsLoggerFilter.AndroidBiometricCategoryPrefix)]
    [TestCase(PlaybackDiagnosticsLoggerFilter.AppleBiometricCategoryPrefix)]
    public void ShouldLog_InformationFromABiometricCategory_Logs(string categoryPrefix)
    {
        // "Biometric sign-in is not offered on this device" is Information, and it is the entire
        // answer to why the button is missing. Dropped, a device that was asked and refused looks
        // exactly like one that was never asked.
        var logged = PlaybackDiagnosticsLoggerFilter.ShouldLog(categoryPrefix, LogLevel.Information, LogLevel.Information);

        Assert.That(logged, Is.True);
    }

    [Test]
    public void IsDiagnosticCategory_MatchesOnPrefix_NotExactName()
    {
        // Categories arrive with the concrete type name appended, so prefix matching is what makes
        // the entries above apply to real log calls.
        var logged = PlaybackDiagnosticsLoggerFilter.IsDiagnosticCategory(
            PlaybackDiagnosticsLoggerFilter.AndroidMedia3CategoryPrefix + "PlaybackRuntime");

        Assert.That(logged, Is.True);
    }

    [TestCase("")]
    [TestCase("   ")]
    public void IsDiagnosticCategory_WithoutACategoryName_IsNotDiagnostic(string categoryName)
    {
        Assert.That(PlaybackDiagnosticsLoggerFilter.IsDiagnosticCategory(categoryName), Is.False);
    }

    [TestCase("MusicSalesApp.Maui.Services.PushNotificationCoordinator")]
    [TestCase("MusicSalesApp.Maui.Services.PushApiService")]
    [TestCase("MusicSalesApp.Maui.Platforms.Android.AndroidPushRegistrationService")]
    [TestCase("MusicSalesApp.Maui.Platforms.iOS.ApplePushRegistrationService")]
    public void ShouldLog_InformationFromThePushPath_Logs(string category)
    {
        // The whole push success path is Information - only a rejected token is a Warning - so
        // without these a device that registered and a device that never tried write the same
        // nothing, and the FCM token cannot be read off the device to test a send with.
        var logged = PlaybackDiagnosticsLoggerFilter.ShouldLog(category, LogLevel.Information, LogLevel.Information);

        Assert.That(logged, Is.True);
    }
}
