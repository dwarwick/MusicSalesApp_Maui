using System.Reflection;
using System.Text;
using System.Xml.Linq;

namespace MusicSalesApp.Maui.Tests.Views;

/// <summary>
/// Checks every <c>{Binding}</c> path in the app's XAML against the ViewModel named by the enclosing
/// <c>x:DataType</c>.
///
/// The Views are not compiled into this project — they need the platform heads — so every ViewModel
/// is covered by tests while the markup that consumes them is not. A renamed or mistyped binding path
/// therefore fails silently at runtime, binding to nothing, with the whole suite still green. That is
/// exactly the class of mistake only a device run has been catching.
///
/// Validating the markup as text closes most of it. What it cannot see: bindings whose source is an
/// element or a template item with no declared type, and whether the page is wired to the ViewModel
/// the test assumes. Those are reported as skips rather than passes, and
/// <see cref="EveryBindingPathResolves"/> fails if the skip rate suggests the parser, rather than the
/// markup, is what changed.
/// </summary>
[TestFixture]
public class XamlBindingContractTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2009/xaml";

    private static string XamlDirectory => Path.Combine(AppContext.BaseDirectory, "AppXaml");

    private static IEnumerable<string> XamlFiles =>
        Directory.EnumerateFiles(XamlDirectory, "*.xaml").OrderBy(f => f, StringComparer.Ordinal);

    private sealed record BindingSite(string File, string Element, string Attribute, string Path, Type DataType);

    [Test]
    public void TheAppsXamlIsAvailableToTest()
    {
        // Guards the csproj copy: if the markup stops reaching the output directory, every test below
        // trivially passes over an empty set.
        Assert.That(Directory.Exists(XamlDirectory), Is.True, $"no XAML copied to {XamlDirectory}");
        Assert.That(XamlFiles.Count(), Is.GreaterThan(10), "far fewer XAML files than the app has");
    }

    [Test]
    public void EveryBindingPathResolves()
    {
        var checkedSites = new List<BindingSite>();
        var broken = new List<string>();
        var skipped = 0;

        foreach (var file in XamlFiles)
        {
            var document = XDocument.Load(file);
            var name = Path.GetFileName(file);

            foreach (var element in document.Descendants())
            {
                foreach (var attribute in element.Attributes())
                {
                    foreach (var expression in ExtractBindings(attribute.Value))
                    {
                        var target = ResolveTargetType(element, expression);
                        var path = ResolvePath(expression);

                        if (target is null || path is null)
                        {
                            skipped++;
                            continue;
                        }

                        var site = new BindingSite(name, element.Name.LocalName, attribute.Name.LocalName, path, target);
                        checkedSites.Add(site);

                        if (!HasMember(target, path.Split('.')[0]))
                        {
                            broken.Add(
                                $"{name}: <{site.Element} {site.Attribute}=\"{{Binding {path}}}\"> " +
                                $"— {target.Name} has no member '{path.Split('.')[0]}'");
                        }
                    }
                }
            }
        }

        // A parser regression that resolved nothing would otherwise report a clean bill of health.
        Assert.That(checkedSites, Has.Count.GreaterThan(100),
            $"only {checkedSites.Count} bindings resolved ({skipped} skipped) — the parser, not the markup, likely changed");

        Assert.That(broken, Is.Empty, string.Join(Environment.NewLine, broken));
    }

    /// <summary>
    /// The wiring this work added, asserted by name. <see cref="EveryBindingPathResolves"/> proves a
    /// binding points at something real; these prove the right thing is bound at all, which is the
    /// other half — deleting the element entirely would leave that test perfectly happy.
    /// </summary>
    [TestCase("HomePage.xaml", "ShowSessionExpiredNotice")]
    [TestCase("HomePage.xaml", "SessionExpiredMessage")]
    [TestCase("HomePage.xaml", "ShowSubscriptionUnavailableBanner")]
    [TestCase("HomePage.xaml", "SubscriptionUnavailableBannerText")]
    [TestCase("AccountSettingsPage.xaml", "ShowSubscriptionUnavailableBanner")]
    [TestCase("AccountSettingsPage.xaml", "IsBiometricLoginSupported")]
    [TestCase("AccountSettingsPage.xaml", "IsBiometricLoginEnabled")]
    [TestCase("AccountSettingsPage.xaml", "BiometricLoginStatusText")]
    [TestCase("AccountSettingsPage.xaml", "TurnOffBiometricLoginCommand")]
    public void PageBindsTo(string file, string path)
    {
        var markup = File.ReadAllText(Path.Combine(XamlDirectory, file));

        Assert.That(markup, Does.Contain($"{{Binding {path}}}"),
            $"{file} no longer binds {path}");
    }

    [Test]
    public void TheSessionExpiryNoticeAndSubscriptionBannerAreSeparateElements()
    {
        // They carry different messages for different states and must not be collapsed into one.
        var markup = File.ReadAllText(Path.Combine(XamlDirectory, "HomePage.xaml"));

        Assert.That(CountOccurrences(markup, "views:InlineWarningBanner"), Is.EqualTo(2));
    }

    [Test]
    public void NoXamlStillReferencesTheRenamedBannerControl()
    {
        foreach (var file in XamlFiles)
        {
            Assert.That(File.ReadAllText(file), Does.Not.Contain("views:SubscriptionInfoUnavailableBanner"),
                $"{Path.GetFileName(file)} references the pre-rename control type");
        }
    }

    // --- Parsing ---

    /// <summary>
    /// Pulls each <c>{Binding …}</c> out of an attribute value, matching braces so a nested markup
    /// extension such as <c>StringFormat='{}{0}'</c> or an inner <c>{x:Type}</c> does not truncate it.
    /// </summary>
    private static IEnumerable<string> ExtractBindings(string value)
    {
        const string opener = "{Binding";
        var index = value.IndexOf(opener, StringComparison.Ordinal);

        while (index >= 0)
        {
            var depth = 0;
            var end = -1;

            for (var i = index; i < value.Length; i++)
            {
                if (value[i] == '{')
                {
                    depth++;
                }
                else if (value[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        end = i;
                        break;
                    }
                }
            }

            if (end < 0)
            {
                yield break;
            }

            yield return value.Substring(index + opener.Length, end - index - opener.Length).Trim();
            index = value.IndexOf(opener, end, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The type a binding resolves against: the ancestor <c>x:DataType</c>, or the type named by an
    /// explicit <c>RelativeSource AncestorType</c>. Null when the source is an element reference or a
    /// type this project does not compile, both of which are unverifiable rather than wrong.
    /// </summary>
    private static Type? ResolveTargetType(XElement element, string expression)
    {
        var ancestorType = Match(expression, "AncestorType={x:Type ", '}');
        if (ancestorType is not null)
        {
            return ResolveXamlType(element, ancestorType);
        }

        if (expression.Contains("Source=", StringComparison.Ordinal))
        {
            return null;
        }

        for (var current = element; current is not null; current = current.Parent)
        {
            var declared = current.Attribute(Xaml + "DataType")?.Value;
            if (declared is not null)
            {
                return ResolveXamlType(current, declared.Replace("{x:Type ", string.Empty).TrimEnd('}').Trim());
            }
        }

        return null;
    }

    /// <summary>The property path, or null when the binding targets the context itself.</summary>
    private static string? ResolvePath(string expression)
    {
        var first = SplitTopLevel(expression).FirstOrDefault()?.Trim() ?? string.Empty;

        if (first.StartsWith("Path=", StringComparison.Ordinal))
        {
            first = first["Path=".Length..].Trim();
        }
        else if (first.Contains('=', StringComparison.Ordinal))
        {
            // A named parameter such as Converter= in first position means there is no explicit path.
            return null;
        }

        return first is "" or "." ? null : first;
    }

    private static IEnumerable<string> SplitTopLevel(string expression)
    {
        var depth = 0;
        var segment = new StringBuilder();

        foreach (var c in expression)
        {
            switch (c)
            {
                case '{': depth++; break;
                case '}': depth--; break;
                case ',' when depth == 0:
                    yield return segment.ToString();
                    segment.Clear();
                    continue;
            }

            segment.Append(c);
        }

        if (segment.Length > 0)
        {
            yield return segment.ToString();
        }
    }

    /// <summary>Maps a prefixed XAML type name onto a CLR type via the document's xmlns declarations.</summary>
    private static Type? ResolveXamlType(XElement scope, string prefixedName)
    {
        var parts = prefixedName.Split(':', 2);
        if (parts.Length != 2)
        {
            return null;
        }

        var namespaceUri = scope.GetNamespaceOfPrefix(parts[0])?.NamespaceName;
        if (namespaceUri is null || !namespaceUri.StartsWith("clr-namespace:", StringComparison.Ordinal))
        {
            return null;
        }

        var clrNamespace = namespaceUri["clr-namespace:".Length..].Split(';')[0];

        // Only types this project compiles are resolvable; Views are not, and are skipped.
        return Type.GetType($"{clrNamespace}.{parts[1]}");
    }

    /// <summary>The text between <paramref name="marker"/> and the next <paramref name="terminator"/>.</summary>
    private static string? Match(string source, string marker, char terminator)
    {
        var start = source.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        start += marker.Length;
        var end = source.IndexOf(terminator, start);

        return end < 0 ? null : source[start..end].Trim();
    }

    private static bool HasMember(Type type, string name)
        => type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy) is not null
           || type.GetField(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy) is not null;

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = haystack.IndexOf(needle, StringComparison.Ordinal);

        while (index >= 0)
        {
            count++;
            index = haystack.IndexOf(needle, index + needle.Length, StringComparison.Ordinal);
        }

        return count;
    }
}
