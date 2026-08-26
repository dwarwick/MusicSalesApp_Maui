using System.Text.Json;

namespace MusicSalesApp.Maui.Services;

/// <summary>
/// Remembers which songs the signed-in user has streamed, so the thumbs can be enabled without waiting
/// on the server - including offline, where the stream that earned the right to rate is still sitting in
/// the retry queue.
/// </summary>
public interface IUserStreamedSongStore
{
    bool HasStreamed(int songMetadataId);

    /// <summary>Records a locally observed qualifying stream. Returns true if this was new.</summary>
    bool MarkStreamed(int songMetadataId);

    /// <summary>Folds in what the server reports, so streams from another device or the web count too.</summary>
    void MergeFromServer(IEnumerable<int> songMetadataIds);

    IReadOnlySet<int> GetStreamedSongIds();

    /// <summary>Drops everything. Called on logout - this is one user's listening history.</summary>
    void Clear();
}

/// <summary>
/// Preference-backed implementation.
///
/// Deliberately not modelled on <see cref="AnonymousFeaturedStreamStore"/>'s key-per-song layout: this
/// set has to be wiped wholesale when a user signs out, and scattered keys give nothing to enumerate.
/// One JSON array under a single key clears in one call and cannot leave a stray song behind for the
/// next account to inherit.
/// </summary>
public sealed class UserStreamedSongStore : IUserStreamedSongStore
{
    private const string PreferenceKey = "user_streamed_songs_v1";

    private readonly IAppPreferenceStore _preferenceStore;
    private readonly object _gate = new();
    private HashSet<int>? _cache;

    public UserStreamedSongStore(IAppPreferenceStore preferenceStore)
    {
        _preferenceStore = preferenceStore;
    }

    public bool HasStreamed(int songMetadataId)
    {
        if (songMetadataId <= 0)
        {
            return false;
        }

        lock (_gate)
        {
            return Load().Contains(songMetadataId);
        }
    }

    public bool MarkStreamed(int songMetadataId)
    {
        if (songMetadataId <= 0)
        {
            return false;
        }

        lock (_gate)
        {
            var streamed = Load();
            if (!streamed.Add(songMetadataId))
            {
                return false;
            }

            Save(streamed);
            return true;
        }
    }

    public void MergeFromServer(IEnumerable<int> songMetadataIds)
    {
        lock (_gate)
        {
            var streamed = Load();
            var changed = false;

            foreach (var songMetadataId in songMetadataIds)
            {
                if (songMetadataId > 0 && streamed.Add(songMetadataId))
                {
                    changed = true;
                }
            }

            if (changed)
            {
                Save(streamed);
            }
        }
    }

    public IReadOnlySet<int> GetStreamedSongIds()
    {
        lock (_gate)
        {
            return new HashSet<int>(Load());
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _cache = new HashSet<int>();
            _preferenceStore.Remove(PreferenceKey);
        }
    }

    private HashSet<int> Load()
    {
        if (_cache != null)
        {
            return _cache;
        }

        var stored = _preferenceStore.GetString(PreferenceKey);
        if (string.IsNullOrWhiteSpace(stored))
        {
            return _cache = new HashSet<int>();
        }

        try
        {
            var ids = JsonSerializer.Deserialize<List<int>>(stored);
            return _cache = ids == null ? new HashSet<int>() : new HashSet<int>(ids);
        }
        catch (JsonException)
        {
            // Corrupt payload. Starting empty costs at most a re-fetch from the server; refusing to
            // parse would leave the buttons permanently dead with no way back.
            return _cache = new HashSet<int>();
        }
    }

    private void Save(HashSet<int> streamed)
    {
        _cache = streamed;
        _preferenceStore.SetString(PreferenceKey, JsonSerializer.Serialize(streamed.ToList()));
    }
}
