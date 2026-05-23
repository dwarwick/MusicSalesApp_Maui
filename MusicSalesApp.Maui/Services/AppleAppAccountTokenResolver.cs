namespace MusicSalesApp.Maui.Services;

public static class AppleAppAccountTokenResolver
{
    public static string? FromStoredUserId(string? storedUserId)
    {
        if (!int.TryParse(storedUserId, out var userId) || userId <= 0)
        {
            return null;
        }

        return userId.ToString();
    }
}