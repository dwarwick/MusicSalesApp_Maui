namespace MusicSalesApp.Maui.Services;

public class AlertService : IAlertService
{
    public Task DisplayAlertAsync(string title, string message, string cancel)
        => InvokeOnPageAsync(page => page.DisplayAlertAsync(title, message, cancel));

    public Task<bool> ShowConfirmAsync(string title, string message, string accept = "Yes", string cancel = "No")
        => InvokeOnPageAsync(page => page.DisplayAlertAsync(title, message, accept, cancel), false);

    public Task<string?> ShowActionSheetAsync(string title, string cancel, string? destruction, params string[] buttons)
        => InvokeOnPageAsync(page => page.DisplayActionSheetAsync(title, cancel, destruction, buttons), (string?)null);

    public Task<string?> ShowPromptAsync(string title, string message, string accept = "OK", string cancel = "Cancel", string? placeholder = null, string? initialValue = null, int maxLength = -1)
        => InvokeOnPageAsync(
            page => page.DisplayPromptAsync(title, message, accept, cancel, placeholder, maxLength, null, initialValue ?? string.Empty),
            (string?)null);

    private static Task InvokeOnPageAsync(Func<Page, Task> callback)
    {
        return MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (Application.Current?.Windows.FirstOrDefault()?.Page is Page page)
            {
                await callback(page);
            }
        });
    }

    private static Task<T> InvokeOnPageAsync<T>(Func<Page, Task<T>> callback, T fallback)
    {
        return MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (Application.Current?.Windows.FirstOrDefault()?.Page is Page page)
            {
                return await callback(page);
            }

            return fallback;
        });
    }
}
