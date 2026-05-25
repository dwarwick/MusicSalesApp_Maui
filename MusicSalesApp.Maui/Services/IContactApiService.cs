using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Services;

public interface IContactApiService
{
    Task<ContactSubmitResult> SubmitContactRequestAsync(string subject, string message);
}