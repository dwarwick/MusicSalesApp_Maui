using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.Tests.Services;

[TestFixture]
public class UnsolicitedPurchasePolicyTests
{
    /// <summary>
    /// The regression this exists for: refusing to finish renewals left them queued, and three had
    /// accumulated within fifteen minutes of a single sandbox purchase.
    /// </summary>
    [Test]
    public void ARenewal_IsFinished([Values(true, false)] bool canVerify)
    {
        var action = UnsolicitedPurchasePolicy.Decide(
            transactionId: "2000001221162229",
            originalTransactionId: "2000001221161618",
            canVerify);

        Assert.That(action, Is.EqualTo(UnsolicitedPurchaseAction.Finish));
    }

    [Test]
    public void AStandalonePurchase_IsVerifiedWhenItCanBe()
    {
        // A first purchase reports either no original ID or its own.
        var action = UnsolicitedPurchasePolicy.Decide(
            transactionId: "2000001221161618",
            originalTransactionId: null,
            canVerify: true);

        Assert.That(action, Is.EqualTo(UnsolicitedPurchaseAction.Verify));
    }

    [Test]
    public void AStandalonePurchase_ReportingItsOwnIdAsTheOriginal_IsStillAFirstPurchase()
    {
        var action = UnsolicitedPurchasePolicy.Decide(
            transactionId: "2000001221161618",
            originalTransactionId: "2000001221161618",
            canVerify: true);

        Assert.That(action, Is.EqualTo(UnsolicitedPurchaseAction.Verify));
    }

    /// <summary>
    /// Nobody is signed in, so the purchase cannot be attached to an account. Finishing it would
    /// discard a purchase the customer has paid for and the server has never heard of.
    /// </summary>
    [Test]
    public void AStandalonePurchase_ThatCannotBeVerified_IsHeld()
    {
        var action = UnsolicitedPurchasePolicy.Decide(
            transactionId: "2000001221161618",
            originalTransactionId: null,
            canVerify: false);

        Assert.That(action, Is.EqualTo(UnsolicitedPurchaseAction.Hold));
    }

    [Test]
    public void AnEmptyOriginalId_IsNotMistakenForARenewal()
    {
        var action = UnsolicitedPurchasePolicy.Decide(
            transactionId: "2000001221161618",
            originalTransactionId: "   ",
            canVerify: false);

        Assert.That(action, Is.EqualTo(UnsolicitedPurchaseAction.Hold));
    }
}
