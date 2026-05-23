using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.Tests.Services;

[TestFixture]
public class ApplePurchaseResultFactoryTests
{
    [Test]
    public void CreateSuccess_UsesOriginalTransactionId_WhenProvided()
    {
        var result = ApplePurchaseResultFactory.CreateSuccess("tx-123", "orig-123", "streamtunes_monthly_sub_ios", "account-token");

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(result.Provider, Is.EqualTo(BillingProviders.Apple));
            Assert.That(result.TransactionId, Is.EqualTo("tx-123"));
            Assert.That(result.OriginalTransactionId, Is.EqualTo("orig-123"));
            Assert.That(result.ProductId, Is.EqualTo("streamtunes_monthly_sub_ios"));
            Assert.That(result.AppAccountToken, Is.EqualTo("account-token"));
        });
    }

    [Test]
    public void CreateSuccess_FallsBackToTransactionId_WhenOriginalTransactionMissing()
    {
        var result = ApplePurchaseResultFactory.CreateSuccess("tx-123", null, "streamtunes_monthly_sub_ios", null);

        Assert.That(result.OriginalTransactionId, Is.EqualTo("tx-123"));
    }
}