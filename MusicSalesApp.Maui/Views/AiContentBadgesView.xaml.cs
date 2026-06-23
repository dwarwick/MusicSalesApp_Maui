namespace MusicSalesApp.Maui.Views;

public partial class AiContentBadgesView : ContentView
{
    public static readonly BindableProperty IsAiMusicProperty = BindableProperty.Create(
        nameof(IsAiMusic),
        typeof(bool),
        typeof(AiContentBadgesView),
        false,
        propertyChanged: OnBadgePropertyChanged);

    public static readonly BindableProperty IsAiVocalsProperty = BindableProperty.Create(
        nameof(IsAiVocals),
        typeof(bool),
        typeof(AiContentBadgesView),
        false,
        propertyChanged: OnBadgePropertyChanged);

    public static readonly BindableProperty IsAiLyricsProperty = BindableProperty.Create(
        nameof(IsAiLyrics),
        typeof(bool),
        typeof(AiContentBadgesView),
        false,
        propertyChanged: OnBadgePropertyChanged);

    public static readonly BindableProperty BadgeSizeProperty = BindableProperty.Create(
        nameof(BadgeSize),
        typeof(double),
        typeof(AiContentBadgesView),
        28d);

    public AiContentBadgesView()
    {
        InitializeComponent();
    }

    public bool IsAiMusic
    {
        get => (bool)GetValue(IsAiMusicProperty);
        set => SetValue(IsAiMusicProperty, value);
    }

    public bool IsAiVocals
    {
        get => (bool)GetValue(IsAiVocalsProperty);
        set => SetValue(IsAiVocalsProperty, value);
    }

    public bool IsAiLyrics
    {
        get => (bool)GetValue(IsAiLyricsProperty);
        set => SetValue(IsAiLyricsProperty, value);
    }

    public double BadgeSize
    {
        get => (double)GetValue(BadgeSizeProperty);
        set => SetValue(BadgeSizeProperty, value);
    }

    public bool HasAnyBadge => IsAiMusic || IsAiVocals || IsAiLyrics;

    private static void OnBadgePropertyChanged(BindableObject bindable, object oldValue, object newValue)
    {
        ((AiContentBadgesView)bindable).OnPropertyChanged(nameof(HasAnyBadge));
    }
}
