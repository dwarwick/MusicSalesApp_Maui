using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Services;

public class TipApiService : ITipApiService
{
    private const string Client = "MusicSalesApi";
    private const string BaseRoute = "api/mobile/tips";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TipApiService> _logger;

    public TipApiService(IHttpClientFactory httpClientFactory, ILogger<TipApiService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public Task<TipOperationResponseDto> CreateOrderAsync(int creatorId, int? songMetadataId, decimal amount, string? deviceFingerprint = null)
    {
        return PostAsync($"{BaseRoute}/create-order", new CreateTipOrderRequestDto
        {
            CreatorId = creatorId,
            SongMetadataId = songMetadataId,
            Amount = amount,
            DeviceFingerprint = deviceFingerprint
        });
    }

    public Task<TipOperationResponseDto> CaptureAsync(string payPalOrderId)
    {
        return PostAsync($"{BaseRoute}/capture", new TipOrderRequestDto
        {
            PayPalOrderId = payPalOrderId
        });
    }

    public Task<TipOperationResponseDto> CancelAsync(string payPalOrderId)
    {
        return PostAsync($"{BaseRoute}/cancel", new TipOrderRequestDto
        {
            PayPalOrderId = payPalOrderId
        });
    }

    private async Task<TipOperationResponseDto> PostAsync(string requestPath, object payload)
    {
        var client = _httpClientFactory.CreateClient(Client);

        try
        {
            var response = await client.PostAsJsonAsync(requestPath, payload);
            if (!response.IsSuccessStatusCode)
            {
                var message = await ApiErrorMessageFormatter.ReadDisplayMessageAsync(response);
                return Failure(message);
            }

            return await response.Content.ReadFromJsonAsync<TipOperationResponseDto>()
                ?? Failure("The server returned an empty response.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tip request failed for {RequestPath}", requestPath);
            return Failure(ApiErrorMessageFormatter.FormatException(client.BaseAddress, requestPath, ex));
        }
    }

    private static TipOperationResponseDto Failure(string message)
    {
        return new TipOperationResponseDto
        {
            Success = false,
            ResultKind = TipResultKinds.PaymentFailure,
            Message = message
        };
    }
}