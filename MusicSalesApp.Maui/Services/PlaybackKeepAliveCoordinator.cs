namespace MusicSalesApp.Maui.Services;

public sealed class PlaybackKeepAliveCoordinator : IDisposable
{
    private readonly Action _activate;
    private readonly Action _deactivate;
    private bool _isDisposed;
    private bool _isPlaybackActive;

    public PlaybackKeepAliveCoordinator(Action activate, Action deactivate)
    {
        _activate = activate ?? throw new ArgumentNullException(nameof(activate));
        _deactivate = deactivate ?? throw new ArgumentNullException(nameof(deactivate));
    }

    public void SetPlaybackActive(bool isActive)
    {
        if (_isDisposed || _isPlaybackActive == isActive)
        {
            return;
        }

        _isPlaybackActive = isActive;

        if (isActive)
        {
            _activate();
            return;
        }

        _deactivate();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        if (!_isPlaybackActive)
        {
            return;
        }

        _isPlaybackActive = false;
        _deactivate();
    }
}