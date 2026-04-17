namespace MusicSalesApp.Maui.Views;

public partial class ShareButton : ContentView
{
    public static readonly BindableProperty ShareTitleProperty =
        BindableProperty.Create(nameof(ShareTitle), typeof(string), typeof(ShareButton));

    public static readonly BindableProperty ShareUrlProperty =
        BindableProperty.Create(nameof(ShareUrl), typeof(string), typeof(ShareButton));

    public string ShareTitle
    {
        get => (string)GetValue(ShareTitleProperty);
        set => SetValue(ShareTitleProperty, value);
    }

    public string ShareUrl
    {
        get => (string)GetValue(ShareUrlProperty);
        set => SetValue(ShareUrlProperty, value);
    }

    public ShareButton()
    {
        InitializeComponent();
        var tapGesture = new TapGestureRecognizer();
        tapGesture.Tapped += OnShareClicked;
        ShareBtn.GestureRecognizers.Add(tapGesture);
    }

    private async void OnShareClicked(object? sender, TappedEventArgs e)
    {
        if (string.IsNullOrEmpty(ShareUrl)) return;

        await Share.Default.RequestAsync(new ShareTextRequest
        {
            Title = ShareTitle ?? "Check out this song!",
            Uri = ShareUrl
        });
    }
}
