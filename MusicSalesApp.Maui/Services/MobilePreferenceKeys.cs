namespace MusicSalesApp.Maui.Services;

internal static class MobilePreferenceKeys
{
    public const string OfflineCacheLimitMb = "Mobile.OfflineCacheLimitMb";

    public const string AutoScrollToPlayingSong = "Mobile.AutoScrollToPlayingSong";

    /// <summary>
    /// The push token this install last successfully registered with the server.
    /// </summary>
    /// <remarks>
    /// Kept so signing out can unregister the exact token that was registered. By then the platform
    /// may hand back a different one, and unregistering the wrong token leaves the old registration
    /// live - which is how a signed-out phone carries on receiving someone's notifications.
    /// </remarks>
    public const string RegisteredPushToken = "Mobile.RegisteredPushToken";

    /// <summary>
    /// A random id for this installation, generated on first use. Lets the server replace a rotated
    /// token in place. Deliberately not derived from any hardware identifier - it identifies an
    /// install, not a handset or a person.
    /// </summary>
    public const string PushDeviceId = "Mobile.PushDeviceId";
}
