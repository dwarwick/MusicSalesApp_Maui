using System.Windows.Input;
using MusicSalesApp.Maui.Services;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using MusicSalesApp.Maui.Resources.Styles;

namespace MusicSalesApp.Maui.Views;

public partial class EqualizerPlayButton : ContentView
{
    private const double FallbackAnimationStep = 0.35d;

    public static readonly BindableProperty SongIdProperty =
        BindableProperty.Create(nameof(SongId), typeof(int), typeof(EqualizerPlayButton), 0, propertyChanged: OnVisualPropertyChanged);

    public static readonly BindableProperty CommandProperty =
        BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(EqualizerPlayButton));

    public static readonly BindableProperty CommandParameterProperty =
        BindableProperty.Create(nameof(CommandParameter), typeof(object), typeof(EqualizerPlayButton));

    public static readonly BindableProperty ButtonSizeProperty =
        BindableProperty.Create(nameof(ButtonSize), typeof(double), typeof(EqualizerPlayButton), 40d, propertyChanged: OnButtonSizePropertyChanged);

    public static readonly BindableProperty AccentColorProperty =
        BindableProperty.Create(nameof(AccentColor), typeof(Color), typeof(EqualizerPlayButton), AppColors.BlueBright, propertyChanged: OnVisualPropertyChanged);

    public static readonly BindableProperty IconColorProperty =
        BindableProperty.Create(nameof(IconColor), typeof(Color), typeof(EqualizerPlayButton), Colors.Black, propertyChanged: OnVisualPropertyChanged);

    private IPlaybackService? _playbackService;
    private IAudioVisualizerService? _audioVisualizerService;
    private IDispatcherTimer? _fallbackAnimationTimer;
    private double _fallbackAnimationPhase;
    private readonly CoalescedUiUpdateScheduler _visualUpdateScheduler;

    public EqualizerPlayButton()
    {
        InitializeComponent();
        _visualUpdateScheduler = new CoalescedUiUpdateScheduler(
            action => MainThread.BeginInvokeOnMainThread(action),
            _ => ApplyCoalescedVisualUpdate());

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
        StopFallbackAnimation();

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

        _visualUpdateScheduler.Request(1);
    }

    private void OnVisualizationChanged()
    {
        if (!IsCurrentSongPlaying())
        {
            return;
        }

        _visualUpdateScheduler.Request(1);
    }

    private void ApplyCoalescedVisualUpdate()
    {
        if (_playbackService == null)
        {
            return;
        }

        UpdateVisualState();
    }

    private bool IsCurrentSongPlaying()
    {
        return _playbackService?.IsPlaying == true &&
               PlaybackIndicatorStateResolver.ShouldToggleCurrentSong(SongId, _playbackService.CurrentSong);
    }

    private void OnTapped(object? sender, TappedEventArgs e)
    {
        if (_playbackService != null && PlaybackIndicatorStateResolver.ShouldToggleCurrentSong(SongId, _playbackService.CurrentSong))
        {
            _playbackService.TogglePlayPause();
            return;
        }

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
        VisualizerCanvas.WidthRequest = ButtonSize;
        VisualizerCanvas.HeightRequest = ButtonSize;
        VisualizerCanvas.InvalidateSurface();
    }

    private void UpdateVisualState()
    {
        var isCurrentSongPlaying = _playbackService?.IsPlaying == true
            && PlaybackIndicatorStateResolver.ShouldToggleCurrentSong(SongId, _playbackService.CurrentSong);

        if (isCurrentSongPlaying && _audioVisualizerService != null)
        {
            _ = _audioVisualizerService.EnsureInitializedAsync();
        }

        var hasLiveLevels = PlaybackIndicatorStateResolver.HasLiveLevels(_audioVisualizerService?.Levels);
        if (isCurrentSongPlaying && !hasLiveLevels)
        {
            EnsureFallbackAnimation();
        }
        else
        {
            StopFallbackAnimation();
        }

        VisualizerCanvas.InvalidateSurface();
    }

    private void EnsureFallbackAnimation()
    {
        if (_fallbackAnimationTimer != null)
        {
            if (!_fallbackAnimationTimer.IsRunning)
            {
                _fallbackAnimationTimer.Start();
            }

            return;
        }

        var dispatcher = Dispatcher ?? Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            return;
        }

        _fallbackAnimationTimer = dispatcher.CreateTimer();
        _fallbackAnimationTimer.Interval = TimeSpan.FromMilliseconds(120);
        _fallbackAnimationTimer.Tick += OnFallbackAnimationTick;
        _fallbackAnimationTimer.Start();
    }

    private void StopFallbackAnimation()
    {
        if (_fallbackAnimationTimer == null)
        {
            return;
        }

        _fallbackAnimationTimer.Stop();
        _fallbackAnimationTimer.Tick -= OnFallbackAnimationTick;
        _fallbackAnimationTimer = null;
        _fallbackAnimationPhase = 0d;
    }

    private void OnFallbackAnimationTick(object? sender, EventArgs e)
    {
        _fallbackAnimationPhase += FallbackAnimationStep;
        VisualizerCanvas.InvalidateSurface();
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear();

        var info = e.Info;
        var bounds = new SKRect(0, 0, info.Width, info.Height);
        var diameter = Math.Min(info.Width, info.Height);
        var radius = diameter / 2f;
        var center = new SKPoint(info.Width / 2f, info.Height / 2f);

        using var backgroundPaint = new SKPaint
        {
            Color = ToSkColor(AccentColor),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        canvas.DrawCircle(center, radius, backgroundPaint);

        var isCurrentSongPlaying = _playbackService?.IsPlaying == true
            && _playbackService.CurrentSong?.Id == SongId
            && SongId > 0;

        var visualState = PlaybackIndicatorStateResolver.Resolve(isCurrentSongPlaying);
        if (visualState == PlaybackIndicatorVisualState.PlayIcon)
        {
            DrawPlayIcon(canvas, bounds);
            return;
        }

        DrawEqualizer(canvas, bounds, PlaybackIndicatorStateResolver.ResolveLevels(_audioVisualizerService?.Levels, _fallbackAnimationPhase));
    }

    private void DrawPlayIcon(SKCanvas canvas, SKRect bounds)
    {
        var width = bounds.Width;
        var height = bounds.Height;
        var triangleWidth = width * 0.28f;
        var triangleHeight = height * 0.32f;
        var centerX = bounds.MidX + (width * 0.04f);
        var centerY = bounds.MidY;

        using var iconPaint = new SKPaint
        {
            Color = ToSkColor(IconColor),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        using var path = new SKPath();
        path.MoveTo(centerX - (triangleWidth / 2f), centerY - (triangleHeight / 2f));
        path.LineTo(centerX - (triangleWidth / 2f), centerY + (triangleHeight / 2f));
        path.LineTo(centerX + (triangleWidth / 2f), centerY);
        path.Close();
        canvas.DrawPath(path, iconPaint);
    }

    private void DrawEqualizer(SKCanvas canvas, SKRect bounds, IReadOnlyList<float> levels)
    {
        var barCount = levels.Count;
        var spacing = Math.Max(0.5f, bounds.Width * 0.02f);
        var availableWidth = bounds.Width * 0.46f - ((barCount - 1) * spacing);
        var barWidth = availableWidth / barCount;
        if (barWidth < 1f)
        {
            barWidth = 1f;
            spacing = Math.Max(0f, ((bounds.Width * 0.46f) - (barCount * barWidth)) / Math.Max(1, barCount - 1));
        }

        var totalWidth = (barCount * barWidth) + ((barCount - 1) * spacing);
        var left = bounds.MidX - (totalWidth / 2f);
        var bottom = bounds.Height * 0.72f;
        var minHeight = bounds.Height * 0.16f;
        var maxHeight = bounds.Height * 0.42f;

        using var paint = new SKPaint
        {
            Color = ToSkColor(IconColor),
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
