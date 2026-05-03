using Microsoft.Maui.ApplicationModel;

namespace MusicSalesApp.Maui.Services;

public sealed class MicrophonePermissionService : IMicrophonePermissionService
{
    public Task<PermissionStatus> CheckStatusAsync()
        => MainThread.InvokeOnMainThreadAsync(() => Permissions.CheckStatusAsync<Permissions.Microphone>());

    public Task<PermissionStatus> RequestAsync()
        => MainThread.InvokeOnMainThreadAsync(() => Permissions.RequestAsync<Permissions.Microphone>());
}