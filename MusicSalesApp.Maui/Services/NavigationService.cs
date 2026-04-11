namespace MusicSalesApp.Maui.Services;

public class NavigationService : INavigationService
{
    public Task GoToAsync(string route)
        => Shell.Current.GoToAsync(route);

    public Task GoToAsync(string route, IDictionary<string, object> parameters)
        => Shell.Current.GoToAsync(route, parameters);
}
