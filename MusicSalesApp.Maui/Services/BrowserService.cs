namespace MusicSalesApp.Maui.Services;

public class BrowserService : IBrowserService
{
    public async Task OpenAsync(string url)
    {
        await Browser.Default.OpenAsync(url, BrowserLaunchMode.SystemPreferred);
    }

    public async Task OpenExternalAsync(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new InvalidOperationException("The supplied URL was not valid.");

        await Launcher.Default.OpenAsync(uri);
    }
}
