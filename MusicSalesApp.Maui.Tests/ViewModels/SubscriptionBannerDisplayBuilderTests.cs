using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Tests.ViewModels;

[TestFixture]
public class SubscriptionBannerDisplayBuilderTests
{
    /// <summary>
    /// The reported bug: a fresh install sat at the default Unverified and told a user who had
    /// never signed in that their subscription features were paused.
    /// </summary>
    [Test]
    public void Create_WhenSignedOut_IsAlwaysHidden(
        [Values(SubscriptionVerificationState.Unverified,
                SubscriptionVerificationState.Cached,
                SubscriptionVerificationState.Verified)] SubscriptionVerificationState verification,
        [Values(true, false)] bool isOffline)
    {
        var banner = SubscriptionBannerDisplayBuilder.Create(isSignedIn: false, verification, isOffline);

        Assert.Multiple(() =>
        {
            Assert.That(banner.IsVisible, Is.False);
            Assert.That(banner.Text, Is.Empty);
        });
    }

    [Test]
    public void Create_WhenVerified_IsHidden([Values(true, false)] bool isOffline)
    {
        var banner = SubscriptionBannerDisplayBuilder.Create(
            isSignedIn: true, SubscriptionVerificationState.Verified, isOffline);

        Assert.Multiple(() =>
        {
            Assert.That(banner.IsVisible, Is.False);
            Assert.That(banner.Text, Is.Empty);
        });
    }

    [Test]
    public void Create_WhenCachedAndOffline_SaysLastConfirmedAndOffline()
    {
        var banner = SubscriptionBannerDisplayBuilder.Create(
            isSignedIn: true, SubscriptionVerificationState.Cached, isOffline: true);

        Assert.Multiple(() =>
        {
            Assert.That(banner.IsVisible, Is.True);
            Assert.That(banner.Text, Does.Contain("last confirmed"));
            Assert.That(banner.Text, Does.Contain("offline"));
            Assert.That(banner.Text, Does.Not.Contain("paused"));
        });
    }

    /// <summary>
    /// The copy used to assert "you're offline" unconditionally, which reads as a bug in the app
    /// when the device is plainly on wifi.
    /// </summary>
    [Test]
    public void Create_WhenCachedAndOnline_DoesNotClaimTheUserIsOffline()
    {
        var banner = SubscriptionBannerDisplayBuilder.Create(
            isSignedIn: true, SubscriptionVerificationState.Cached, isOffline: false);

        Assert.Multiple(() =>
        {
            Assert.That(banner.IsVisible, Is.True);
            Assert.That(banner.Text, Does.Contain("last confirmed"));
            Assert.That(banner.Text, Does.Not.Contain("offline"));
            Assert.That(banner.Text, Does.Not.Contain("paused"));
        });
    }

    [Test]
    public void Create_WhenUnverifiedAndOffline_SaysPausedAndOffline()
    {
        var banner = SubscriptionBannerDisplayBuilder.Create(
            isSignedIn: true, SubscriptionVerificationState.Unverified, isOffline: true);

        Assert.Multiple(() =>
        {
            Assert.That(banner.IsVisible, Is.True);
            Assert.That(banner.Text, Does.Contain("paused"));
            Assert.That(banner.Text, Does.Contain("offline"));
        });
    }

    [Test]
    public void Create_WhenUnverifiedAndOnline_DoesNotClaimTheUserIsOffline()
    {
        var banner = SubscriptionBannerDisplayBuilder.Create(
            isSignedIn: true, SubscriptionVerificationState.Unverified, isOffline: false);

        Assert.Multiple(() =>
        {
            Assert.That(banner.IsVisible, Is.True);
            Assert.That(banner.Text, Does.Contain("paused"));
            Assert.That(banner.Text, Does.Not.Contain("offline"));
        });
    }
}
