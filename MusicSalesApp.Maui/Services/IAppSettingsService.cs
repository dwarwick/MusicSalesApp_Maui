namespace MusicSalesApp.Maui.Services;

public interface IAppSettingsService
{
    Task<int> GetStreamQualifyingSecondsAsync();
}
