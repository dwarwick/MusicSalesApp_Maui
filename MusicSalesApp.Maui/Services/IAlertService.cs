namespace MusicSalesApp.Maui.Services;

public interface IAlertService
{
    Task DisplayAlertAsync(string title, string message, string cancel);
    Task<bool> ShowConfirmAsync(string title, string message, string accept = "Yes", string cancel = "No");
}
