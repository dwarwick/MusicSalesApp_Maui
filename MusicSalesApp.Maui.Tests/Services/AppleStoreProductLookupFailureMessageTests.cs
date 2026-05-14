using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.Tests.Services;

[TestFixture]
public class AppleStoreProductLookupFailureMessageTests
{
    [Test]
    public void Create_WithoutInvalidProducts_IncludesProductIdAndGeneralGuidance()
    {
        var message = AppleStoreProductLookupFailureMessage.Create("streamtunes_monthly_sub_ios");

        Assert.Multiple(() =>
        {
            Assert.That(message, Does.Contain("streamtunes_monthly_sub_ios"));
            Assert.That(message, Does.Contain("iOS simulator"));
            Assert.That(message, Does.Contain(".storekit"));
            Assert.That(message, Does.Contain("Sandbox Apple Account"));
        });
    }

    [Test]
    public void Create_WhenStoreKitMarksRequestedProductInvalid_HighlightsInvalidProductState()
    {
        var message = AppleStoreProductLookupFailureMessage.Create(
            "streamtunes_monthly_sub_ios",
            ["streamtunes_monthly_sub_ios"]);

        Assert.Multiple(() =>
        {
            Assert.That(message, Does.Contain("marked subscription product 'streamtunes_monthly_sub_ios' as invalid"));
            Assert.That(message, Does.Not.Contain("iOS simulator"));
            Assert.That(message, Does.Contain("Paid Applications Agreement"));
        });
    }
}