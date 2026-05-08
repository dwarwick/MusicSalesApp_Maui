namespace MusicSalesApp.Maui.Services;

public interface IAppPreferenceStore
{
    bool GetBool(string key, bool defaultValue = false);

    void SetBool(string key, bool value);

    string? GetString(string key);

    void SetString(string key, string value);

    void Remove(string key);
}