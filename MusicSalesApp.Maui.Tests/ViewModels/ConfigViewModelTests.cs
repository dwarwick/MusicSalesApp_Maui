using Moq;
using MusicSalesApp.Maui.Services;
using MusicSalesApp.Common.Helpers;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Tests.ViewModels;

[TestFixture]
public class ConfigViewModelTests
{
    private Mock<IOfflineCacheSettingsService> _settings = null!;
    private Mock<IAudioCacheService> _audioCacheService = null!;
    private ConfigViewModel _viewModel = null!;

    [SetUp]
    public void SetUp()
    {
        _settings = new Mock<IOfflineCacheSettingsService>();
        _settings.SetupGet(s => s.MinimumCacheLimitMb).Returns(100);
        _settings.SetupGet(s => s.MaximumCacheLimitMb).Returns(5120);
        _settings.SetupGet(s => s.DefaultCacheLimitMb).Returns(1024);
        _settings.SetupGet(s => s.DeviceFreeSpaceReserveMb).Returns(1024);
        _settings.Setup(s => s.NormalizeCacheLimitMb(It.IsAny<int>()))
            .Returns((int limitMb) => Math.Clamp(limitMb, 100, 5120));
        _settings.Setup(s => s.GetOfflineCacheLimitMb()).Returns(1024);

        _audioCacheService = new Mock<IAudioCacheService>();
        _audioCacheService.Setup(s => s.GetCacheUsageBytesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0L);

        _viewModel = new ConfigViewModel(_settings.Object, _audioCacheService.Object);
    }

    [Test]
    public void Constructor_LoadsConfiguredLimitAndReserveDisplay()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.OfflineCacheLimitMb, Is.EqualTo(1024));
            Assert.That(_viewModel.OfflineCacheLimitDisplay, Is.EqualTo("1 GB"));
            Assert.That(_viewModel.DeviceFreeSpaceReserveDisplay, Is.EqualTo("1 GB"));
        });
    }

    [Test]
    public void OfflineCacheLimitMb_WhenChanged_PersistsNormalizedValue()
    {
        _viewModel.OfflineCacheLimitMb = 2048;

        Assert.That(_viewModel.OfflineCacheLimitDisplay, Is.EqualTo("2 GB"));
        _settings.Verify(s => s.SetOfflineCacheLimitMb(2048), Times.Once);
    }

    [Test]
    public void ResetOfflineCacheLimitCommand_RestoresDefaultLimit()
    {
        _viewModel.OfflineCacheLimitMb = 2048;

        _viewModel.ResetOfflineCacheLimitCommand.Execute(null);

        Assert.That(_viewModel.OfflineCacheLimitMb, Is.EqualTo(1024));
        _settings.Verify(s => s.SetOfflineCacheLimitMb(1024), Times.AtLeastOnce);
    }

    [Test]
    public async Task RefreshCacheUsageAsync_FormatsBytesFromAudioCacheService()
    {
        _audioCacheService.Setup(s => s.GetCacheUsageBytesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(6L * 1024 * 1024);

        await _viewModel.RefreshCacheUsageAsync();

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.IsCacheUsageLoading, Is.False);
            Assert.That(_viewModel.CacheUsageDisplay, Is.EqualTo("6 MB"));
        });
    }

    [Test]
    public async Task RefreshCacheUsageAsync_WhenAudioCacheServiceThrows_ClearsLoadingFlagAndPropagates()
    {
        _audioCacheService.Setup(s => s.GetCacheUsageBytesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        Assert.That(
            async () => await _viewModel.RefreshCacheUsageAsync(),
            Throws.InstanceOf<InvalidOperationException>());
        Assert.That(_viewModel.IsCacheUsageLoading, Is.False);
    }

    // --- Notifications: one place for the OS permission and the account preferences ---

    private (ConfigViewModel ViewModel, Mock<INotificationPreferenceApiService> Api, Mock<IPushNotificationCoordinator> Push)
        CreateWithNotifications(NotificationPreferences? stored, PushPermissionStatus permission)
    {
        var api = new Mock<INotificationPreferenceApiService>();
        api.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(stored);
        api.Setup(x => x.SetAsync(It.IsAny<NotificationPreferences>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var push = new Mock<IPushNotificationCoordinator>();
        push.Setup(x => x.GetPermissionStatusAsync()).ReturnsAsync(permission);
        push.Setup(x => x.RequestPermissionAndRegisterAsync()).ReturnsAsync(PushPermissionStatus.Granted);

        var viewModel = new ConfigViewModel(
            _settings.Object, _audioCacheService.Object, api.Object, push.Object);

        return (viewModel, api, push);
    }

    private static NotificationPreferences Stored(
        bool release = true,
        bool message = true,
        ArtistPushFrequency frequency = ArtistPushFrequency.Instant) => new()
    {
        ReceiveArtistReleasePush = release,
        ReceiveArtistMessagePush = message,
        ArtistPushFrequency = frequency,
    };

    [Test]
    public async Task Load_ShowsWhatTheServerAndTheDeviceBothSay()
    {
        var (viewModel, _, _) = CreateWithNotifications(
            Stored(frequency: ArtistPushFrequency.Daily), PushPermissionStatus.Granted);

        await viewModel.LoadNotificationPreferencesAsync();

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.IsNotificationSectionAvailable, Is.True);
            Assert.That(viewModel.AllowPushNotifications, Is.True);
            Assert.That(viewModel.ReceiveReleasePush, Is.True);
            Assert.That(viewModel.ReceiveMessagePush, Is.True);
            Assert.That(viewModel.NotificationFrequency, Is.EqualTo(ArtistPushFrequency.Daily));
        });
    }

    [Test]
    public async Task Load_WithPermissionButNothingWanted_ShowsTheMasterOff()
    {
        // The master means "a notification could actually arrive", so it must not claim to be on
        // while every category is off.
        var (viewModel, _, _) = CreateWithNotifications(
            Stored(release: false, message: false), PushPermissionStatus.Granted);

        await viewModel.LoadNotificationPreferencesAsync();

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.AllowPushNotifications, Is.False);
            Assert.That(viewModel.CanEditNotificationCategories, Is.False, "categories mean nothing while the master is off");
        });
    }

    [Test]
    public async Task Load_WhenTheDeviceRefusedThePermission_ShowsTheMasterOffAndSaysWhy()
    {
        // The account may well still want both kinds; the phone is what is refusing.
        var (viewModel, _, _) = CreateWithNotifications(Stored(), PushPermissionStatus.Denied);

        await viewModel.LoadNotificationPreferencesAsync();

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.IsNotificationSectionAvailable, Is.True, "hiding it reads as the feature missing");
            Assert.That(viewModel.AllowPushNotifications, Is.False);
            Assert.That(viewModel.IsPushBlockedBySystem, Is.True);
            Assert.That(viewModel.CanEditNotifications, Is.False);
            Assert.That(viewModel.NotificationBlockedMessage, Does.Contain("device settings"));
        });
    }

    [Test]
    public async Task Load_DoesNotEchoAnythingBackToTheServer()
    {
        var (viewModel, api, _) = CreateWithNotifications(Stored(), PushPermissionStatus.Granted);

        await viewModel.LoadNotificationPreferencesAsync();

        api.Verify(x => x.SetAsync(It.IsAny<NotificationPreferences>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestCase(PushPermissionStatus.Unsupported)]
    public async Task Load_OnAPlatformWithNoTransport_HidesTheSection(PushPermissionStatus permission)
    {
        var (viewModel, _, _) = CreateWithNotifications(Stored(), permission);

        await viewModel.LoadNotificationPreferencesAsync();

        Assert.That(viewModel.IsNotificationSectionAvailable, Is.False);
    }

    [Test]
    public async Task Load_WhenTheServerCannotBeReached_HidesTheSection()
    {
        var (viewModel, _, _) = CreateWithNotifications(stored: null, PushPermissionStatus.Granted);

        await viewModel.LoadNotificationPreferencesAsync();

        Assert.That(viewModel.IsNotificationSectionAvailable, Is.False);
    }

    [Test]
    public async Task TurningTheMasterOn_AsksTheOsAndSwitchesTheCategoriesOn()
    {
        var (viewModel, api, push) = CreateWithNotifications(
            Stored(release: false, message: false), PushPermissionStatus.NotDetermined);

        await viewModel.LoadNotificationPreferencesAsync();

        api.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Stored());
        viewModel.AllowPushNotifications = true;
        await Task.Delay(50);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.ReceiveReleasePush, Is.True);
            Assert.That(viewModel.ReceiveMessagePush, Is.True);
            Assert.That(viewModel.AllowPushNotifications, Is.True);
        });

        // Through the coordinator, because "allow" is both halves: the OS prompt and the account.
        push.Verify(x => x.RequestPermissionAndRegisterAsync(), Times.Once);
    }

    [Test]
    public async Task TurningTheMasterOn_WhenTheUserRefuses_GoesBackOffAndExplains()
    {
        var (viewModel, _, push) = CreateWithNotifications(
            Stored(release: false, message: false), PushPermissionStatus.NotDetermined);
        push.Setup(x => x.RequestPermissionAndRegisterAsync()).ReturnsAsync(PushPermissionStatus.Denied);

        await viewModel.LoadNotificationPreferencesAsync();
        viewModel.AllowPushNotifications = true;
        await Task.Delay(50);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.AllowPushNotifications, Is.False, "a switch that stays on while nothing arrives is a lie");
            Assert.That(viewModel.IsPushBlockedBySystem, Is.True);
            Assert.That(viewModel.NotificationStatus, Does.Contain("device settings"));
        });
    }

    [Test]
    public async Task TurningTheMasterOff_SwitchesBothCategoriesOffOnTheServer()
    {
        // Off cannot revoke the OS permission, so it means "send me nothing" - which is the two
        // category switches. The device stays registered, so turning it back on needs no prompt.
        var (viewModel, api, push) = CreateWithNotifications(Stored(), PushPermissionStatus.Granted);
        await viewModel.LoadNotificationPreferencesAsync();

        api.Setup(x => x.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Stored(release: false, message: false));

        viewModel.AllowPushNotifications = false;
        await Task.Delay(50);

        api.Verify(
            x => x.SetAsync(
                It.Is<NotificationPreferences>(p => !p.ReceiveArtistReleasePush && !p.ReceiveArtistMessagePush),
                It.IsAny<CancellationToken>()),
            Times.Once);
        push.Verify(x => x.RequestPermissionAndRegisterAsync(), Times.Never);
    }

    [Test]
    public async Task TurningTheMasterOn_WritesOnce_NotOncePerCategory()
    {
        // The master sets both categories; without suppression each setter would fire its own
        // save, giving three round trips for one tap in a racing order.
        var (viewModel, api, _) = CreateWithNotifications(
            Stored(release: false, message: false), PushPermissionStatus.NotDetermined);

        await viewModel.LoadNotificationPreferencesAsync();
        api.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Stored());

        viewModel.AllowPushNotifications = true;
        await Task.Delay(50);

        api.Verify(
            x => x.SetAsync(It.IsAny<NotificationPreferences>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "the coordinator writes them; this must not write again on top");
    }

    [Test]
    public async Task TurningOffOneCategory_KeepsTheEmailPreferencesItDidNotAskAbout()
    {
        var stored = Stored();
        stored.ReceiveArtistReleaseEmails = true;

        var (viewModel, api, _) = CreateWithNotifications(stored, PushPermissionStatus.Granted);
        await viewModel.LoadNotificationPreferencesAsync();

        viewModel.ReceiveMessagePush = false;
        await Task.Delay(50);

        api.Verify(
            x => x.SetAsync(
                It.Is<NotificationPreferences>(p => p.ReceiveArtistReleaseEmails && !p.ReceiveArtistMessagePush),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ChangingTheFrequency_OnAServerThatIgnoresIt_SaysSoAndShowsTheRealValue()
    {
        // Exactly what an app newer than its server does: the unknown property is dropped, the PUT
        // answers OK, and the choice is gone by the next page open. Saying "Saved" there is a lie.
        var (viewModel, api, _) = CreateWithNotifications(Stored(), PushPermissionStatus.Granted);

        // A fresh record per read, so the view model's own mutation cannot be mistaken for the
        // server having stored it - which is precisely what an older server does.
        api.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => Stored());

        await viewModel.LoadNotificationPreferencesAsync();

        viewModel.NotificationFrequency = ArtistPushFrequency.Daily;
        await Task.Delay(50);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.NotificationStatus, Does.Contain("does not support"));
            Assert.That(viewModel.NotificationFrequency, Is.EqualTo(ArtistPushFrequency.Instant));
        });
    }

    [Test]
    public async Task ChangingTheFrequency_WhenItSticks_SaysSaved()
    {
        var stored = Stored();
        var (viewModel, api, _) = CreateWithNotifications(stored, PushPermissionStatus.Granted);
        api.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => stored);

        await viewModel.LoadNotificationPreferencesAsync();

        viewModel.NotificationFrequency = ArtistPushFrequency.Daily;
        await Task.Delay(50);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.NotificationStatus, Is.EqualTo("Saved."));
            Assert.That(viewModel.NotificationFrequency, Is.EqualTo(ArtistPushFrequency.Daily));
        });
    }

    [Test]
    public async Task WithNoPushServices_TheSectionIsSimplyAbsent()
    {
        // Both dependencies are optional and trailing, so every pre-existing construction passes
        // null - that must stay harmless.
        await _viewModel.LoadNotificationPreferencesAsync();

        Assert.That(_viewModel.IsNotificationSectionAvailable, Is.False);
    }
}
