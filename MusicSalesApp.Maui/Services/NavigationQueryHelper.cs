namespace MusicSalesApp.Maui.Services;

public static class NavigationQueryHelper
{
    public static bool TryReadBoolean(IDictionary<string, object> query, string key, out bool value)
    {
        value = false;
        if (!query.TryGetValue(key, out var raw))
        {
            return false;
        }

        switch (raw)
        {
            case bool boolValue:
                value = boolValue;
                return true;
            case string textValue when bool.TryParse(textValue, out var parsed):
                value = parsed;
                return true;
            default:
                return false;
        }
    }
}
