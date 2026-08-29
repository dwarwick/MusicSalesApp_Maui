namespace MusicSalesApp.Maui.Services;

/// <summary>
/// Whether the song lists follow the playing song on their own.
/// </summary>
/// <remarks>
/// Surfaced as the "Auto-scroll" checkbox in the navigation bar rather than on the Config page,
/// because it is a browsing preference the listener flips while looking at a list - not something
/// set once and forgotten. The bar shows it only on the two pages it governs.
/// </remarks>
public interface IAutoScrollSettingsService
{
    /// <summary>
    /// Defaults to true: following the playing song is the behaviour a listener expects, and the
    /// setting exists mainly so it can be turned OFF while browsing a long catalogue.
    /// </summary>
    bool ScrollAutomatically { get; set; }

    /// <summary>
    /// Raised only on an actual change. A list that is on screen when this turns on scrolls to the
    /// playing song straight away rather than waiting for the queue to advance.
    /// </summary>
    event Action? Changed;
}

public sealed class AutoScrollSettingsService : IAutoScrollSettingsService
{
    private const bool DefaultScrollAutomatically = true;

    private readonly IAppPreferenceStore _preferenceStore;

    public AutoScrollSettingsService(IAppPreferenceStore preferenceStore)
    {
        _preferenceStore = preferenceStore;
    }

    public event Action? Changed;

    public bool ScrollAutomatically
    {
        get => _preferenceStore.GetBool(
            MobilePreferenceKeys.AutoScrollToPlayingSong,
            DefaultScrollAutomatically);
        set
        {
            // Guarded so a no-op write - the checkbox re-asserting the value it already had, which
            // a two-way binding does on every rebuild of the title view - does not make every list
            // on screen jump to the playing song.
            if (value == ScrollAutomatically)
            {
                return;
            }

            _preferenceStore.SetBool(MobilePreferenceKeys.AutoScrollToPlayingSong, value);
            Changed?.Invoke();
        }
    }
}
