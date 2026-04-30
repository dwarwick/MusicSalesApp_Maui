namespace MusicSalesApp.Maui.Services;

public interface ITipAmountPicker
{
    Task<decimal?> PickAmountAsync(string songTitle);
}