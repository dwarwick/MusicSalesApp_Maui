namespace MusicSalesApp.Maui.Views;

public partial class SubscriptionInfoUnavailableBanner : ContentView
{
    /// <summary>
    /// The banner now covers two situations that need different wording: subscription information
    /// that may merely be out of date while offline, and entitlement that could not be confirmed at
    /// all, which pauses the user's features. The default keeps the original copy, so any usage that
    /// does not set it behaves exactly as before.
    /// </summary>
    public static readonly BindableProperty TextProperty = BindableProperty.Create(
        nameof(Text),
        typeof(string),
        typeof(SubscriptionInfoUnavailableBanner),
        "Subscription information is unavailable while you’re offline. Connect to the internet to refresh it.");

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public SubscriptionInfoUnavailableBanner()
    {
        InitializeComponent();
    }
}
