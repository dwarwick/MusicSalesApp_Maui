namespace MusicSalesApp.Maui.Services;

/// <summary>
/// Stands in on every platform without a native Apple sign-in sheet. Mirrors
/// <see cref="UnsupportedBiometricAuthenticator"/>: answer "not supported" rather than throw, so
/// callers can simply not offer the option.
/// </summary>
public sealed class UnsupportedAppleSignInService : IAppleSignInService
{
    public bool IsSupported => false;

    public Task<AppleSignInResult> AuthenticateAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(AppleSignInResult.Failed("Sign in with Apple is not available on this platform."));
}
