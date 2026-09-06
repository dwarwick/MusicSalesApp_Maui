using Microsoft.Extensions.Logging;
using Moq;
using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.Tests.Services;

/// <summary>
/// The coordinator decides <i>when</i> this device is registered, which is the half of push that
/// can be tested off-device. The native transports behind IPushRegistrationService live under
/// Platforms/ and are not compiled into this project at all.
/// </summary>
[TestFixture]
public class PushNotificationCoordinatorTests
{
    private Mock<IAuthService> _authService;
    private Mock<IPushRegistrationService> _registrationService;
    private Mock<IPushApiService> _pushApiService;
    private InMemoryPreferenceStore _preferences;
    private Mock<INotificationPreferenceApiService> _notificationPreferences;
    private PushNotificationCoordinator _coordinator;

    [SetUp]
    public void SetUp()
    {
        _authService = new Mock<IAuthService>();
        _registrationService = new Mock<IPushRegistrationService>();
        _pushApiService = new Mock<IPushApiService>();
        _preferences = new InMemoryPreferenceStore();

        _registrationService.Setup(x => x.IsSupported).Returns(true);
        _registrationService.Setup(x => x.GetPermissionStatusAsync())
            .ReturnsAsync(PushPermissionStatus.Granted);
        _registrationService.Setup(x => x.GetTokenAsync()).ReturnsAsync("token-abc");

        _pushApiService
            .Setup(x => x.RegisterDeviceAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(PushRegistrationOutcome.Registered);

        _authService.Setup(x => x.IsLoggedIn).Returns(true);

        _notificationPreferences = new Mock<INotificationPreferenceApiService>();
        _notificationPreferences
            .Setup(x => x.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NotificationPreferences());
        _notificationPreferences
            .Setup(x => x.SetAsync(It.IsAny<NotificationPreferences>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _coordinator = new PushNotificationCoordinator(
            _authService.Object,
            _registrationService.Object,
            _pushApiService.Object,
            _preferences,
            Mock.Of<ILogger<PushNotificationCoordinator>>(),
            _notificationPreferences.Object);
    }

    [TearDown]
    public void TearDown() => _coordinator.Dispose();

    [Test]
    public async Task Sync_RegistersASignedInDeviceThatHasPermission()
    {
        await _coordinator.SyncAsync();

        _pushApiService.Verify(
            x => x.RegisterDeviceAsync(It.IsAny<string>(), "token-abc", It.IsAny<string>()),
            Times.Once);
    }

    [Test]
    public async Task Sync_NeverShowsThePermissionPrompt()
    {
        // Sync runs on activation and on auth changes. A system prompt appearing at either moment
        // is unexplained, and a denial is close to permanent because neither platform shows the
        // prompt twice - so prompting is only ever done from RequestPermissionAndRegisterAsync.
        await _coordinator.SyncAsync();

        _registrationService.Verify(x => x.RequestPermissionAsync(), Times.Never);
    }

    [Test]
    public async Task Sync_DoesNothingWithoutPermission()
    {
        _registrationService.Setup(x => x.GetPermissionStatusAsync())
            .ReturnsAsync(PushPermissionStatus.Denied);

        await _coordinator.SyncAsync();

        _pushApiService.Verify(
            x => x.RegisterDeviceAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Test]
    public async Task Sync_DoesNothingOnAPlatformWithNoPushTransport()
    {
        _registrationService.Setup(x => x.IsSupported).Returns(false);

        await _coordinator.SyncAsync();

        _registrationService.Verify(x => x.GetPermissionStatusAsync(), Times.Never);
        _pushApiService.Verify(
            x => x.RegisterDeviceAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Test]
    public async Task Sync_RemembersTheTokenItRegisteredSoSignOutCanRetireIt()
    {
        // By sign-out the platform may hand back a different token, and unregistering the wrong one
        // leaves the old registration live - which is how a signed-out phone keeps receiving
        // someone else's notifications.
        await _coordinator.SyncAsync();

        Assert.That(
            _preferences.GetString(MobilePreferenceKeys.RegisteredPushToken),
            Is.EqualTo("token-abc"));
    }

    [Test]
    public async Task Sync_UnregistersTheStoredTokenOnSignOut()
    {
        await _coordinator.SyncAsync();
        _authService.Setup(x => x.IsLoggedIn).Returns(false);

        await _coordinator.SyncAsync();

        Assert.Multiple(() =>
        {
            _pushApiService.Verify(x => x.UnregisterDeviceAsync("token-abc"), Times.Once);
            Assert.That(_preferences.GetString(MobilePreferenceKeys.RegisteredPushToken), Is.Null.Or.Empty);
        });
    }

    [Test]
    public async Task Sync_SigningOutWithNothingRegisteredDoesNotCallTheServer()
    {
        _authService.Setup(x => x.IsLoggedIn).Returns(false);

        await _coordinator.SyncAsync();

        _pushApiService.Verify(x => x.UnregisterDeviceAsync(It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task Sync_ForgetsATokenTheServerRejects()
    {
        // Permanent. Storing it would mean retrying the same doomed registration on every
        // activation for the life of the install.
        _pushApiService
            .Setup(x => x.RegisterDeviceAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(PushRegistrationOutcome.Rejected);

        await _coordinator.SyncAsync();

        Assert.That(_preferences.GetString(MobilePreferenceKeys.RegisteredPushToken), Is.Null.Or.Empty);
    }

    [Test]
    public async Task Sync_RetriesAfterADeferredRegistration()
    {
        _pushApiService
            .Setup(x => x.RegisterDeviceAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(PushRegistrationOutcome.Deferred);

        await _coordinator.SyncAsync();

        _pushApiService
            .Setup(x => x.RegisterDeviceAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(PushRegistrationOutcome.Registered);

        await _coordinator.SyncAsync();

        Assert.That(
            _preferences.GetString(MobilePreferenceKeys.RegisteredPushToken),
            Is.EqualTo("token-abc"),
            "A deferred attempt must leave the next one free to succeed.");
    }

    [Test]
    public async Task Sync_DoesNotRegisterWhenThePlatformHasNoTokenYet()
    {
        // iOS in particular answers null until APNs delivers one to the AppDelegate; the refresh
        // event is what drives the retry.
        _registrationService.Setup(x => x.GetTokenAsync()).ReturnsAsync((string?)null);

        await _coordinator.SyncAsync();

        _pushApiService.Verify(
            x => x.RegisterDeviceAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Test]
    public async Task Sync_ReusesOneDeviceIdAcrossRegistrations()
    {
        // The server uses it to replace a rotated token in place rather than accumulating dead
        // rows, so a new id every launch would defeat the point.
        await _coordinator.SyncAsync();
        var first = _preferences.GetString(MobilePreferenceKeys.PushDeviceId);

        _registrationService.Setup(x => x.GetTokenAsync()).ReturnsAsync("token-two");
        await _coordinator.SyncAsync();

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.Not.Null.And.Not.Empty);
            Assert.That(_preferences.GetString(MobilePreferenceKeys.PushDeviceId), Is.EqualTo(first));
        });
    }

    [Test]
    public async Task RequestPermissionAndRegister_PromptsThenRegisters()
    {
        _registrationService.Setup(x => x.RequestPermissionAsync())
            .ReturnsAsync(PushPermissionStatus.Granted);

        var status = await _coordinator.RequestPermissionAndRegisterAsync();

        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(PushPermissionStatus.Granted));
            _pushApiService.Verify(
                x => x.RegisterDeviceAsync(It.IsAny<string>(), "token-abc", It.IsAny<string>()),
                Times.Once);
        });
    }

    [Test]
    public async Task RequestPermissionAndRegister_DoesNotRegisterAfterARefusal()
    {
        _registrationService.Setup(x => x.RequestPermissionAsync())
            .ReturnsAsync(PushPermissionStatus.Denied);

        var status = await _coordinator.RequestPermissionAndRegisterAsync();

        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(PushPermissionStatus.Denied));
            _pushApiService.Verify(
                x => x.RegisterDeviceAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
                Times.Never);
        });
    }

    [Test]
    public async Task Sync_SurvivesTheRegistrationServiceThrowing()
    {
        // This runs from app activation. An escaping exception there is a crash on resume.
        _registrationService.Setup(x => x.GetTokenAsync()).ThrowsAsync(new InvalidOperationException("boom"));

        Assert.DoesNotThrowAsync(async () => await _coordinator.SyncAsync());
        await Task.CompletedTask;
    }

    /// <summary>
    /// A real store rather than a mock: the coordinator round-trips values through it, and
    /// asserting on what it holds is clearer than verifying setter calls.
    /// </summary>
    private sealed class InMemoryPreferenceStore : IAppPreferenceStore
    {
        private readonly Dictionary<string, string> _values = [];

        public bool GetBool(string key, bool defaultValue = false) => defaultValue;

        public void SetBool(string key, bool value) { }

        public int GetInt(string key, int defaultValue = 0) => defaultValue;

        public void SetInt(string key, int value) { }

        public string? GetString(string key) => _values.GetValueOrDefault(key);

        public void SetString(string key, string value) => _values[key] = value;

        public void Remove(string key) => _values.Remove(key);
    }

    [Test]
    public async Task GetPermissionStatusAsync_ReportsWhatThePlatformSays_WithoutPrompting()
    {
        _registrationService.Setup(x => x.GetPermissionStatusAsync())
            .ReturnsAsync(PushPermissionStatus.Denied);

        var status = await _coordinator.GetPermissionStatusAsync();

        Assert.That(status, Is.EqualTo(PushPermissionStatus.Denied));

        // The UI asks this to decide which control to show. If it prompted, simply opening the
        // settings page would spend the one prompt the platform ever shows.
        _registrationService.Verify(x => x.RequestPermissionAsync(), Times.Never);
    }

    [Test]
    public async Task GetPermissionStatusAsync_WhenThePlatformHasNoTransport_ReportsUnsupported()
    {
        _registrationService.Setup(x => x.IsSupported).Returns(false);

        var status = await _coordinator.GetPermissionStatusAsync();

        Assert.That(status, Is.EqualTo(PushPermissionStatus.Unsupported));
    }

    // --- Opting in on the device is also opting in on the account ---

    [Test]
    public async Task RequestPermissionAndRegister_WhenGranted_SwitchesTheAccountPushPreferencesOn()
    {
        // Otherwise the phone registers cleanly and is then never sent anything, because the
        // account-level switches default off - which reads as push being broken.
        _registrationService.Setup(x => x.RequestPermissionAsync()).ReturnsAsync(PushPermissionStatus.Granted);

        await _coordinator.RequestPermissionAndRegisterAsync();

        _notificationPreferences.Verify(
            x => x.SetAsync(
                It.Is<NotificationPreferences>(p => p.ReceiveArtistReleasePush && p.ReceiveArtistMessagePush),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task RequestPermissionAndRegister_PreservesTheEmailPreferencesItDidNotAskAbout()
    {
        // The endpoint replaces the whole record, so writing only the push flags would silently
        // unsubscribe the listener from mail they had asked for.
        _registrationService.Setup(x => x.RequestPermissionAsync()).ReturnsAsync(PushPermissionStatus.Granted);
        _notificationPreferences
            .Setup(x => x.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NotificationPreferences { ReceiveArtistReleaseEmails = true });

        await _coordinator.RequestPermissionAndRegisterAsync();

        _notificationPreferences.Verify(
            x => x.SetAsync(
                It.Is<NotificationPreferences>(p => p.ReceiveArtistReleaseEmails),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task RequestPermissionAndRegister_WhenAlreadyOn_DoesNotWriteAtAll()
    {
        _registrationService.Setup(x => x.RequestPermissionAsync()).ReturnsAsync(PushPermissionStatus.Granted);
        _notificationPreferences
            .Setup(x => x.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NotificationPreferences
            {
                ReceiveArtistReleasePush = true,
                ReceiveArtistMessagePush = true,
            });

        await _coordinator.RequestPermissionAndRegisterAsync();

        _notificationPreferences.Verify(
            x => x.SetAsync(It.IsAny<NotificationPreferences>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task RequestPermissionAndRegister_WhenDenied_LeavesTheAccountAlone()
    {
        // This only ever turns preferences ON, and only when the user said yes.
        _registrationService.Setup(x => x.RequestPermissionAsync()).ReturnsAsync(PushPermissionStatus.Denied);

        await _coordinator.RequestPermissionAndRegisterAsync();

        _notificationPreferences.Verify(
            x => x.SetAsync(It.IsAny<NotificationPreferences>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task RequestPermissionAndRegister_WhenThePreferencesCannotBeSaved_StillRegisters()
    {
        // The device registration is the part that matters and has already happened; a failed
        // preference write must not be reported as a denied permission.
        _registrationService.Setup(x => x.RequestPermissionAsync()).ReturnsAsync(PushPermissionStatus.Granted);
        _notificationPreferences
            .Setup(x => x.SetAsync(It.IsAny<NotificationPreferences>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("offline"));

        var status = await _coordinator.RequestPermissionAndRegisterAsync();

        Assert.That(status, Is.EqualTo(PushPermissionStatus.Granted));
        _pushApiService.Verify(
            x => x.RegisterDeviceAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Once);
    }
}
