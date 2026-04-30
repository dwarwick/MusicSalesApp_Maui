namespace MusicSalesApp.Maui.Services;

public class TipAmountPicker : ITipAmountPicker
{
    public Task<decimal?> PickAmountAsync(string songTitle)
    {
        return MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (Application.Current?.Windows.FirstOrDefault()?.Page is not Page page)
                return null;

            var pickerPage = new TipAmountPickerPage(songTitle);

            await page.Navigation.PushModalAsync(pickerPage, false);
            return await pickerPage.WaitForResultAsync();
        });
    }
}