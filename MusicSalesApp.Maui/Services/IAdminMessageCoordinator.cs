namespace MusicSalesApp.Maui.Services;

public interface IAdminMessageCoordinator
{
    Task InitializeAsync();

    Task ProcessPendingMessagesAsync();
}