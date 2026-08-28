using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.Tests.Services;

[TestFixture]
public class UserStreamedSongStoreTests
{
    private FakeAppPreferenceStore _preferences = null!;
    private UserStreamedSongStore _store = null!;

    [SetUp]
    public void SetUp()
    {
        _preferences = new FakeAppPreferenceStore();
        _store = new UserStreamedSongStore(_preferences);
    }

    [Test]
    public void HasStreamed_UnknownSong_IsFalse()
    {
        Assert.That(_store.HasStreamed(42), Is.False);
    }

    [Test]
    public void MarkStreamed_MakesTheSongKnown()
    {
        Assert.That(_store.MarkStreamed(42), Is.True, "First mark is new.");
        Assert.That(_store.HasStreamed(42), Is.True);
    }

    [Test]
    public void MarkStreamed_Twice_ReportsTheSecondAsNotNew()
    {
        _store.MarkStreamed(42);

        Assert.That(_store.MarkStreamed(42), Is.False);
    }

    [Test]
    public void MarkStreamed_IgnoresAnInvalidId()
    {
        Assert.That(_store.MarkStreamed(0), Is.False);
        Assert.That(_store.HasStreamed(0), Is.False);
    }

    [Test]
    public void MarkStreamed_SurvivesANewStoreOverTheSamePreferences()
    {
        // The store is a singleton in the app, but a relaunch builds a new one over the same
        // preferences - eligibility has to come back with it.
        _store.MarkStreamed(42);

        var reloaded = new UserStreamedSongStore(_preferences);

        Assert.That(reloaded.HasStreamed(42), Is.True);
    }

    [Test]
    public void MergeFromServer_AddsWithoutDroppingLocalMarks()
    {
        // The local mark came from a stream that is still queued offline; the server list cannot know
        // about it yet and must not displace it.
        _store.MarkStreamed(1);

        _store.MergeFromServer([2, 3]);

        Assert.That(_store.GetStreamedSongIds(), Is.EquivalentTo(new[] { 1, 2, 3 }));
    }

    [Test]
    public void Clear_ForgetsEverything()
    {
        _store.MarkStreamed(42);

        _store.Clear();

        Assert.That(_store.HasStreamed(42), Is.False);
        Assert.That(new UserStreamedSongStore(_preferences).HasStreamed(42), Is.False,
            "Logout must not leave the outgoing user's history for the next account to inherit.");
    }

    [Test]
    public void Load_CorruptPayload_StartsEmptyRatherThanThrowing()
    {
        _preferences.SetString("user_streamed_songs_v1", "not json");

        var store = new UserStreamedSongStore(_preferences);

        Assert.DoesNotThrow(() => store.HasStreamed(42));
        Assert.That(store.HasStreamed(42), Is.False);
    }

    private sealed class FakeAppPreferenceStore : IAppPreferenceStore
    {
        private readonly Dictionary<string, string> _values = [];

        public bool GetBool(string key, bool defaultValue = false) => defaultValue;

        public void SetBool(string key, bool value) { }

        public int GetInt(string key, int defaultValue = 0) => defaultValue;

        public void SetInt(string key, int value) { }

        public string? GetString(string key) => _values.TryGetValue(key, out var value) ? value : null;

        public void SetString(string key, string value) => _values[key] = value;

        public void Remove(string key) => _values.Remove(key);
    }
}
