namespace MusicSalesApp.Maui.Services;

/// <summary>
/// Keeps this device's push registration in step with who is signed in.
/// </summary>
public interface IPushNotificationCoordinator
{
    /// <summary>
    /// Registers this device when someone is signed in and has granted permission, and unregisters
    /// it when nobody is. Safe to call from anywhere, as often as you like - concurrent calls
    /// collapse into one.
    /// </summary>
    /// <remarks>
    /// Never shows a permission prompt. It runs on activation and on auth changes, and a system
    /// prompt appearing at an unexplained moment is how people come to deny it - which is close to
    /// permanent, because neither platform shows it twice.
    /// </remarks>
    Task SyncAsync();

    /// <summary>
    /// Asks the OS for permission and, if granted, registers. Call this from somewhere the user has
    /// just expressed interest in notifications, so the prompt has a reason next to it.
    /// </summary>
    Task<PushPermissionStatus> RequestPermissionAndRegisterAsync();
}

/// <summary>
/// The no-op used where there is no push transport - Windows and Mac Catalyst.
/// </summary>
/// <remarks>
/// A real implementation that does nothing, rather than a null check at every call site. Matches
/// how the app already handles platform gaps (<c>UnsupportedAppleSignInService</c>,
/// <c>NoBillingService</c>, <c>NoAudioVisualizerService</c>) so calling code never branches on
/// platform.
/// </remarks>
public sealed class NoPushNotificationCoordinator : IPushNotificationCoordinator
{
    public Task SyncAsync() => Task.CompletedTask;

    public Task<PushPermissionStatus> RequestPermissionAndRegisterAsync() =>
        Task.FromResult(PushPermissionStatus.Unsupported);
}

/// <summary>
/// The no-op registration service for platforms with no push transport.
/// </summary>
public sealed class NoPushRegistrationService : IPushRegistrationService
{
    public bool IsSupported => false;

    public Task<PushPermissionStatus> RequestPermissionAsync() =>
        Task.FromResult(PushPermissionStatus.Unsupported);

    public Task<PushPermissionStatus> GetPermissionStatusAsync() =>
        Task.FromResult(PushPermissionStatus.Unsupported);

    public Task<string?> GetTokenAsync() => Task.FromResult<string?>(null);

    // Never raised. Declared to satisfy the interface; the add/remove accessors keep the compiler
    // from warning about an event that is never invoked.
    public event EventHandler<string>? TokenRefreshed
    {
        add { }
        remove { }
    }
}
