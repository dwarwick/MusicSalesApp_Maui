namespace MusicSalesApp.Maui.Services;

public class BrowserService : IBrowserService
{
    public async Task OpenAsync(string url)
    {
        await Browser.Default.OpenAsync(url, BrowserLaunchMode.SystemPreferred);
    }
}
