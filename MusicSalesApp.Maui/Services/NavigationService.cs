namespace MusicSalesApp.Maui.Services;

public class NavigationService : INavigationService
{
    public Task GoBackAsync()
        => Shell.Current.GoToAsync("..");

    public Task GoToAsync(string route)
        => Shell.Current.GoToAsync(route);

    public Task GoToAsync(string route, IDictionary<string, object> parameters)
        => Shell.Current.GoToAsync(route, parameters);

    public async Task GoToReplacingCurrentAsync(string route, IDictionary<string, object> parameters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        ArgumentNullException.ThrowIfNull(parameters);

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
    }
}
