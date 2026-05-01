using Android.Media.Audiofx;
using MediaManager;
using Microsoft.Maui.ApplicationModel;
using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.Platforms.Android;

public sealed class AudioVisualizerService : IAudioVisualizerService, IDisposable
{
    private readonly IPlaybackService _playbackService;
    private readonly AudioEqualizerBarProcessor _barProcessor = new();
    private readonly SemaphoreSlim _bindLock = new(1, 1);

    private Visualizer? _visualizer;
    private DataCaptureListener? _captureListener;
    private bool _permissionChecked;
    private bool _permissionGranted;
    private int _boundSessionId;

    public AudioVisualizerService(IPlaybackService playbackService)
    {
        _playbackService = playbackService;
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

        var status = await MainThread.InvokeOnMainThreadAsync(() => Permissions.CheckStatusAsync<Permissions.Microphone>());
        if (status != PermissionStatus.Granted)
        {
            status = await MainThread.InvokeOnMainThreadAsync(() => Permissions.RequestAsync<Permissions.Microphone>());
        }

        _permissionChecked = true;
        _permissionGranted = status == PermissionStatus.Granted;

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
            _captureListener = new DataCaptureListener(this);

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

    private void HandleFftData(byte[]? fft)
    {
        var samplingRate = _visualizer?.SamplingRate ?? 0;
        Levels = _barProcessor.ProcessFft(fft, samplingRate);
        SetVisualizationAvailable(Levels.Count > 0, clearLevels: false, notifyWhenUnchanged: true);
    }

    private void ReleaseVisualizer(bool clearLevels)
    {
        try
        {
            if (_visualizer != null)
            {
                _visualizer.SetEnabled(false);
                _visualizer.SetDataCaptureListener(null, 0, false, false);
                _visualizer.Release();
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

    private static int GetCurrentAudioSessionId()
    {
        return CrossMediaManager.Android?.Player?.AudioSessionId ?? 0;
    }

    private sealed class DataCaptureListener(AudioVisualizerService owner)
        : Java.Lang.Object, Visualizer.IOnDataCaptureListener
    {
        public void OnFftDataCapture(Visualizer? visualizer, byte[]? fft, int samplingRate)
        {
            owner.HandleFftData(fft);
        }

        public void OnWaveFormDataCapture(Visualizer? visualizer, byte[]? waveform, int samplingRate)
        {
        }
    }
}