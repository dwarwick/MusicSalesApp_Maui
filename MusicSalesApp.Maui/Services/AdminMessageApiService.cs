using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Services;

public class AdminMessageApiService : IAdminMessageApiService
{
    private const string PendingDialogsPath = "api/mobile/admin-messages/pending-dialogs";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AdminMessageApiService> _logger;

    public AdminMessageApiService(IHttpClientFactory httpClientFactory, ILogger<AdminMessageApiService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<PendingAdminMessageDto>> GetPendingDialogMessagesAsync()
    {
        var client = _httpClientFactory.CreateClient("MusicSalesApi");

        try
        {
            var messages = await client.GetFromJsonAsync<List<PendingAdminMessageDto>>(PendingDialogsPath);
            return messages ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch pending admin messages");
            return [];
        }
    }

    public async Task<bool> AcknowledgeMessageAsync(int messageId)
    {
        var client = _httpClientFactory.CreateClient("MusicSalesApi");

        try
        {
            var response = await client.PostAsync($"api/mobile/admin-messages/{messageId}/acknowledge", null);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to acknowledge admin message {MessageId}", messageId);
            return false;
        }
    }
}