using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.Tests.Services;

/// <summary>
/// The fallback registered on Windows, Mac Catalyst, and anywhere else without an implementation.
/// Worth pinning because it has to keep giving the exact answer <c>AuthService</c> used to hard-code
/// in its <c>#else</c> branch - that string reaches the user's screen.
/// </summary>
[TestFixture]
public class UnsupportedBiometricAuthenticatorTests
{
    private UnsupportedBiometricAuthenticator _authenticator = null!;

    [SetUp]
    public void SetUp() => _authenticator = new UnsupportedBiometricAuthenticator();

    [Test]
    public async Task GetAvailabilityAsync_ReportsNothingAvailable()
    {
        var availability = await _authenticator.GetAvailabilityAsync();

        Assert.Multiple(() =>
        {
            Assert.That(availability.IsAvailable, Is.False);
            Assert.That(availability.Method, Is.EqualTo(BiometricMethod.None));
        });
    }

    [Test]
    public async Task AuthenticateAsync_FailsWithTheSameWordingAsBefore()
    {
        var (success, error) = await _authenticator.AuthenticateAsync();

        Assert.Multiple(() =>
        {
            Assert.That(success, Is.False);
            Assert.That(error, Is.EqualTo("Biometric authentication is not supported on this platform."));
        });
    }

    [Test]
    public void UnavailableNamesTheFeatureGenerically()
    {
        // Used in copy built before the device has answered, so it must not say "fingerprint".
        Assert.Multiple(() =>
        {
            Assert.That(BiometricAvailability.Unavailable.IsAvailable, Is.False);
            Assert.That(BiometricAvailability.Unavailable.DisplayName, Is.EqualTo("biometrics"));
        });
    }
}
