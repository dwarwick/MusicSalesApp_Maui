namespace MusicSalesApp.Maui.Services;

public interface IAppPreferenceStore
{
    bool GetBool(string key, bool defaultValue = false);

    void SetBool(string key, bool value);

    int GetInt(string key, int defaultValue = 0);

    void SetInt(string key, int value);

    string? GetString(string key);

    void SetString(string key, string value);

    void Remove(string key);
}
