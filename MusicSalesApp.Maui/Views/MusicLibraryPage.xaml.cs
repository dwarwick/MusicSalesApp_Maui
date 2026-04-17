using System.Globalization;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Views;
using Microsoft.Extensions.Logging;
using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Views;

public partial class MusicLibraryPage : ContentPage
{
    private readonly MusicLibraryViewModel _viewModel;
    private readonly IPlaybackService _playbackService;
    private readonly ILogger<MusicLibraryPage> _logger;
    private IDispatcherTimer? _progressTimer;

    public MusicLibraryPage(MusicLibraryViewModel viewModel, IPlaybackService playbackService, ILogger<MusicLibraryPage> logger, IAuthService authService)
    {
        _viewModel = viewModel;
        _playbackService = playbackService;
        _logger = logger;
        BindingContext = viewModel;

        // Subscribe to PlaybackService events to drive MediaElement
        _playbackService.PlayRequested += OnPlayRequested;
        _playbackService.ResumeRequested += OnResumeRequested;
        _playbackService.PauseRequested += OnPauseRequested;
        _playbackService.StopRequested += OnStopRequested;
        _playbackService.SeekRequested += OnSeekRequested;
        _playbackService.StateChanged += OnPlaybackStateChanged;

        // Add converters to page resources before InitializeComponent
        Resources.Add("PlayPauseGlyphConverter", new PlayPauseGlyphConverter());
        Resources.Add("DurationConverter", new DurationConverter());
        Resources.Add("ActivePillBgConverter", new ActivePillBgConverter());
        Resources.Add("ActivePillTextConverter", new ActivePillTextConverter());
        Resources.Add("LikeGlyphConverter", new LikeGlyphConverter());
        Resources.Add("DislikeGlyphConverter", new DislikeGlyphConverter());
        Resources.Add("LikeColorConverter", new LikeColorConverter());
        Resources.Add("DislikeColorConverter", new DislikeColorConverter());
        Resources.Add("LikeFillConverter", new LikeFillConverter());
        Resources.Add("DislikeFillConverter", new DislikeFillConverter());

        InitializeComponent();

        // Initialize the reusable NowPlayingView with the playback service
        NowPlayingBar.Initialize(playbackService, authService);

        // Wire RefreshView command in code-behind to avoid MAUIG2045
        SongsRefreshView.Command = _viewModel.LoadSongsCommand;

        // Subscribe to MediaElement events
        AudioPlayer.StateChanged += OnMediaStateChanged;
        AudioPlayer.MediaOpened += OnMediaOpened;
        AudioPlayer.MediaFailed += OnMediaFailed;
        AudioPlayer.MediaEnded += OnMediaEnded;

        _logger.LogInformation("[Audio] MusicLibraryPage constructed.");
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (_viewModel.Songs.Count == 0)
        {
            await _viewModel.LoadSongsCommand.ExecuteAsync(null);
        }

        await _viewModel.LoadStreamQualifyingSecondsAsync();
        await _viewModel.StartSignalRAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // Don't stop playback when navigating away — it keeps playing in background
    }

    // --- PlaybackService event handlers (drive MediaElement) ---

    private void OnPlayRequested(SongDto song)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (!string.IsNullOrEmpty(song.StreamUrl))
            {
                AudioPlayer.Stop();
                AudioPlayer.Source = MediaSource.FromUri(song.StreamUrl);
                _logger.LogInformation("[Audio] PlayRequested: {Title}", song.SongTitle);
            }
        });
    }

    private void OnResumeRequested()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (AudioPlayer.CurrentState is MediaElementState.Paused or MediaElementState.Stopped)
            {
                AudioPlayer.Play();
                _logger.LogInformation("[Audio] Resumed.");
            }
            StartProgressTimer();
        });
    }

    private void OnPauseRequested()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            AudioPlayer.Pause();
            StopProgressTimer();
            _logger.LogInformation("[Audio] Paused.");
        });
    }

    private void OnStopRequested()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            StopProgressTimer();
            AudioPlayer.Stop();
            _logger.LogInformation("[Audio] Stopped.");
        });
    }

    private void OnSeekRequested(TimeSpan position)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            AudioPlayer.SeekTo(position);
            _logger.LogInformation("[Audio] Seeked to {Position}", position);
        });
    }

    private void OnPlaybackStateChanged(string propertyName)
    {
        // NowPlayingView handles its own state updates via StateChanged subscription
    }

    // --- Progress timer ---

    private void StartProgressTimer()
    {
        if (_progressTimer != null) return;

        _progressTimer = Dispatcher.CreateTimer();
        _progressTimer.Interval = TimeSpan.FromMilliseconds(500);
        _progressTimer.Tick += OnProgressTimerTick;
        _progressTimer.Start();
    }

    private void StopProgressTimer()
    {
        if (_progressTimer == null) return;

        _progressTimer.Stop();
        _progressTimer.Tick -= OnProgressTimerTick;
        _progressTimer = null;
    }

    private void OnProgressTimerTick(object? sender, EventArgs e)
    {
        _playbackService.UpdatePosition(AudioPlayer.Position, AudioPlayer.Duration);
    }

    // --- MediaElement event handlers ---

    private void OnMediaStateChanged(object? sender, MediaStateChangedEventArgs e)
    {
        _logger.LogInformation("[Audio] StateChanged: {Previous} -> {New}", e.PreviousState, e.NewState);

        if (e.NewState == MediaElementState.Playing && _playbackService.IsPlaying)
        {
            StartProgressTimer();
        }
    }

    private void OnMediaOpened(object? sender, EventArgs e)
    {
        _logger.LogInformation("[Audio] MediaOpened! Duration={Duration}", AudioPlayer.Duration);
        _playbackService.UpdatePosition(AudioPlayer.Position, AudioPlayer.Duration);
    }

    private void OnMediaFailed(object? sender, MediaFailedEventArgs e)
    {
        _logger.LogError("[Audio] MediaFailed! Error: {Error}", e.ErrorMessage);
    }

    private void OnMediaEnded(object? sender, EventArgs e)
    {
        _logger.LogInformation("[Audio] MediaEnded.");
        StopProgressTimer();
        _playbackService.OnMediaEnded();
    }
}

/// <summary>
/// Converts bool IsPlaying to the appropriate play/pause Unicode glyph.
/// </summary>
public class PlayPauseGlyphConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? "\u23F8" : "\u25B6"; // ⏸ or ▶
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// Converts a nullable double (seconds) to a formatted duration string (m:ss or h:mm:ss).
/// </summary>
public class DurationConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double seconds && !double.IsNaN(seconds) && !double.IsInfinity(seconds) && seconds > 0)
        {
            var ts = TimeSpan.FromSeconds(seconds);
            return ts.TotalHours >= 1
                ? ts.ToString(@"h\:mm\:ss")
                : ts.ToString(@"m\:ss");
        }
        return "--:--";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// Converts bool (isActive) to pill background color: green when active, gray when inactive.
/// </summary>
public class ActivePillBgConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true)
            return Color.FromArgb("#1DB954"); // Primary green

        // Use theme-appropriate gray
        return Application.Current?.RequestedTheme == AppTheme.Dark
            ? Color.FromArgb("#404040")   // Gray600
            : Color.FromArgb("#C8C8C8");  // Gray200
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// Converts bool (isActive) to pill text color: white when active, gray when inactive.
/// </summary>
public class ActivePillTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true)
            return Colors.White;

        return Application.Current?.RequestedTheme == AppTheme.Dark
            ? Color.FromArgb("#ACACAC")  // Gray300
            : Color.FromArgb("#404040"); // Gray600
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
