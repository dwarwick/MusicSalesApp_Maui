using System.Windows.Input;

namespace MusicSalesApp.Maui.Views;

public partial class SubscriptionOfferCardView : ContentView
{
    public static readonly BindableProperty TitleTextProperty = BindableProperty.Create(
        nameof(TitleText), typeof(string), typeof(SubscriptionOfferCardView), string.Empty);

    public static readonly BindableProperty BodyTextProperty = BindableProperty.Create(
        nameof(BodyText), typeof(string), typeof(SubscriptionOfferCardView), string.Empty);

    public static readonly BindableProperty PriceTextProperty = BindableProperty.Create(
        nameof(PriceText), typeof(string), typeof(SubscriptionOfferCardView), string.Empty);

    public static readonly BindableProperty ShowPriceTextProperty = BindableProperty.Create(
        nameof(ShowPriceText), typeof(bool), typeof(SubscriptionOfferCardView), true);

    public static readonly BindableProperty DisclosureTextProperty = BindableProperty.Create(
        nameof(DisclosureText), typeof(string), typeof(SubscriptionOfferCardView), string.Empty);

    public static readonly BindableProperty PrimaryButtonTextProperty = BindableProperty.Create(
        nameof(PrimaryButtonText), typeof(string), typeof(SubscriptionOfferCardView), string.Empty);

    public static readonly BindableProperty PrimaryCommandProperty = BindableProperty.Create(
        nameof(PrimaryCommand), typeof(ICommand), typeof(SubscriptionOfferCardView));

    public static readonly BindableProperty ShowPrimaryButtonProperty = BindableProperty.Create(
        nameof(ShowPrimaryButton), typeof(bool), typeof(SubscriptionOfferCardView), true);

    public static readonly BindableProperty IsPrimaryButtonEnabledProperty = BindableProperty.Create(
        nameof(IsPrimaryButtonEnabled), typeof(bool), typeof(SubscriptionOfferCardView), true);

    public static readonly BindableProperty SecondaryButtonTextProperty = BindableProperty.Create(
        nameof(SecondaryButtonText), typeof(string), typeof(SubscriptionOfferCardView), string.Empty);

    public static readonly BindableProperty SecondaryCommandProperty = BindableProperty.Create(
        nameof(SecondaryCommand), typeof(ICommand), typeof(SubscriptionOfferCardView));

    public static readonly BindableProperty ShowSecondaryButtonProperty = BindableProperty.Create(
        nameof(ShowSecondaryButton), typeof(bool), typeof(SubscriptionOfferCardView), false);

    public SubscriptionOfferCardView()
    {
        InitializeComponent();
    }

    public string TitleText
    {
        get => (string)GetValue(TitleTextProperty);
        set => SetValue(TitleTextProperty, value);
    }

    public string BodyText
    {
        get => (string)GetValue(BodyTextProperty);
        set => SetValue(BodyTextProperty, value);
    }

    public string PriceText
    {
        get => (string)GetValue(PriceTextProperty);
        set => SetValue(PriceTextProperty, value);
    }

    public bool ShowPriceText
    {
        get => (bool)GetValue(ShowPriceTextProperty);
        set => SetValue(ShowPriceTextProperty, value);
    }

    public string DisclosureText
    {
        get => (string)GetValue(DisclosureTextProperty);
        set => SetValue(DisclosureTextProperty, value);
    }

    public string PrimaryButtonText
    {
        get => (string)GetValue(PrimaryButtonTextProperty);
        set => SetValue(PrimaryButtonTextProperty, value);
    }

    public ICommand? PrimaryCommand
    {
        get => (ICommand?)GetValue(PrimaryCommandProperty);
        set => SetValue(PrimaryCommandProperty, value);
    }

    public bool ShowPrimaryButton
    {
        get => (bool)GetValue(ShowPrimaryButtonProperty);
        set => SetValue(ShowPrimaryButtonProperty, value);
    }

    public bool IsPrimaryButtonEnabled
    {
        get => (bool)GetValue(IsPrimaryButtonEnabledProperty);
        set => SetValue(IsPrimaryButtonEnabledProperty, value);
    }

    public string SecondaryButtonText
    {
        get => (string)GetValue(SecondaryButtonTextProperty);
        set => SetValue(SecondaryButtonTextProperty, value);
    }

    public ICommand? SecondaryCommand
    {
        get => (ICommand?)GetValue(SecondaryCommandProperty);
        set => SetValue(SecondaryCommandProperty, value);
    }

    public bool ShowSecondaryButton
    {
        get => (bool)GetValue(ShowSecondaryButtonProperty);
        set => SetValue(ShowSecondaryButtonProperty, value);
    }
}
