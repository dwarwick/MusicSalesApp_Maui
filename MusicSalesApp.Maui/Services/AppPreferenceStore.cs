using Microsoft.Maui.Storage;

namespace MusicSalesApp.Maui.Services;

public sealed class AppPreferenceStore : IAppPreferenceStore
{
    public bool GetBool(string key, bool defaultValue = false)
        => Preferences.Default.Get(key, defaultValue);

    public void SetBool(string key, bool value)
        => Preferences.Default.Set(key, value);

    public int GetInt(string key, int defaultValue = 0)
        => Preferences.Default.Get(key, defaultValue);

    public void SetInt(string key, int value)
        => Preferences.Default.Set(key, value);

    public string? GetString(string key)
        => Preferences.Default.Get<string?>(key, null);

    public void SetString(string key, string value)
        => Preferences.Default.Set(key, value);

    public void Remove(string key)
        => Preferences.Default.Remove(key);
}
