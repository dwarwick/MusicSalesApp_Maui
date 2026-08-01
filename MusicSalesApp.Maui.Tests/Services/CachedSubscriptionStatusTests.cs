using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.Tests.Services;

[TestFixture]
public class CachedSubscriptionStatusTests
{
    private static readonly DateTime Now = new(2026, 7, 31, 12, 0, 0, DateTimeKind.Utc);

    private static CachedSubscriptionStatus Subscribed(
        DateTime? endsAt = null,
        DateTime? cachedAt = null) => new()
        {
            HasActiveSubscription = true,
            SubscriptionStatus = "ACTIVE",
            SubscriptionEndDate = endsAt,
            CachedAtUtc = cachedAt ?? Now
        };

    [Test]
    public void IsUsableAt_WithAnUnexpiredSubscription_IsUsable()
    {
        var snapshot = Subscribed(endsAt: Now.AddDays(20));

        Assert.That(snapshot.IsUsableAt(Now), Is.True);
    }

    [Test]
    public void IsUsableAt_AfterTheSubscriptionEndDate_IsNotUsable()
    {
        // The end date is the real authority on when access stops.
        var snapshot = Subscribed(endsAt: Now.AddMinutes(-1));

        Assert.That(snapshot.IsUsableAt(Now), Is.False);
    }

    [Test]
    public void IsUsableAt_WhenCancelledButStillInThePaidPeriod_IsUsable()
    {
        // Cancelling does not end access immediately — the user keeps the benefit they paid for
        // until the period runs out, and the cache has to honour that.
        var snapshot = Subscribed(endsAt: Now.AddDays(12)) with { SubscriptionStatus = "CANCELLED" };

        Assert.That(snapshot.IsUsableAt(Now), Is.True);
    }

    [Test]
    public void IsUsableAt_BeyondTheStalenessCap_IsNotUsable()
    {
        // Even with plenty of subscription left. This is the guard against entitlement that was
        // revoked server-side for a reason the end date knows nothing about.
        var snapshot = Subscribed(endsAt: Now.AddDays(300), cachedAt: Now.AddDays(-15));

        Assert.That(snapshot.IsUsableAt(Now), Is.False);
    }

    [Test]
    public void IsUsableAt_JustInsideTheStalenessCap_IsUsable()
    {
        var snapshot = Subscribed(endsAt: Now.AddDays(300), cachedAt: Now.AddDays(-13));

        Assert.That(snapshot.IsUsableAt(Now), Is.True);
    }

    [Test]
    public void IsUsableAt_WithNoEndDate_LeansEntirelyOnTheStalenessCap()
    {
        // A server that reports an active subscription without an EndDate would otherwise produce a
        // snapshot that never expires.
        var fresh = Subscribed(endsAt: null, cachedAt: Now.AddDays(-1));
        var ancient = Subscribed(endsAt: null, cachedAt: Now.AddDays(-30));

        Assert.Multiple(() =>
        {
            Assert.That(fresh.IsUsableAt(Now), Is.True);
            Assert.That(ancient.IsUsableAt(Now), Is.False);
        });
    }

    [Test]
    public void IsUsableAt_WhenTheClockHasBeenWoundBack_IsNotUsable()
    {
        // A snapshot stamped in the future can only mean the device clock moved backwards, which is
        // the one way an end-date check alone could be extended indefinitely.
        var snapshot = Subscribed(endsAt: Now.AddDays(20), cachedAt: Now.AddDays(2));

        Assert.That(snapshot.IsUsableAt(Now), Is.False);
    }

    [Test]
    public void IsUsableAt_WithSmallClockSkew_IsStillUsable()
    {
        var snapshot = Subscribed(endsAt: Now.AddDays(20), cachedAt: Now.AddMinutes(5));

        Assert.That(snapshot.IsUsableAt(Now), Is.True);
    }

    [Test]
    public void IsUsableAt_WithAnUnexpiredTrial_IsUsable()
    {
        var snapshot = new CachedSubscriptionStatus
        {
            IsOnTrial = true,
            TrialEndDate = Now.AddDays(3),
            CachedAtUtc = Now
        };

        Assert.That(snapshot.IsUsableAt(Now), Is.True);
    }

    [Test]
    public void IsUsableAt_AfterTheTrialEnded_IsNotUsable()
    {
        var snapshot = new CachedSubscriptionStatus
        {
            IsOnTrial = true,
            TrialEndDate = Now.AddMinutes(-1),
            CachedAtUtc = Now
        };

        Assert.That(snapshot.IsUsableAt(Now), Is.False);
    }

    [Test]
    public void IsUsableAt_WhenATrialHasConvertedToAPaidSubscription_IsUsable()
    {
        // The spent trial date must not veto a subscription that is genuinely paid up.
        var snapshot = new CachedSubscriptionStatus
        {
            HasActiveSubscription = true,
            IsOnTrial = true,
            TrialEndDate = Now.AddDays(-2),
            SubscriptionEndDate = Now.AddDays(28),
            CachedAtUtc = Now
        };

        Assert.That(snapshot.IsUsableAt(Now), Is.True);
    }

    [Test]
    public void IsUsableAt_WithNoEntitlementAtAll_IsNotUsable()
    {
        // Nothing to grant, so there is nothing worth applying.
        var snapshot = new CachedSubscriptionStatus { CachedAtUtc = Now };

        Assert.That(snapshot.IsUsableAt(Now), Is.False);
    }

    // --- Persistence round-trip ---
    //
    // The format is hand-rolled precisely so a trimmed, AOT-compiled Release build cannot quietly
    // return a defaulted snapshot the way reflection-based serialization can. These pin that.

    [Test]
    public void SerializeThenParse_PreservesEveryField()
    {
        var original = new CachedSubscriptionStatus
        {
            HasActiveSubscription = true,
            SubscriptionStatus = "ACTIVE",
            SubscriptionEndDate = new DateTime(2026, 9, 15, 8, 30, 0, DateTimeKind.Utc),
            IsOnTrial = true,
            TrialEndDate = new DateTime(2026, 8, 10, 6, 0, 0, DateTimeKind.Utc),
            BillingSource = "GooglePlay",
            CachedAtUtc = Now
        };

        Assert.That(CachedSubscriptionStatus.TryParse(original.Serialize(), out var parsed), Is.True);
        Assert.That(parsed, Is.EqualTo(original));
    }

    [Test]
    public void SerializeThenParse_PreservesNullDatesAndStrings()
    {
        var original = new CachedSubscriptionStatus { HasActiveSubscription = true, CachedAtUtc = Now };

        Assert.That(CachedSubscriptionStatus.TryParse(original.Serialize(), out var parsed), Is.True);
        Assert.That(parsed, Is.EqualTo(original));
    }

    [Test]
    public void SerializeThenParse_SurvivesAServerStringContainingTheSeparator()
    {
        var original = new CachedSubscriptionStatus
        {
            HasActiveSubscription = true,
            SubscriptionStatus = "ACTIVE|WEIRD",
            BillingSource = "Google|Play",
            CachedAtUtc = Now
        };

        Assert.That(CachedSubscriptionStatus.TryParse(original.Serialize(), out var parsed), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(parsed!.SubscriptionStatus, Is.EqualTo("ACTIVE|WEIRD"));
            Assert.That(parsed.BillingSource, Is.EqualTo("Google|Play"));
        });
    }

    [Test]
    public void SerializeThenParse_KeepsAnUnexpiredSubscriptionUsable()
    {
        // The round trip has to survive as *usable*, not merely as equal fields — this is the whole
        // path an offline launch depends on.
        var original = Subscribed(endsAt: Now.AddDays(20), cachedAt: Now.AddHours(-2));

        Assert.That(CachedSubscriptionStatus.TryParse(original.Serialize(), out var parsed), Is.True);
        Assert.That(parsed!.IsUsableAt(Now), Is.True);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("garbage")]
    [TestCase("v0|1||||||")]
    [TestCase("v1|1|ACTIVE")]
    public void TryParse_WithUnreadableInput_Fails(string? stored)
    {
        Assert.Multiple(() =>
        {
            Assert.That(CachedSubscriptionStatus.TryParse(stored, out var parsed), Is.False);
            Assert.That(parsed, Is.Null);
        });
    }

    [Test]
    public void IsUsableAt_TreatsUnspecifiedKindDatesAsUtc()
    {
        // Dates deserialized from JSON often arrive as Unspecified; reading them as local time
        // would shift every comparison by the device's offset.
        var snapshot = Subscribed(endsAt: DateTime.SpecifyKind(Now.AddHours(2), DateTimeKind.Unspecified));

        Assert.That(snapshot.IsUsableAt(Now), Is.True);
    }
}
