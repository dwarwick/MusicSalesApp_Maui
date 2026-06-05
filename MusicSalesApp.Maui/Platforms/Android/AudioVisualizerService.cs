using Android.Media.Audiofx;
using Microsoft.Maui.ApplicationModel;
using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.Platforms.Android;

public sealed class AudioVisualizerService : IAudioVisualizerService, IDisposable
{
    private const int ReleaseDelayMilliseconds = 500;

    private readonly IPlaybackService _playbackService;
    private readonly IPlatformPlaybackRuntime _playbackRuntime;
    private readonly IMediaPlaybackOnboardingService _mediaPlaybackOnboardingService;
    private readonly AudioEqualizerBarProcessor _barProcessor = new();
    private readonly SemaphoreSlim _bindLock = new(1, 1);

    private Visualizer? _visualizer;
    private DataCaptureListener? _captureListener;
    private bool _permissionChecked;
    private bool _permissionGranted;
    private int _boundSessionId;
    private int _captureGeneration;

    public AudioVisualizerService(
        IPlaybackService playbackService,
        IPlatformPlaybackRuntime playbackRuntime,
        IMediaPlaybackOnboardingService mediaPlaybackOnboardingService)
    {
        _playbackService = playbackService;
        _playbackRuntime = playbackRuntime;
        _mediaPlaybackOnboardingService = mediaPlaybackOnboardingService;
        _playbackService.StateChanged += OnPlaybackStateChanged;
    }

    public IReadOnlyList<float> Levels { get; private set; } = [];

    public bool IsVisualizationAvailable { get; private set; }

    public event Action? VisualizationChanged;

    public async Task EnsureInitializedAsync()
    {
        if (!_playbackService.IsPlaying || _playbackService.CurrentSong == null)
        {
            return;
        }

        if (!await EnsurePermissionGrantedAsync())
        {
            return;
        }

        await _bindLock.WaitAsync();
        try
        {
            if (!_playbackService.IsPlaying || _playbackService.CurrentSong == null)
            {
                return;
            }

            var sessionId = await WaitForAudioSessionIdAsync();
            if (sessionId <= 0)
            {
                SetVisualizationAvailable(false, clearLevels: false);
                return;
            }

            if (_visualizer != null && _boundSessionId == sessionId)
            {
                SetVisualizationAvailable(true, clearLevels: false);
                return;
            }

            BindVisualizer(sessionId);
        }
        finally
        {
            _bindLock.Release();
        }
    }

    public void Dispose()
    {
        _playbackService.StateChanged -= OnPlaybackStateChanged;
        ReleaseVisualizer(clearLevels: true);
        _bindLock.Dispose();
    }

    public void Suspend()
    {
        ReleaseVisualizer(clearLevels: true);
    }

    private void OnPlaybackStateChanged(string propertyName)
    {
        if (propertyName != nameof(IPlaybackService.CurrentSong) && propertyName != nameof(IPlaybackService.IsPlaying))
        {
            return;
        }

        _ = HandlePlaybackStateChangedAsync();
    }

    private async Task HandlePlaybackStateChangedAsync()
    {
        if (!_playbackService.IsPlaying || _playbackService.CurrentSong == null)
        {
            ReleaseVisualizer(clearLevels: true);
            return;
        }

        await EnsureInitializedAsync();
    }

    private async Task<bool> EnsurePermissionGrantedAsync()
    {
        if (_permissionChecked)
        {
            return _permissionGranted;
        }

        _permissionChecked = true;
        _permissionGranted = await _mediaPlaybackOnboardingService.EnsureMicrophonePermissionAsync();

        if (!_permissionGranted)
        {
            SetVisualizationAvailable(false, clearLevels: true);
        }

        return _permissionGranted;
    }

    private async Task<int> WaitForAudioSessionIdAsync()
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            var sessionId = GetCurrentAudioSessionId();
            if (sessionId > 0)
            {
                return sessionId;
            }

            await Task.Delay(150);
        }

        return 0;
    }

    private void BindVisualizer(int sessionId)
    {
        ReleaseVisualizer(clearLevels: false);

        try
        {
            _visualizer = new Visualizer(sessionId);
            var captureGeneration = Interlocked.Increment(ref _captureGeneration);
            _captureListener = new DataCaptureListener(this, captureGeneration);

            var captureSizeRange = Visualizer.GetCaptureSizeRange();
            if (captureSizeRange == null || captureSizeRange.Length < 2)
            {
                ReleaseVisualizer(clearLevels: true);
                return;
            }

            _visualizer.SetCaptureSize(captureSizeRange[1]);
            _visualizer.SetScalingMode(VisualizerScalingMode.Normalized);
            _visualizer.SetDataCaptureListener(_captureListener, Visualizer.MaxCaptureRate / 2, false, true);
            _visualizer.SetEnabled(true);

            _boundSessionId = sessionId;
            SetVisualizationAvailable(true, clearLevels: false);
        }
        catch
        {
            ReleaseVisualizer(clearLevels: true);
        }
    }

    private void HandleFftData(byte[]? fft, int samplingRate, int captureGeneration)
    {
        if (captureGeneration != Volatile.Read(ref _captureGeneration) || _visualizer == null || _boundSessionId <= 0)
        {
            return;
        }

        // Delayed callbacks can still arrive after the Visualizer has been disabled.
        // Use the sampling rate provided by the callback instead of touching released state.
        Levels = _barProcessor.ProcessFft(fft, samplingRate);
        SetVisualizationAvailable(Levels.Count > 0, clearLevels: false, notifyWhenUnchanged: true);
    }

    private void ReleaseVisualizer(bool clearLevels)
    {
        var visualizer = _visualizer;

        Interlocked.Increment(ref _captureGeneration);

        try
        {
            if (visualizer != null)
            {
                visualizer.SetDataCaptureListener(null, 0, false, false);
                visualizer.SetEnabled(false);
                _ = ReleaseVisualizerAsync(visualizer);
            }
        }
        catch
        {
        }
        finally
        {
            _visualizer = null;
            _captureListener = null;
            _boundSessionId = 0;
        }

        SetVisualizationAvailable(false, clearLevels);
    }

    private static async Task ReleaseVisualizerAsync(Visualizer visualizer)
    {
        try
        {
            await Task.Delay(ReleaseDelayMilliseconds);
            visualizer.Release();
        }
        catch
        {
        }
        finally
        {
            visualizer.Dispose();
        }
    }

    private void SetVisualizationAvailable(bool isAvailable, bool clearLevels, bool notifyWhenUnchanged = false)
    {
        if (clearLevels)
        {
            _barProcessor.Reset();
            Levels = [];
        }

        var changed = IsVisualizationAvailable != isAvailable || clearLevels || notifyWhenUnchanged;
        IsVisualizationAvailable = isAvailable;

        if (changed)
        {
            VisualizationChanged?.Invoke();
        }
    }

    private int GetCurrentAudioSessionId()
    {
        return _playbackRuntime.AudioSessionId;
    }

    private sealed class DataCaptureListener(AudioVisualizerService owner, int captureGeneration)
        : Java.Lang.Object, Visualizer.IOnDataCaptureListener
    {
        public void OnFftDataCapture(Visualizer? visualizer, byte[]? fft, int samplingRate)
        {
            owner.HandleFftData(fft, samplingRate, captureGeneration);
        }

        public void OnWaveFormDataCapture(Visualizer? visualizer, byte[]? waveform, int samplingRate)
        {
        }
    }
}
