using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Services;

public class ContactApiService : IContactApiService
{
    private const string ContactRequestPath = "api/mobile/contact";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ContactApiService> _logger;

    public ContactApiService(IHttpClientFactory httpClientFactory, ILogger<ContactApiService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<ContactSubmitResult> SubmitContactRequestAsync(string subject, string message)
    {
        var client = _httpClientFactory.CreateClient("MusicSalesApi");

        try
        {
            var response = await client.PostAsJsonAsync(ContactRequestPath, new ContactRequestDto
            {
                Subject = subject,
                Message = message
            });

            if (response.IsSuccessStatusCode)
            {
                return new ContactSubmitResult(true);
            }

            var errorMessage = await ApiErrorMessageFormatter.ReadDisplayMessageAsync(response);
            return new ContactSubmitResult(false, errorMessage);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to submit mobile contact request.");
            return new ContactSubmitResult(false, "Unable to send your message. Please check your connection and try again.");
        }
    }
}