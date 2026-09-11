using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.Tests.Services;

/// <summary>
/// An <see cref="IAppPreferenceStore"/> that keeps its values in memory and round-trips all three
/// types.
/// </summary>
/// <remarks>
/// Shared, because there were four of these and the copies had drifted: two stubbed
/// <c>GetBool</c>/<c>GetInt</c> to return the default and made <c>SetBool</c>/<c>SetInt</c> do
/// nothing, so a test that stored a bool through one of them would have passed against a fake that
/// silently discarded it.
///
/// <para>
/// The namespace is deliberately the one the service tests already live in, so they reach it
/// without a using. <c>AnonymousFeaturedStreamStoreTests</c> keeps its own on purpose: it exposes
/// the backing dictionary for a test to assert on, which is a different job from standing in for
/// the store.
/// </para>
/// </remarks>
internal sealed class InMemoryPreferenceStore : IAppPreferenceStore
{
    private readonly Dictionary<string, string> _values = [];

    public bool GetBool(string key, bool defaultValue = false)
        => bool.TryParse(GetString(key), out var value) ? value : defaultValue;

    public void SetBool(string key, bool value)
        => SetString(key, value.ToString());

    public int GetInt(string key, int defaultValue = 0)
        => int.TryParse(GetString(key), out var value) ? value : defaultValue;

    public void SetInt(string key, int value)
        => SetString(key, value.ToString());

    public string? GetString(string key)
        => _values.TryGetValue(key, out var value) ? value : null;

    public void SetString(string key, string value)
        => _values[key] = value;

    public void Remove(string key)
        => _values.Remove(key);
}
