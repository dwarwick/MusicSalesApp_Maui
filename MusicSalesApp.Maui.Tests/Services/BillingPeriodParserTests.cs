using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.Tests.Services;

[TestFixture]
public class BillingPeriodParserTests
{
    [TestCase("P3D", 3)]
    [TestCase("P1W", 7)]
    [TestCase("P2W", 14)]
    [TestCase("P1M", 30)]
    [TestCase("P2M", 60)]
    [TestCase("P1Y", 365)]
    [TestCase("P1M7D", 37)]
    [TestCase("p1w", 7)]
    public void ParseIso8601PeriodDays_ReturnsApproximateDays(string period, int expectedDays)
    {
        var result = BillingPeriodParser.ParseIso8601PeriodDays(period);

        Assert.That(result, Is.EqualTo(expectedDays));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("P")]
    [TestCase("PT3D")]
    [TestCase("3D")]
    [TestCase("PXD")]
    public void ParseIso8601PeriodDays_ReturnsNullForUnsupportedPeriods(string? period)
    {
        var result = BillingPeriodParser.ParseIso8601PeriodDays(period);

        Assert.That(result, Is.Null);
    }
}