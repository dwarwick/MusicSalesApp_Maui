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

    private static SongDto CreateSong(
        bool displayOnHomePage = false,
        int? creatorUserId = null,
        int? creatorId = null) =>
        new()
        {
            Id = 1,
            SongTitle = "Test",
            DisplayOnHomePage = displayOnHomePage,
            CreatorUserId = creatorUserId,
            CreatorId = creatorId,
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
        // Signed in, because a creator always is - the real AuthService nulls UserId on
        // logout, so "has a UserId but is signed out" is a state it cannot produce.
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(true);

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

    [Test]
    public void ShouldLimitPreview_CreatorOwnSong_MatchedOnlyByCreatorId_IsNotLimited()
    {
        // The bug that consolidating the ownership rule fixed. CreatorUserId is sent as
        // Creator?.UserId, so it is absent whenever that navigation was not eager-loaded - and this
        // check used to test CreatorUserId alone. The follow bell correctly vanished from the card
        // (it matched on CreatorId), while the same creator was cut off at sixty seconds of their
        // own song. One rule now answers for both.
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(true);
        _mockAuthService.Setup(a => a.HasActiveSubscription).Returns(false);
        _mockAuthService.Setup(a => a.IsCreator).Returns(true);
        _mockAuthService.Setup(a => a.CreatorId).Returns(90);

        var song = CreateSong(creatorUserId: null, creatorId: 90);

        Assert.That(PreviewAccessPolicy.ShouldLimitPreview(_mockAuthService.Object, song), Is.False);
    }

    [Test]
    public void ShouldLimitPreview_SignedOutListener_IsStillLimited()
    {
        // The null-equals-null trap the guards exist for: both sides are int?, so an unguarded
        // comparison is TRUE for a signed-out visitor looking at a song with no creator loaded,
        // which would hand out every song in full.
        _mockAuthService.Setup(a => a.IsLoggedIn).Returns(false);
        _mockAuthService.Setup(a => a.HasActiveSubscription).Returns(false);

        var song = CreateSong(creatorUserId: null, creatorId: null);

        Assert.That(PreviewAccessPolicy.ShouldLimitPreview(_mockAuthService.Object, song), Is.True);
    }
}
