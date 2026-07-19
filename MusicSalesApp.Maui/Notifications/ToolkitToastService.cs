using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.Notifications;

public sealed class ToolkitToastService : IToastService
{
    public Task ShowAsync(string message, CancellationToken cancellationToken = default) =>
        MainThread.InvokeOnMainThreadAsync(() => Toast.Make(message, ToastDuration.Long).Show(cancellationToken));
}
