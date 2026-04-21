namespace MusicSalesApp.Maui.Services;

public interface IAlertService
{
    Task DisplayAlertAsync(string title, string message, string cancel);
    Task<bool> ShowConfirmAsync(string title, string message, string accept = "Yes", string cancel = "No");
    Task<string?> ShowActionSheetAsync(string title, string cancel, string? destruction, params string[] buttons);

    /// <summary>
    /// Shows a text-input prompt. Returns null if the user cancels; otherwise the entered text (may be empty).
    /// </summary>
    Task<string?> ShowPromptAsync(string title, string message, string accept = "OK", string cancel = "Cancel", string? placeholder = null, string? initialValue = null, int maxLength = -1);
}
