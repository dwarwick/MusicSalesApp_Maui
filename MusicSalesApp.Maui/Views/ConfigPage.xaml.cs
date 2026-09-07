using System.Globalization;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Views;

public partial class ConfigPage : ContentPage
{
    private readonly ConfigViewModel _viewModel;

    public ConfigPage(ConfigViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.Refresh();
        try
        {
            await _viewModel.RefreshCacheUsageAsync();
        }
        catch (Exception ex)
        {
            // Never let a cache-usage read failure crash the async void handler; the page
            // stays usable and the label falls back via IsCacheUsageLoading.
            System.Diagnostics.Debug.WriteLine($"Failed to refresh cache usage: {ex}");
        }

        try
        {
            await _viewModel.LoadNotificationPreferencesAsync();
        }
        catch (Exception ex)
        {
            // Same reasoning as above: this is an async void handler, and an unreachable server
            // must leave the rest of the page working rather than take the app down.
            System.Diagnostics.Debug.WriteLine($"Failed to load notification preferences: {ex}");
        }
    }
}

/// <summary>
/// Shows an <see cref="MusicSalesApp.Common.Helpers.ArtistPushFrequency"/> using the shared label,
/// so the picker and the web account page always say the same thing.
/// </summary>
public sealed class PushFrequencyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is MusicSalesApp.Common.Helpers.ArtistPushFrequency frequency
            ? MusicSalesApp.Common.Helpers.ArtistPushFrequencies.DisplayName(frequency)
            : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
