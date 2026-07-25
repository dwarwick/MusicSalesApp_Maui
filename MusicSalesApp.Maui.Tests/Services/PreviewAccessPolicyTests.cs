using Moq;
using MusicSalesApp.Common.Helpers;
using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Tests.Services;

[TestFixture]
public class PreviewAccessPolicyTests
{
    private Mock<IAuthService> _mockAuthService = null!;

    [SetUp]
    public void Setup()
    {
        _mockAuthService = new Mock<IAuthService>();
    }

    private static SongDto CreateSong(bool displayOnHomePage = false, int? creatorUserId = null) =>
        new()
        {
            Id = 1,
            SongTitle = "Test",
            DisplayOnHomePage = displayOnHomePage,
            CreatorUserId = creatorUserId,
            StreamUrl = "https://test.com/song.mp3"
        };

    [Test]
    public void ShouldLimitPreview_NonSubscriber_IsLimited()
    {
        _mockAuthService.Setup(a => a.HasActiveSubscription).Returns(false);

        Assert.That(PreviewAccessPolicy.ShouldLimitPreview(_mockAuthService.Object, CreateSong()), Is.True);
    }

    [Test]
    public void ShouldLimitPreview_Subscriber_IsNotLimited()
    {
        _mockAuthService.Setup(a => a.HasActiveSubscription).Returns(true);

        Assert.That(PreviewAccessPolicy.ShouldLimitPreview(_mockAuthService.Object, CreateSong()), Is.False);
    }

    [Test]
    public void ShouldLimitPreview_CancelledSubscriptionPastEndDate_IsLimited()
    {
        _mockAuthService.Setup(a => a.HasActiveSubscription).Returns(true);
        _mockAuthService.Setup(a => a.SubscriptionStatus).Returns(SubscriptionStatuses.Cancelled);
        _mockAuthService.Setup(a => a.SubscriptionEndDate).Returns(DateTime.UtcNow.AddDays(-1));

        Assert.That(PreviewAccessPolicy.ShouldLimitPreview(_mockAuthService.Object, CreateSong()), Is.True);
    }

    [Test]
    public void ShouldLimitPreview_CancelledSubscriptionBeforeEndDate_IsNotLimited()
    {
        _mockAuthService.Setup(a => a.HasActiveSubscription).Returns(true);
        _mockAuthService.Setup(a => a.SubscriptionStatus).Returns(SubscriptionStatuses.Cancelled);
        _mockAuthService.Setup(a => a.SubscriptionEndDate).Returns(DateTime.UtcNow.AddDays(1));

        Assert.That(PreviewAccessPolicy.ShouldLimitPreview(_mockAuthService.Object, CreateSong()), Is.False);
    }

    [Test]
    public void ShouldLimitPreview_AdminWithoutSubscription_IsNotLimited()
    {
        _mockAuthService.Setup(a => a.HasActiveSubscription).Returns(false);
        _mockAuthService.Setup(a => a.IsAdmin).Returns(true);

        Assert.That(PreviewAccessPolicy.ShouldLimitPreview(_mockAuthService.Object, CreateSong()), Is.False);
    }

    [Test]
    public void ShouldLimitPreview_AdminWithExpiredCancelledSubscription_IsNotLimited()
    {
        _mockAuthService.Setup(a => a.HasActiveSubscription).Returns(true);
        _mockAuthService.Setup(a => a.SubscriptionStatus).Returns(SubscriptionStatuses.Cancelled);
        _mockAuthService.Setup(a => a.SubscriptionEndDate).Returns(DateTime.UtcNow.AddDays(-1));
        _mockAuthService.Setup(a => a.IsAdmin).Returns(true);

        Assert.That(PreviewAccessPolicy.ShouldLimitPreview(_mockAuthService.Object, CreateSong()), Is.False);
    }

    [Test]
    public void ShouldLimitPreview_FeaturedSong_IsNotLimitedForAnyone()
    {
        _mockAuthService.Setup(a => a.HasActiveSubscription).Returns(false);

        Assert.That(
            PreviewAccessPolicy.ShouldLimitPreview(_mockAuthService.Object, CreateSong(displayOnHomePage: true)),
            Is.False);
    }

    [Test]
    public void ShouldLimitPreview_CreatorOwnSong_IsNotLimited()
    {
        _mockAuthService.Setup(a => a.HasActiveSubscription).Returns(false);
        _mockAuthService.Setup(a => a.IsCreator).Returns(true);
        _mockAuthService.Setup(a => a.UserId).Returns(100);

        Assert.That(
            PreviewAccessPolicy.ShouldLimitPreview(_mockAuthService.Object, CreateSong(creatorUserId: 100)),
            Is.False);
    }

    [Test]
    public void ShouldLimitPreview_CreatorOtherSong_IsLimited()
    {
        _mockAuthService.Setup(a => a.HasActiveSubscription).Returns(false);
        _mockAuthService.Setup(a => a.IsCreator).Returns(true);
        _mockAuthService.Setup(a => a.UserId).Returns(100);

        Assert.That(
            PreviewAccessPolicy.ShouldLimitPreview(_mockAuthService.Object, CreateSong(creatorUserId: 200)),
            Is.True);
    }

    [Test]
    public void ShouldLimitPreview_NoSongOrNoAuthService_IsNotLimited()
    {
        Assert.Multiple(() =>
        {
            Assert.That(PreviewAccessPolicy.ShouldLimitPreview(_mockAuthService.Object, null), Is.False);
            Assert.That(PreviewAccessPolicy.ShouldLimitPreview(null, null), Is.False);
        });
    }

    [Test]
    public void ShouldLimitPreview_AnonymousListener_IsLimited()
    {
        Assert.That(PreviewAccessPolicy.ShouldLimitPreview(null, CreateSong()), Is.True);
    }

    [Test]
    public void HasFullPlaybackAccess_AdminOrSubscriberOnly()
    {
        var anonymous = new Mock<IAuthService>();

        var subscriber = new Mock<IAuthService>();
        subscriber.Setup(a => a.HasActiveSubscription).Returns(true);

        var admin = new Mock<IAuthService>();
        admin.Setup(a => a.IsAdmin).Returns(true);

        Assert.Multiple(() =>
        {
            Assert.That(PreviewAccessPolicy.HasFullPlaybackAccess(anonymous.Object), Is.False);
            Assert.That(PreviewAccessPolicy.HasFullPlaybackAccess(subscriber.Object), Is.True);
            Assert.That(PreviewAccessPolicy.HasFullPlaybackAccess(admin.Object), Is.True);
        });
    }
}
