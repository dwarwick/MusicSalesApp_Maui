using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.Tests.Services;

/// <summary>
/// Turning whatever a creator typed into their website field into something safe to open.
/// </summary>
/// <remarks>
/// The server stores this with nothing but a Trim - no scheme added, nothing validated - and the
/// result reaches the device's launcher, so the parsing has to be deliberate rather than hopeful.
/// </remarks>
[TestFixture]
public class WebsiteUriTests
{
    [TestCase("https://example.com", "https://example.com/")]
    [TestCase("http://example.com", "http://example.com/")]
    [TestCase("https://example.com/artist/page", "https://example.com/artist/page")]
    public void KeepsAWellFormedWebAddress(string input, string expected)
    {
        Assert.That(WebsiteUri.TryParse(input, out var uri), Is.True);
        Assert.That(uri!.ToString(), Is.EqualTo(expected));
    }

    [TestCase("example.com")]
    [TestCase("www.example.com")]
    [TestCase("  example.com  ")]
    public void AssumesHttps_WhenNoSchemeWasTyped(string input)
    {
        // The common case by a distance: creators type a domain, not a URL.
        Assert.That(WebsiteUri.TryParse(input, out var uri), Is.True);
        Assert.That(uri!.Scheme, Is.EqualTo(Uri.UriSchemeHttps));
    }

    /// <summary>
    /// Anything that is not plain web browsing is refused.
    /// </summary>
    /// <remarks>
    /// Checked against an allow-list rather than merely parsed. These all parse perfectly well as
    /// absolute URIs, which is exactly why parsing alone is not the test - the value comes from a
    /// free-text field and ends up at the device launcher.
    /// </remarks>
    [TestCase("javascript:alert(1)")]
    [TestCase("file:///etc/passwd")]
    [TestCase("ftp://example.com")]
    [TestCase("mailto:someone@example.com")]
    [TestCase("tel:+15551234")]
    public void RefusesAnythingThatIsNotHttpOrHttps(string input)
    {
        Assert.That(WebsiteUri.TryParse(input, out var uri), Is.False);
        Assert.That(uri, Is.Null);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void RefusesNothingAtAll(string? input)
    {
        Assert.That(WebsiteUri.TryParse(input, out _), Is.False);
    }
}
