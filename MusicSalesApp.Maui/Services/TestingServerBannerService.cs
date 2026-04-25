namespace MusicSalesApp.Maui.Services;

public interface ITestingServerBannerService
{
    TestingServerBannerInfo GetBannerInfo();
}

public sealed record TestingServerBannerInfo(bool IsVisible, string MessagePrefix, string Url)
{
    public static TestingServerBannerInfo Hidden { get; } = new(false, string.Empty, string.Empty);

    public string DisplayText => string.IsNullOrWhiteSpace(Url)
        ? MessagePrefix
        : $"{MessagePrefix} {Url}";
}

public class TestingServerBannerService : ITestingServerBannerService
{
    private const string BannerPrefix = "Streamtunes Testing - Backend Server is";
    private const string TestingHost = "davidtest.dev";
    private readonly IAppConfig _appConfig;

    public TestingServerBannerService(IAppConfig appConfig)
    {
        _appConfig = appConfig;
    }

    public TestingServerBannerInfo GetBannerInfo()
    {
        var url = ResolveBannerUrl();
        return IsTestingServerUrl(url)
            ? new TestingServerBannerInfo(true, BannerPrefix, url!)
            : TestingServerBannerInfo.Hidden;
    }

    private string? ResolveBannerUrl()
    {
        if (!string.IsNullOrWhiteSpace(_appConfig.WebBaseUrl))
        {
            return _appConfig.WebBaseUrl.TrimEnd('/');
        }

        if (!string.IsNullOrWhiteSpace(_appConfig.ApiBaseUrl))
        {
            return _appConfig.ApiBaseUrl.TrimEnd('/');
        }

        return null;
    }

    private static bool IsTestingServerUrl(string? url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && string.Equals(uri.Host, TestingHost, StringComparison.OrdinalIgnoreCase);
    }
}