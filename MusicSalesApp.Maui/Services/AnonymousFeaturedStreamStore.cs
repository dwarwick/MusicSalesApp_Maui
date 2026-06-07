namespace MusicSalesApp.Maui.Services;

public interface IAnonymousFeaturedStreamStore
{
    bool HasRecordedFeaturedStream(int songMetadataId);

    void MarkFeaturedStreamRecorded(int songMetadataId);
}

public sealed class AnonymousFeaturedStreamStore(IAppPreferenceStore preferenceStore) : IAnonymousFeaturedStreamStore
{
    private const string PreferenceKeyPrefix = "anonymous_featured_stream_recorded_";

    public bool HasRecordedFeaturedStream(int songMetadataId)
    {
        return songMetadataId > 0 &&
               preferenceStore.GetBool(GetPreferenceKey(songMetadataId));
    }

    public void MarkFeaturedStreamRecorded(int songMetadataId)
    {
        if (songMetadataId <= 0)
        {
            return;
        }

        preferenceStore.SetBool(GetPreferenceKey(songMetadataId), true);
    }

    private static string GetPreferenceKey(int songMetadataId)
    {
        return $"{PreferenceKeyPrefix}{songMetadataId}";
    }
}
