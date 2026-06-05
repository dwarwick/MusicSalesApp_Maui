using Moq;
using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Tests.ViewModels;

[TestFixture]
public class ConfigViewModelTests
{
    private Mock<IOfflineCacheSettingsService> _settings = null!;
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

        _viewModel = new ConfigViewModel(_settings.Object);
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
}
