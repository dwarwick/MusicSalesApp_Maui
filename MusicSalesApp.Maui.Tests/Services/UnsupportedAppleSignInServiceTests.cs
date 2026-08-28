using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.Tests.Services;

[TestFixture]
public class UnsupportedAppleSignInServiceTests
{
    private UnsupportedAppleSignInService _service;

    [SetUp]
    public void SetUp() => _service = new UnsupportedAppleSignInService();

    [Test]
    public void IsSupported_IsFalse()
    {
        // What hides the Apple button everywhere except iOS.
        Assert.That(_service.IsSupported, Is.False);
    }

    [Test]
    public async Task AuthenticateAsync_FailsWithAMessageRatherThanThrowing()
    {
        var result = await _service.AuthenticateAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Is.Not.Empty);
            // Not a cancellation: the UI stays silent for those, and "this platform cannot do it"
            // is something the user should actually be told if they somehow reach it.
            Assert.That(result.WasCancelled, Is.False);
        });
    }
}
