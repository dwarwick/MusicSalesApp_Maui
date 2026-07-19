namespace MusicSalesApp.Maui.Services;

public interface IToastService
{
    Task ShowAsync(string message, CancellationToken cancellationToken = default);
}
