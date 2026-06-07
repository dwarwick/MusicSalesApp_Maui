using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.Tests.Services;

[TestFixture]
public class AnonymousFeaturedStreamStoreTests
{
    private InMemoryPreferenceStore _preferenceStore = null!;
    private AnonymousFeaturedStreamStore _store = null!;

    [SetUp]
    public void SetUp()
    {
        _preferenceStore = new InMemoryPreferenceStore();
        _store = new AnonymousFeaturedStreamStore(_preferenceStore);
    }

    [Test]
    public void HasRecordedFeaturedStream_WhenSongWasNotMarked_ReturnsFalse()
    {
        Assert.That(_store.HasRecordedFeaturedStream(42), Is.False);
    }

    [Test]
    public void MarkFeaturedStreamRecorded_WhenSongIsMarked_PersistsRecordedState()
    {
        _store.MarkFeaturedStreamRecorded(42);

        Assert.That(_store.HasRecordedFeaturedStream(42), Is.True);
    }

    [Test]
    public void MarkFeaturedStreamRecorded_WithInvalidSongId_DoesNotPersistAnything()
    {
        _store.MarkFeaturedStreamRecorded(0);

        Assert.That(_preferenceStore.Values, Is.Empty);
    }

    private sealed class InMemoryPreferenceStore : IAppPreferenceStore
    {
        public Dictionary<string, object> Values { get; } = [];

        public bool GetBool(string key, bool defaultValue = false)
        {
            return Values.TryGetValue(key, out var value) && value is bool boolValue
                ? boolValue
                : defaultValue;
        }

        public void SetBool(string key, bool value)
        {
            Values[key] = value;
        }

        public int GetInt(string key, int defaultValue = 0)
        {
            return Values.TryGetValue(key, out var value) && value is int intValue
                ? intValue
                : defaultValue;
        }

        public void SetInt(string key, int value)
        {
            Values[key] = value;
        }

        public string? GetString(string key)
        {
            return Values.TryGetValue(key, out var value) ? value as string : null;
        }

        public void SetString(string key, string value)
        {
            Values[key] = value;
        }

        public void Remove(string key)
        {
            Values.Remove(key);
        }
    }
}
