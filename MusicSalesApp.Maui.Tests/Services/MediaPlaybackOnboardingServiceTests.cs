using Microsoft.Maui.ApplicationModel;
using Moq;
using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.Tests.Services;

[TestFixture]
public class MediaPlaybackOnboardingServiceTests
{
    private Mock<IAppPreferenceStore> _preferences = null!;
    private Mock<IPermissionExplainerService> _explainer = null!;
    private Mock<IMicrophonePermissionService> _microphonePermission = null!;
    private MediaPlaybackOnboardingService _service = null!;

    [SetUp]
    public void Setup()
    {
        _preferences = new Mock<IAppPreferenceStore>();
        _explainer = new Mock<IPermissionExplainerService>();
        _microphonePermission = new Mock<IMicrophonePermissionService>();
        _service = new MediaPlaybackOnboardingService(_preferences.Object, _explainer.Object, _microphonePermission.Object);
    }

    [Test]
    public async Task EnsureBackgroundPlaybackExplainedAsync_DoesNothing()
    {
        await _service.EnsureBackgroundPlaybackExplainedAsync();

        _explainer.Verify(e => e.ShowAsync(It.IsAny<PermissionExplainerRequest>()), Times.Never);
        _preferences.Verify(p => p.SetBool(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [Test]
    public async Task EnsureMicrophonePermissionAsync_WhenAlreadyGranted_SkipsExplainerAndRequest()
    {
        _microphonePermission.Setup(p => p.CheckStatusAsync()).ReturnsAsync(PermissionStatus.Granted);

        var granted = await _service.EnsureMicrophonePermissionAsync();

        Assert.That(granted, Is.True);
        _explainer.Verify(e => e.ShowAsync(It.IsAny<PermissionExplainerRequest>()), Times.Never);
        _microphonePermission.Verify(p => p.RequestAsync(), Times.Never);
    }

    [Test]
    public async Task EnsureMicrophonePermissionAsync_WhenExplainerNeeded_ShowsExplainerThenRequestsPermission()
    {
        _microphonePermission.Setup(p => p.CheckStatusAsync()).ReturnsAsync(PermissionStatus.Denied);
        _microphonePermission.Setup(p => p.RequestAsync()).ReturnsAsync(PermissionStatus.Granted);
        _preferences.Setup(p => p.GetBool("MediaPlayback.MicrophoneExplainerSuppressed", false)).Returns(false);
        _explainer.Setup(e => e.ShowAsync(It.IsAny<PermissionExplainerRequest>())).ReturnsAsync(new PermissionExplainerResult(true, false));

        var granted = await _service.EnsureMicrophonePermissionAsync();

        Assert.That(granted, Is.True);
        _explainer.Verify(e => e.ShowAsync(It.Is<PermissionExplainerRequest>(r => r.Overline == "Equalizer visualization" && r.ShowDoNotAskAgainOption)), Times.Once);
        _preferences.Verify(p => p.SetBool(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
        _microphonePermission.Verify(p => p.RequestAsync(), Times.Once);
    }

    [Test]
    public async Task EnsureMicrophonePermissionAsync_WhenExplainerDismissedWithoutOptOut_AsksAgainLater()
    {
        _microphonePermission.Setup(p => p.CheckStatusAsync()).ReturnsAsync(PermissionStatus.Denied);
        _preferences.Setup(p => p.GetBool("MediaPlayback.MicrophoneExplainerSuppressed", false)).Returns(false);
        _explainer.Setup(e => e.ShowAsync(It.IsAny<PermissionExplainerRequest>())).ReturnsAsync(new PermissionExplainerResult(false, false));

        var granted = await _service.EnsureMicrophonePermissionAsync();

        Assert.That(granted, Is.False);
        _microphonePermission.Verify(p => p.RequestAsync(), Times.Never);
        _preferences.Verify(p => p.SetBool(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [Test]
    public async Task EnsureMicrophonePermissionAsync_WhenExplainerDismissedWithOptOut_DoesNotAskAgain()
    {
        _microphonePermission.Setup(p => p.CheckStatusAsync()).ReturnsAsync(PermissionStatus.Denied);
        _preferences.SetupSequence(p => p.GetBool("MediaPlayback.MicrophoneExplainerSuppressed", false))
            .Returns(false)
            .Returns(true);
        _explainer.Setup(e => e.ShowAsync(It.IsAny<PermissionExplainerRequest>())).ReturnsAsync(new PermissionExplainerResult(false, true));

        var firstResult = await _service.EnsureMicrophonePermissionAsync();
        var secondResult = await _service.EnsureMicrophonePermissionAsync();

        Assert.That(firstResult, Is.False);
        Assert.That(secondResult, Is.False);
        _explainer.Verify(e => e.ShowAsync(It.IsAny<PermissionExplainerRequest>()), Times.Once);
        _preferences.Verify(p => p.SetBool("MediaPlayback.MicrophoneExplainerSuppressed", true), Times.Once);
        _microphonePermission.Verify(p => p.RequestAsync(), Times.Never);
    }

    [Test]
    public async Task EnsureMicrophonePermissionAsync_WhenPermissionDeniedAfterContinueWithOptOut_DoesNotAskAgain()
    {
        _microphonePermission.SetupSequence(p => p.CheckStatusAsync())
            .ReturnsAsync(PermissionStatus.Denied)
            .ReturnsAsync(PermissionStatus.Denied);
        _microphonePermission.Setup(p => p.RequestAsync()).ReturnsAsync(PermissionStatus.Denied);
        _preferences.SetupSequence(p => p.GetBool("MediaPlayback.MicrophoneExplainerSuppressed", false))
            .Returns(false)
            .Returns(true);
        _explainer.Setup(e => e.ShowAsync(It.IsAny<PermissionExplainerRequest>())).ReturnsAsync(new PermissionExplainerResult(true, true));

        var firstResult = await _service.EnsureMicrophonePermissionAsync();
        var secondResult = await _service.EnsureMicrophonePermissionAsync();

        Assert.That(firstResult, Is.False);
        Assert.That(secondResult, Is.False);
        _explainer.Verify(e => e.ShowAsync(It.IsAny<PermissionExplainerRequest>()), Times.Once);
        _preferences.Verify(p => p.SetBool("MediaPlayback.MicrophoneExplainerSuppressed", true), Times.Once);
        _microphonePermission.Verify(p => p.RequestAsync(), Times.Once);
    }

    [Test]
    public async Task EnsureMicrophonePermissionAsync_WhenSuppressed_DoesNotShowExplainer()
    {
        _microphonePermission.Setup(p => p.CheckStatusAsync()).ReturnsAsync(PermissionStatus.Denied);
        _preferences.Setup(p => p.GetBool("MediaPlayback.MicrophoneExplainerSuppressed", false)).Returns(true);

        var granted = await _service.EnsureMicrophonePermissionAsync();

        Assert.That(granted, Is.False);
        _explainer.Verify(e => e.ShowAsync(It.IsAny<PermissionExplainerRequest>()), Times.Never);
        _microphonePermission.Verify(p => p.RequestAsync(), Times.Never);
    }
}