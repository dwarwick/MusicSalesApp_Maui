using System.Windows.Input;
using Microsoft.Maui.Controls.Shapes;
using MusicSalesApp.Maui.Services;
using SkiaSharp;
using SkiaSharp.Views.Maui;

namespace MusicSalesApp.Maui.Views;

public partial class EqualizerPlayButton : ContentView
{
    private const string PlayGlyph = "\u25B6";
    private const string PauseGlyph = "\u23F8";

    public static readonly BindableProperty SongIdProperty =
        BindableProperty.Create(nameof(SongId), typeof(int), typeof(EqualizerPlayButton), 0, propertyChanged: OnVisualPropertyChanged);

    public static readonly BindableProperty CommandProperty =
        BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(EqualizerPlayButton));

    public static readonly BindableProperty CommandParameterProperty =
        BindableProperty.Create(nameof(CommandParameter), typeof(object), typeof(EqualizerPlayButton));

    public static readonly BindableProperty ButtonSizeProperty =
        BindableProperty.Create(nameof(ButtonSize), typeof(double), typeof(EqualizerPlayButton), 40d, propertyChanged: OnButtonSizePropertyChanged);

    public static readonly BindableProperty AccentColorProperty =
        BindableProperty.Create(nameof(AccentColor), typeof(Color), typeof(EqualizerPlayButton), Color.FromArgb("#1DB954"), propertyChanged: OnVisualPropertyChanged);

    public static readonly BindableProperty IconColorProperty =
        BindableProperty.Create(nameof(IconColor), typeof(Color), typeof(EqualizerPlayButton), Colors.Black, propertyChanged: OnVisualPropertyChanged);

    private IPlaybackService? _playbackService;
    private IAudioVisualizerService? _audioVisualizerService;

    public EqualizerPlayButton()
    {
        InitializeComponent();

        var tapGesture = new TapGestureRecognizer();
        tapGesture.Tapped += OnTapped;
        HitTarget.GestureRecognizers.Add(tapGesture);

        UpdateSizing();
        UpdateVisualState();
    }

    public int SongId
    {
        get => (int)GetValue(SongIdProperty);
        set => SetValue(SongIdProperty, value);
    }

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    public double ButtonSize
    {
        get => (double)GetValue(ButtonSizeProperty);
        set => SetValue(ButtonSizeProperty, value);
    }

    public Color AccentColor
    {
        get => (Color)GetValue(AccentColorProperty);
        set => SetValue(AccentColorProperty, value);
    }

    public Color IconColor
    {
        get => (Color)GetValue(IconColorProperty);
        set => SetValue(IconColorProperty, value);
    }

    public IPlaybackService? PlaybackServiceOverride { get; set; }

    public IAudioVisualizerService? AudioVisualizerServiceOverride { get; set; }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        AttachServices();
    }

    protected override void OnHandlerChanging(HandlerChangingEventArgs args)
    {
        DetachServices();
        base.OnHandlerChanging(args);
    }

    private static void OnVisualPropertyChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        ((EqualizerPlayButton)bindable).UpdateVisualState();
    }

    private static void OnButtonSizePropertyChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        ((EqualizerPlayButton)bindable).UpdateSizing();
    }

    private void AttachServices()
    {
        DetachServices();
        if (Handler == null)
        {
            return;
        }

        var services = IPlatformApplication.Current?.Services;
        _playbackService = PlaybackServiceOverride ?? services?.GetService(typeof(IPlaybackService)) as IPlaybackService;
        _audioVisualizerService = AudioVisualizerServiceOverride ?? services?.GetService(typeof(IAudioVisualizerService)) as IAudioVisualizerService;

        if (_playbackService != null)
        {
            _playbackService.StateChanged += OnPlaybackStateChanged;
        }

        if (_audioVisualizerService != null)
        {
            _audioVisualizerService.VisualizationChanged += OnVisualizationChanged;
        }

        UpdateVisualState();
    }

    private void DetachServices()
    {
        if (_playbackService != null)
        {
            _playbackService.StateChanged -= OnPlaybackStateChanged;
        }

        if (_audioVisualizerService != null)
        {
            _audioVisualizerService.VisualizationChanged -= OnVisualizationChanged;
        }

        _playbackService = null;
        _audioVisualizerService = null;
    }

    private void OnPlaybackStateChanged(string propertyName)
    {
        if (propertyName != nameof(IPlaybackService.CurrentSong) && propertyName != nameof(IPlaybackService.IsPlaying))
        {
            return;
        }

        MainThread.BeginInvokeOnMainThread(UpdateVisualState);
    }

    private void OnVisualizationChanged()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            UpdateVisualState();
            VisualizerCanvas.InvalidateSurface();
        });
    }

    private void OnTapped(object? sender, TappedEventArgs e)
    {
        var parameter = CommandParameter;
        if (Command?.CanExecute(parameter) == true)
        {
            Command.Execute(parameter);
        }
    }

    private void UpdateSizing()
    {
        WidthRequest = ButtonSize;
        HeightRequest = ButtonSize;
        HitTarget.WidthRequest = ButtonSize;
        HitTarget.HeightRequest = ButtonSize;
        ButtonBorder.WidthRequest = ButtonSize;
        ButtonBorder.HeightRequest = ButtonSize;
        VisualizerCanvas.WidthRequest = ButtonSize;
        VisualizerCanvas.HeightRequest = ButtonSize;
        ButtonBorder.StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(ButtonSize / 2d) };
        IconLabel.FontSize = Math.Max(18d, ButtonSize * 0.45d);
    }

    private void UpdateVisualState()
    {
        var isCurrentSongPlaying = _playbackService?.IsPlaying == true
            && _playbackService.CurrentSong?.Id == SongId
            && SongId > 0;

        if (isCurrentSongPlaying && _audioVisualizerService != null)
        {
            _ = _audioVisualizerService.EnsureInitializedAsync();
        }

        var showVisualizer = isCurrentSongPlaying
            && _audioVisualizerService?.IsVisualizationAvailable == true
            && _audioVisualizerService.Levels.Count > 0;

        ButtonBorder.BackgroundColor = AccentColor;
        IconLabel.TextColor = IconColor;
        IconLabel.Text = isCurrentSongPlaying ? PauseGlyph : PlayGlyph;
        ButtonBorder.IsVisible = !showVisualizer;
        VisualizerCanvas.IsVisible = showVisualizer;

        if (showVisualizer)
        {
            VisualizerCanvas.InvalidateSurface();
        }
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear();

        var levels = _audioVisualizerService?.Levels;
        if (levels == null || levels.Count == 0)
        {
            return;
        }

        var info = e.Info;
        var barCount = levels.Count;
        var spacing = Math.Max(0.5f, info.Width * 0.02f);
        var availableWidth = info.Width - ((barCount - 1) * spacing);
        var barWidth = availableWidth / barCount;
        if (barWidth < 1f)
        {
            barWidth = 1f;
            spacing = Math.Max(0f, (info.Width - (barCount * barWidth)) / Math.Max(1, barCount - 1));
        }

        var totalWidth = (barCount * barWidth) + ((barCount - 1) * spacing);
        var left = (info.Width - totalWidth) / 2f;
        var bottom = info.Height * 0.86f;
        var minHeight = info.Height * 0.2f;
        var maxHeight = info.Height * 0.72f;

        using var paint = new SKPaint
        {
            Color = ToSkColor(AccentColor),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        for (var barIndex = 0; barIndex < barCount; barIndex++)
        {
            var level = Math.Clamp(levels[barIndex], 0f, 1f);
            var height = minHeight + (level * (maxHeight - minHeight));
            var rect = new SKRect(left, bottom - height, left + barWidth, bottom);
            canvas.DrawRoundRect(rect, barWidth / 2f, barWidth / 2f, paint);
            left += barWidth + spacing;
        }
    }

    private static SKColor ToSkColor(Color color)
    {
        return new SKColor(
            (byte)(color.Red * byte.MaxValue),
            (byte)(color.Green * byte.MaxValue),
            (byte)(color.Blue * byte.MaxValue),
            (byte)(color.Alpha * byte.MaxValue));
    }
}