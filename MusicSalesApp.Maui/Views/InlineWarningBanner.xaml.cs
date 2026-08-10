namespace MusicSalesApp.Maui.Views;

/// <summary>
/// A one-line warning strip: a ⚠ glyph, wrapped text, and the shared offline-banner theme colours.
///
/// Was <c>SubscriptionInfoUnavailableBanner</c>, and was renamed once a second caller appeared that
/// has nothing to do with subscriptions — the session-expiry notice on Home. The old name also baked
/// an <c>AutomationId</c> into the control's own root, so every additional caller had to shadow it
/// from the parent XAML and rely on property-assignment ordering beating InitializeComponent. Callers
/// set their own <c>AutomationId</c> now, and there is no default to fight.
/// </summary>
public partial class InlineWarningBanner : ContentView
{
    public static readonly BindableProperty TextProperty = BindableProperty.Create(
        nameof(Text),
        typeof(string),
        typeof(InlineWarningBanner),
        string.Empty);

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public InlineWarningBanner()
    {
        InitializeComponent();
    }
}
