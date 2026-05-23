namespace MusicSalesApp.Maui.Services;

public static class AppleStoreProductLookupFailureMessage
{
    public static string Create(string productId, IReadOnlyList<string>? invalidProducts = null)
    {
        var invalidList = invalidProducts?
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.Ordinal)
            .ToArray() ?? [];

        if (invalidList.Contains(productId, StringComparer.Ordinal))
        {
            return $"StoreKit marked subscription product '{productId}' as invalid. Confirm the App Store Connect product ID matches exactly, the subscription is attached to the current app version, the Paid Applications Agreement is active, and the device is signed into a Sandbox Apple Account under Settings > Developer.";
        }

        if (invalidList.Length > 0)
        {
            return $"Subscription product '{productId}' was not returned by StoreKit. StoreKit reported these invalid product identifiers: {string.Join(", ", invalidList)}. Confirm the configured product ID matches App Store Connect exactly, the subscription is attached to the current app version, the Paid Applications Agreement is active, and the device is signed into a Sandbox Apple Account under Settings > Developer.";
        }

        return $"Subscription product '{productId}' was not returned by StoreKit. If you are testing on the iOS simulator, App Store Connect products often do not resolve directly; use an Xcode .storekit configuration file instead. If you are testing on a real device, confirm the subscription is attached to the current app version, the Paid Applications Agreement is active, the device is signed into a Sandbox Apple Account under Settings > Developer, and allow time for App Store Connect changes to propagate.";
    }
}