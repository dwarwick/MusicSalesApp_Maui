using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.Tests.Services;

[TestFixture]
public class AppleAppAccountTokenResolverTests
{
    [TestCase("22", "22")]
    [TestCase("0007", "7")]
    public void FromStoredUserId_ReturnsNormalizedToken_WhenUserIdIsValid(string storedUserId, string expected)
    {
        var result = AppleAppAccountTokenResolver.FromStoredUserId(storedUserId);

        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("0")]
    [TestCase("-5")]
    [TestCase("not-a-number")]
    public void FromStoredUserId_ReturnsNull_WhenUserIdIsInvalid(string? storedUserId)
    {
        var result = AppleAppAccountTokenResolver.FromStoredUserId(storedUserId);

        Assert.That(result, Is.Null);
    }
}