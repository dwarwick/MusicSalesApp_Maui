namespace MusicSalesApp.Maui.Services;

/// <summary>
/// The platform half of push: asking the OS for permission, and getting this device's token.
/// </summary>
/// <remarks>
/// Deliberately narrow, and deliberately free of any MAUI or native type. Everything that decides
/// <i>when</i> to register lives in <see cref="PushNotificationCoordinator"/>, which is a plain
/// class the test project compiles - the platform implementations under <c>Platforms/</c> are not
/// compiled into tests at all, so anything testable has to sit on this side of the interface.
/// </remarks>
public interface IPushRegistrationService
{
    /// <summary>
    /// False on platforms with no push transport - Windows and Mac Catalyst here. Callers branch on
    /// this rather than on a platform check, so adding a transport later changes one registration.
    /// </summary>
    bool IsSupported { get; }

    /// <summary>
    /// Asks the OS for notification permission, returning what the user chose.
    /// </summary>
    /// <remarks>
    /// Both platforms only ever show the system prompt once - a second call after a denial returns
    /// the denial without showing anything - so this must be called at a moment the user
    /// understands, not on first launch before they have seen the app.
    /// </remarks>
    Task<PushPermissionStatus> RequestPermissionAsync();

    /// <summary>
    /// The current permission, without prompting.
    /// </summary>
    Task<PushPermissionStatus> GetPermissionStatusAsync();

    /// <summary>
    /// This device's push token, or null when there is none - permission refused, the platform
    /// unsupported, or the push service unreachable.
    /// </summary>
    Task<string?> GetTokenAsync();

    /// <summary>
    /// Raised when the platform issues a new token for this device.
    /// </summary>
    /// <remarks>
    /// Tokens rotate on their own - a restore to a new phone, a reinstall, or the service simply
    /// deciding to. Without handling this the app keeps a token the server will be told is dead,
    /// and the user silently stops receiving anything.
    /// </remarks>
    event EventHandler<string>? TokenRefreshed;
}

/// <summary>
/// What the OS says about notification permission.
/// </summary>
public enum PushPermissionStatus
{
    /// <summary>Never asked. The system prompt has not been shown yet.</summary>
    NotDetermined,

    Granted,

    /// <summary>
    /// Refused. Asking again does nothing on either platform - the user has to change it in system
    /// settings - so the app must not keep prompting.
    /// </summary>
    Denied,

    /// <summary>No push transport on this platform.</summary>
    Unsupported,
}
