namespace MusicSalesApp.Maui.Services;

public interface IAppPreferenceStore
{
    bool GetBool(string key, bool defaultValue = false);

    void SetBool(string key, bool value);
}