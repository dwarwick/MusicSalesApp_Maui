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

    public async Task<string?> ShowActionSheetAsync(string title, string cancel, string? destruction, params string[] buttons)
    {
        if (Application.Current?.Windows.FirstOrDefault()?.Page is Page page)
        {
            return await page.DisplayActionSheetAsync(title, cancel, destruction, buttons);
        }
        return null;
    }

    public async Task<string?> ShowPromptAsync(string title, string message, string accept = "OK", string cancel = "Cancel", string? placeholder = null, string? initialValue = null, int maxLength = -1)
    {
        if (Application.Current?.Windows.FirstOrDefault()?.Page is Page page)
        {
            return await page.DisplayPromptAsync(title, message, accept, cancel, placeholder, maxLength, null, initialValue ?? string.Empty);
        }
        return null;
    }
}
