namespace MusicSalesApp.Maui.Services;

public interface IPermissionExplainerService
{
    Task<PermissionExplainerResult> ShowAsync(PermissionExplainerRequest request);
}