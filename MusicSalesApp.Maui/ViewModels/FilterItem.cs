using CommunityToolkit.Mvvm.ComponentModel;

namespace MusicSalesApp.Maui.ViewModels;

public partial class FilterItem : ObservableObject
{
    public string Name { get; set; } = string.Empty;
    public int Count { get; set; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
