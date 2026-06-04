using System.Globalization;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Tests.ViewModels;

[TestFixture]
public class SubscriptionOfferDisplayBuilderTests
{
    private CultureInfo _originalCulture = null!;
    private CultureInfo _originalUICulture = null!;

    [SetUp]
    public void SetUp()
    {
        _originalCulture = CultureInfo.CurrentCulture;
        _originalUICulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
    }

    [TearDown]
    public void TearDown()
    {
        CultureInfo.CurrentCulture = _originalCulture;
        CultureInfo.CurrentUICulture = _originalUICulture;
    }

    [Test]
    public void FormatMonthlyPrice_WithGooglePlayPesoPrice_DoesNotPrependDollarSign()
    {
        var price = SubscriptionOfferDisplayBuilder.FormatMonthlyPrice("\u20B1205.00", "3.99");

        Assert.Multiple(() =>
        {
            Assert.That(price, Is.EqualTo("\u20B1205.00"));
            Assert.That(price, Does.Not.StartWith("$"));
        });
    }

    [Test]
    public void FormatMonthlyPrice_WithNumericFallback_UsesCurrentCultureCurrency()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-PH");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-PH");

        var price = SubscriptionOfferDisplayBuilder.FormatMonthlyPrice(null, "205.00");

        Assert.Multiple(() =>
        {
            Assert.That(price, Is.EqualTo("\u20B1205.00"));
            Assert.That(price, Does.Not.StartWith("$"));
        });
    }

    [Test]
    public void FormatMonthlyPriceOrEmpty_WhenFallbackDisabledAndStorePriceMissing_ReturnsEmpty()
    {
        var price = SubscriptionOfferDisplayBuilder.FormatMonthlyPriceOrEmpty(null, "3.99", allowFallbackPrice: false);

        Assert.That(price, Is.Empty);
    }

    [Test]
    public void Create_WhenFallbackDisabledAndStorePriceMissing_UsesGooglePlayPriceCopy()
    {
        var display = SubscriptionOfferDisplayBuilder.Create(
            showFreeTrialTerms: true,
            freeTrialDays: 3,
            renewalPrice: null,
            fallbackPrice: "3.99",
            allowFallbackPrice: false);

        Assert.Multiple(() =>
        {
            Assert.That(display.PriceText, Is.Empty);
            Assert.That(display.Body, Does.Contain("monthly price shown in Google Play"));
            Assert.That(display.Body, Does.Not.Contain("$3.99"));
        });
    }
}
