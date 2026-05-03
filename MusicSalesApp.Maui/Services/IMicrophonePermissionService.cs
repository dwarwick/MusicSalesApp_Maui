using Microsoft.Maui.ApplicationModel;

namespace MusicSalesApp.Maui.Services;

public interface IMicrophonePermissionService
{
    Task<PermissionStatus> CheckStatusAsync();

    Task<PermissionStatus> RequestAsync();
}