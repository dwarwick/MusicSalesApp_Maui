using Microsoft.Maui.Storage;

namespace MusicSalesApp.Maui.Services;

public sealed class AppPreferenceStore : IAppPreferenceStore
{
    public bool GetBool(string key, bool defaultValue = false)
        => Preferences.Default.Get(key, defaultValue);

    public void SetBool(string key, bool value)
        => Preferences.Default.Set(key, value);
}