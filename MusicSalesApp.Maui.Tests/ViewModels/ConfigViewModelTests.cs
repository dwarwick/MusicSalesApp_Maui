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

    // --- Notification frequency: an account setting the server enforces, not a local one ---

    private (ConfigViewModel ViewModel, Mock<INotificationPreferenceApiService> Api) CreateWithPreferences(
        NotificationPreferences? stored)
    {
        var api = new Mock<INotificationPreferenceApiService>();
        api.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(stored);
        api.Setup(x => x.SetAsync(It.IsAny<NotificationPreferences>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        return (new ConfigViewModel(_settings.Object, _audioCacheService.Object, api.Object), api);
    }

    [Test]
    public async Task LoadNotificationPreferences_ShowsWhatTheServerHasStored()
    {
        var (viewModel, _) = CreateWithPreferences(new NotificationPreferences
        {
            ArtistPushFrequency = ArtistPushFrequency.Daily,
        });

        await viewModel.LoadNotificationPreferencesAsync();

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.IsNotificationFrequencyAvailable, Is.True);
            Assert.That(viewModel.NotificationFrequency, Is.EqualTo(ArtistPushFrequency.Daily));
            Assert.That(viewModel.NotificationFrequencyDescription, Does.Contain("one notification a day"));
        });
    }

    [Test]
    public async Task LoadNotificationPreferences_DoesNotEchoTheValueStraightBack()
    {
        // Reading the page must not be a write. Setting the backing field through the property
        // would post the freshly-loaded value to the server on every appearance.
        var (viewModel, api) = CreateWithPreferences(new NotificationPreferences
        {
            ArtistPushFrequency = ArtistPushFrequency.TwelveHours,
        });

        await viewModel.LoadNotificationPreferencesAsync();

        api.Verify(
            x => x.SetAsync(It.IsAny<NotificationPreferences>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task LoadNotificationPreferences_WhenTheServerCannotBeReached_HidesTheSection()
    {
        // A picker that cannot save is worse than no picker.
        var (viewModel, _) = CreateWithPreferences(stored: null);

        await viewModel.LoadNotificationPreferencesAsync();

        Assert.That(viewModel.IsNotificationFrequencyAvailable, Is.False);
    }

    [Test]
    public async Task ChangingTheFrequency_SendsTheWholeRecordBack()
    {
        // The endpoint replaces every preference, so sending only the frequency would silently
        // switch off the two push toggles the listener set on the web.
        var (viewModel, api) = CreateWithPreferences(new NotificationPreferences
        {
            ReceiveArtistReleasePush = true,
            ReceiveArtistMessageEmails = true,
            ArtistPushFrequency = ArtistPushFrequency.Instant,
        });

        await viewModel.LoadNotificationPreferencesAsync();
        viewModel.NotificationFrequency = ArtistPushFrequency.TwelveHours;

        api.Verify(
            x => x.SetAsync(
                It.Is<NotificationPreferences>(p =>
                    p.ArtistPushFrequency == ArtistPushFrequency.TwelveHours &&
                    p.ReceiveArtistReleasePush &&
                    p.ReceiveArtistMessageEmails),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task ConfigPage_WithNoPreferenceService_HasNoNotificationSection()
    {
        // The dependency is optional and trailing, so every pre-existing construction passes null.
        await _viewModel.LoadNotificationPreferencesAsync();

        Assert.That(_viewModel.IsNotificationFrequencyAvailable, Is.False);
    }

    [Test]
    public async Task ChangingTheFrequency_OnAServerThatIgnoresIt_SaysSoAndShowsTheRealValue()
    {
        // Exactly what an app newer than its server does: the unknown property is dropped, the PUT
        // answers OK, and the choice is gone by the next page open. Saying "Saved" there is a lie.
        var api = new Mock<INotificationPreferenceApiService>();
        api.Setup(x => x.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new NotificationPreferences { ArtistPushFrequency = ArtistPushFrequency.Instant });
        api.Setup(x => x.SetAsync(It.IsAny<NotificationPreferences>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var viewModel = new ConfigViewModel(_settings.Object, _audioCacheService.Object, api.Object);
        await viewModel.LoadNotificationPreferencesAsync();

        viewModel.NotificationFrequency = ArtistPushFrequency.Daily;

        // The save is fire-and-forget from the setter, so let it finish.
        await Task.Delay(50);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.NotificationFrequencyStatus, Does.Contain("does not support"));
            Assert.That(viewModel.NotificationFrequency, Is.EqualTo(ArtistPushFrequency.Instant));
        });
    }

    [Test]
    public async Task ChangingTheFrequency_WhenItSticks_SaysSaved()
    {
        var stored = new NotificationPreferences { ArtistPushFrequency = ArtistPushFrequency.Instant };
        var api = new Mock<INotificationPreferenceApiService>();
        api.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => stored);
        api.Setup(x => x.SetAsync(It.IsAny<NotificationPreferences>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var viewModel = new ConfigViewModel(_settings.Object, _audioCacheService.Object, api.Object);
        await viewModel.LoadNotificationPreferencesAsync();

        viewModel.NotificationFrequency = ArtistPushFrequency.Daily;
        await Task.Delay(50);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.NotificationFrequencyStatus, Is.EqualTo("Saved."));
            Assert.That(viewModel.NotificationFrequency, Is.EqualTo(ArtistPushFrequency.Daily));
        });
    }
}
