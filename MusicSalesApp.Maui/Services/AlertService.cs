namespace MusicSalesApp.Maui.Services;

public class AlertService : IAlertService
{
    public async Task DisplayAlertAsync(string title, string message, string cancel)
    {
        if (Application.Current?.Windows.FirstOrDefault()?.Page is Page page)
        {
            await page.DisplayAlertAsync(title, message, cancel);
        }
    }

    public async Task<bool> ShowConfirmAsync(string title, string message, string accept = "Yes", string cancel = "No")
    {
        if (Application.Current?.Windows.FirstOrDefault()?.Page is Page page)
        {
            return await page.DisplayAlertAsync(title, message, accept, cancel);
        }
        return false;
    }
}
