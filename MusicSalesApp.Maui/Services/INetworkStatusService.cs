using System.ComponentModel;

namespace MusicSalesApp.Maui.Services;

public interface INetworkStatusService : INotifyPropertyChanged
{
    bool IsOffline { get; }
}
