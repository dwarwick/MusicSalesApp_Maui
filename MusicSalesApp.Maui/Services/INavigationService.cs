namespace MusicSalesApp.Maui.Services;

public interface INavigationService
{
    Task GoBackAsync();
    Task GoToAsync(string route);
    Task GoToAsync(string route, IDictionary<string, object> parameters);
    Task GoToReplacingCurrentAsync(string route, IDictionary<string, object> parameters);
}
