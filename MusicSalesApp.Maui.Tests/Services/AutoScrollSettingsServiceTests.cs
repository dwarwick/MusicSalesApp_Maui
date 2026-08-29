using Moq;
using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.Tests.Services;

[TestFixture]
public class AutoScrollSettingsServiceTests
{
    private Mock<IAppPreferenceStore> _preferenceStore = null!;
    private AutoScrollSettingsService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _preferenceStore = new Mock<IAppPreferenceStore>();
        _service = new AutoScrollSettingsService(_preferenceStore.Object);
    }

    [Test]
    public void ScrollAutomatically_WhenNeverSet_DefaultsToOn()
    {
        // The setting exists to be turned OFF; a fresh install follows the playing song.
        _preferenceStore
            .Setup(p => p.GetBool(MobilePreferenceKeys.AutoScrollToPlayingSong, true))
            .Returns(true);

        Assert.That(_service.ScrollAutomatically, Is.True);
    }

    [Test]
    public void ScrollAutomatically_ReadsTheStoredValue()
    {
        GivenStoredValue(false);

        Assert.That(_service.ScrollAutomatically, Is.False);
    }

    [Test]
    public void ScrollAutomatically_WhenChanged_PersistsAndRaisesChanged()
    {
        GivenStoredValue(true);
        var changedCount = 0;
        _service.Changed += () => changedCount++;

        _service.ScrollAutomatically = false;

        _preferenceStore.Verify(
            p => p.SetBool(MobilePreferenceKeys.AutoScrollToPlayingSong, false),
            Times.Once);
        Assert.That(changedCount, Is.EqualTo(1));
    }

    [Test]
    public void ScrollAutomatically_WhenSetToTheValueItAlreadyHas_WritesNothingAndStaysQuiet()
    {
        // A two-way binding re-asserts its value whenever the title view is rebuilt. Treating that
        // as a change would make every list on screen jump to the playing song.
        GivenStoredValue(true);
        var changedCount = 0;
        _service.Changed += () => changedCount++;

        _service.ScrollAutomatically = true;

        _preferenceStore.Verify(
            p => p.SetBool(It.IsAny<string>(), It.IsAny<bool>()),
            Times.Never);
        Assert.That(changedCount, Is.Zero);
    }

    private void GivenStoredValue(bool value) =>
        _preferenceStore
            .Setup(p => p.GetBool(MobilePreferenceKeys.AutoScrollToPlayingSong, It.IsAny<bool>()))
            .Returns(value);
}
