namespace MusicSalesApp.Maui.Services;

public sealed class PermissionExplainerService : IPermissionExplainerService
{
    public Task<PermissionExplainerResult> ShowAsync(PermissionExplainerRequest request)
    {
        return MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (Application.Current?.Windows.FirstOrDefault()?.Page is not Page page)
            {
                return new PermissionExplainerResult(false, false);
            }

            var explainerPage = new PermissionExplainerPage(request);
            await page.Navigation.PushModalAsync(explainerPage, false);
            return await explainerPage.WaitForResultAsync();
        });
    }
}