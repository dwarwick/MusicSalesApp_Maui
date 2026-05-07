using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Services;

public interface IAdminMessageApiService
{
    Task<IReadOnlyList<PendingAdminMessageDto>> GetPendingDialogMessagesAsync();

    Task<bool> AcknowledgeMessageAsync(int messageId);
}