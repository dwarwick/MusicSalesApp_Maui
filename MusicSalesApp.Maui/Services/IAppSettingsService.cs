namespace MusicSalesApp.Maui.Services;

public interface IAppSettingsService
{
    Task<string> GetSubscriptionPriceAsync();
    Task<int> GetStreamQualifyingSecondsAsync();
}
