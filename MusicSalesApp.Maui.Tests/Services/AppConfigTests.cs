using Microsoft.Extensions.Configuration;
using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.Tests.Services;

[TestFixture]
public class AppConfigTests
{
    [Test]
    public void Constructor_FallsBackToProductionUrl_WhenApiBaseUrlIsEmpty()
    {
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["ApiBaseUrl"] = string.Empty
        });

        var appConfig = new AppConfig(configuration);

        Assert.That(appConfig.ApiBaseUrl, Is.EqualTo("https://streamtunes.net"));
        Assert.That(appConfig.WebBaseUrl, Is.EqualTo("https://streamtunes.net"));
    }

    [Test]
    public void Constructor_UsesDavidTestUrl_WhenUseLocalHostIsFalseAndPrimaryUrlIsEmpty()
    {
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["UseLocalHost"] = "false",
            ["ApiBaseUrl"] = string.Empty,
            ["DavidTest:ApiBaseUrl"] = "https://davidtest.streamtunes.net"
        });

        var appConfig = new AppConfig(configuration);

        Assert.That(appConfig.ApiBaseUrl, Is.EqualTo("https://davidtest.streamtunes.net"));
    }

    [Test]
    public void FirstNonEmpty_ReturnsFirstNonBlankValue()
    {
        var result = AppConfig.FirstNonEmpty(null, string.Empty, "  ", "https://streamtunes.net");

        Assert.That(result, Is.EqualTo("https://streamtunes.net"));
    }

    private static IConfiguration CreateConfiguration(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}