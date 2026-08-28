namespace MusicSalesApp.Maui.Services;

/// <summary>
/// What the native Sign in with Apple sheet hands back.
/// </summary>
/// <param name="Email">
/// Supplied on the FIRST authorization only. Apple never sends it again, so it has to be
/// forwarded to the server and persisted there rather than re-read on the next sign-in.
/// </param>
/// <param name="FullName">Supplied on the first authorization only, same as <paramref name="Email"/>.</param>
public sealed record AppleSignInResult(
    string IdentityToken,
    string AuthorizationCode,
    string Email,
    string FullName,
    bool WasCancelled,
    string ErrorMessage)
{
    public bool Success => !WasCancelled
        && string.IsNullOrEmpty(ErrorMessage)
        && !string.IsNullOrWhiteSpace(IdentityToken);

    public static AppleSignInResult Cancelled() =>
        new(string.Empty, string.Empty, string.Empty, string.Empty, true, string.Empty);

    public static AppleSignInResult Failed(string errorMessage) =>
        new(string.Empty, string.Empty, string.Empty, string.Empty, false, errorMessage);
}

/// <summary>
/// Wraps the platform's native Sign in with Apple UI so <see cref="AuthService"/> stays
/// platform-neutral and testable, in the same spirit as <see cref="IWebAuthenticatorService"/>
/// and <see cref="IBiometricAuthenticator"/>.
/// </summary>
public interface IAppleSignInService
{
    /// <summary>
    /// False everywhere except iOS. The sign-in button is hidden rather than disabled when this
    /// is false, so no other platform has to render a control it cannot honour.
    /// </summary>
    bool IsSupported { get; }

    Task<AppleSignInResult> AuthenticateAsync(CancellationToken cancellationToken = default);
}
