namespace MusicSalesApp.Maui.Services;

public interface ITipFlowHandler
{
    bool CanShowTipButton(int? creatorId, int? creatorUserId);
    Task ShowAsync(int songMetadataId, string songTitle, int? creatorId, int? creatorUserId);
    Task<bool> HandleAppLinkAsync(Uri uri);
}