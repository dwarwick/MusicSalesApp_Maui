namespace MusicSalesApp.Maui.Services;

/// <summary>
/// Shell navigation, always dispatched to the main thread.
///
/// Shell navigation touches native view controllers, and iOS throws
/// <c>UIKitThreadAccessException</c> outright when that happens off the UI thread. These calls used
/// to run on whatever thread the caller happened to be on, which is fine for a button command and
/// wrong for anything reached from a background continuation — the preview-limit subscribe prompt
/// resumes on a thread-pool thread, so navigating from it threw, the exception was swallowed by the
/// CTA handler's catch, and tapping "Sign In" appeared to do nothing at all.
///
/// Marshalled here rather than at each call site: every route in the app goes through this class,
/// and a caller cannot reasonably know which thread it was resumed on. When the caller is already on
/// the main thread the delegate runs inline, so nothing changes for an ordinary UI command.
/// </summary>
public class NavigationService : INavigationService
{
    public Task GoBackAsync()
        => MainThread.InvokeOnMainThreadAsync(() => Shell.Current.GoToAsync(".."));

    public Task GoToAsync(string route)
        => MainThread.InvokeOnMainThreadAsync(() => Shell.Current.GoToAsync(route));

    public Task GoToAsync(string route, IDictionary<string, object> parameters)
        => MainThread.InvokeOnMainThreadAsync(() => Shell.Current.GoToAsync(route, parameters));

    public Task GoToReplacingCurrentAsync(string route, IDictionary<string, object> parameters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        ArgumentNullException.ThrowIfNull(parameters);

        return MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var shell = Shell.Current ?? throw new InvalidOperationException("Shell navigation is not available.");
            var navigation = shell.Navigation;
            var currentPage = navigation.NavigationStack.Count > 0
                ? navigation.NavigationStack[^1]
                : null;

            await shell.GoToAsync(route, parameters);

            var pageToRemove = NavigationStackReplacement.FindPageToRemove(navigation.NavigationStack, currentPage);
            if (pageToRemove != null)
            {
                navigation.RemovePage(pageToRemove);
            }
        });
    }
}
