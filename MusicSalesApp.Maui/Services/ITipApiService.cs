using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Services;

public interface ITipApiService
{
    Task<TipOperationResponseDto> CreateOrderAsync(int creatorId, int? songMetadataId, decimal amount, string? deviceFingerprint = null);
    Task<TipOperationResponseDto> CaptureAsync(string payPalOrderId);
    Task<TipOperationResponseDto> CancelAsync(string payPalOrderId);
}