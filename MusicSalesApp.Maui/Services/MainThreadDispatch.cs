namespace MusicSalesApp.Maui.Services;

/// <summary>
/// Runs an action on the UI thread, from code that may be on any thread.
/// </summary>
/// <remarks>
/// The publisher marshals, not the subscriber. That is the convention every other event in this app
/// already follows - <c>AuthService.AuthStateChanged</c>, <c>NetworkStatusService</c>,
/// <c>PlaybackService</c>'s property notifications and <c>SongArtworkHydrator</c> - and the reason
/// is that a subscriber which forgets is not obviously wrong at the call site: it sets an
/// <c>[ObservableProperty]</c> like any other, and only crashes once that property happens to be
/// bound to something a platform renders. Deciding once, here, is what makes the next surface safe
/// without its author having to know.
///
/// <para>
/// The catch is what lets this type stay compiled into the test project. Unit tests run against the
/// reference assembly, where every <c>MainThread</c> member throws; there is no UI thread to reach
/// and running inline is exactly right.
/// </para>
/// </remarks>
public static class MainThreadDispatch
{
    /// <summary>
    /// Invokes <paramref name="action"/> on the UI thread, or inline when already on it.
    /// </summary>
    public static void Run(Action action)
    {
        if (action is null)
        {
            return;
        }

        try
        {
            if (MainThread.IsMainThread)
            {
                action();
                return;
            }

            MainThread.BeginInvokeOnMainThread(action);
        }
        catch (Exception ex) when (
            ex is NotImplementedException
            || ex.GetType().Name == "NotImplementedInReferenceAssemblyException")
        {
            action();
        }
    }
}
