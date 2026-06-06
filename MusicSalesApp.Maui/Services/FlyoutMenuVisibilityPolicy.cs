namespace MusicSalesApp.Maui.Services;

public static class FlyoutMenuVisibilityPolicy
{
    public static bool ShouldShowUploadYourOwnMusic(bool isLoggedIn, bool isCreator)
        => !(isLoggedIn && isCreator);
}
