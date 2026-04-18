using CommunityToolkit.Mvvm.ComponentModel;
using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.ViewModels;

[QueryProperty(nameof(PolicyTitle), "title")]
[QueryProperty(nameof(PolicyPath), "path")]
public partial class PolicyViewModel : ObservableObject
{
    private readonly IAppConfig _appConfig;

    [ObservableProperty]
    public partial string PolicyTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PolicyPath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PolicyUrl { get; set; } = string.Empty;

    public PolicyViewModel(IAppConfig appConfig)
    {
        _appConfig = appConfig;
    }

    partial void OnPolicyPathChanged(string value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            PolicyUrl = $"{_appConfig.WebBaseUrl}{value}";
        }
    }
}
