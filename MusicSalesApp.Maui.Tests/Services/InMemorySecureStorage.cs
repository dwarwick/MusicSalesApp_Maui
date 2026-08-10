using Microsoft.Maui.Storage;

namespace MusicSalesApp.Maui.Tests.Services;

/// <summary>
/// A working key/value store standing in for <see cref="ISecureStorage"/>, so a test can assert what
/// is *stored* rather than which methods were called.
///
/// The distinction matters for the credential-lifetime rules. A <c>Verify(Remove(key))</c> assertion
/// passes whether or not anything was actually removed, and says nothing about a later write putting
/// the value back — which is exactly the failure mode of pairing one account's email with another's
/// password. Asserting over the surviving contents catches both, and mirrors how these paths were
/// verified on a device: by diffing the encrypted shared_prefs entries before and after.
///
/// It models MAUI's contract, not Android's keystore. It cannot prove androidx actually persisted or
/// erased anything — only a device can — but it does prove this code leaves the right instructions.
/// </summary>
public sealed class InMemorySecureStorage : ISecureStorage
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

    /// <summary>Keys whose access throws, standing in for a damaged keystore.</summary>
    public HashSet<string> FailingKeys { get; } = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> Keys => _values.Keys.ToList();

    public bool Contains(string key) => _values.ContainsKey(key);

    public string? Peek(string key) => _values.TryGetValue(key, out var value) ? value : null;

    /// <summary>Seeds a value without going through the failure injection, for arranging state.</summary>
    public void Seed(string key, string value) => _values[key] = value;

    public Task<string?> GetAsync(string key)
    {
        Throw(key);
        return Task.FromResult(_values.TryGetValue(key, out var value) ? value : null);
    }

    public Task SetAsync(string key, string value)
    {
        Throw(key);
        _values[key] = value;
        return Task.CompletedTask;
    }

    public bool Remove(string key)
    {
        Throw(key);
        return _values.Remove(key);
    }

    public void RemoveAll() => _values.Clear();

    private void Throw(string key)
    {
        if (FailingKeys.Contains(key))
        {
            throw new InvalidOperationException($"keystore unavailable for '{key}'");
        }
    }
}
