using Microsoft.Maui.ApplicationModel;

namespace MusicSalesApp.Maui.Services;

public sealed class MediaPlaybackOnboardingService : IMediaPlaybackOnboardingService
{
    private const string MicrophoneExplainerSuppressedKey = "MediaPlayback.MicrophoneExplainerSuppressed";

    private static readonly PermissionExplainerRequest MicrophoneRequest = new(
        "Equalizer visualization",
        "Enable microphone access for the equalizer",
        "The equalizer animation needs Android microphone access to read playback levels for the visualization. StreamTunes does not record or store your voice.",
        "Continue",
        "Not Now",
        ShowDoNotAskAgainOption: true);

    private readonly IAppPreferenceStore _preferenceStore;
    private readonly IPermissionExplainerService _permissionExplainerService;
    private readonly IMicrophonePermissionService _microphonePermissionService;

    public MediaPlaybackOnboardingService(
        IAppPreferenceStore preferenceStore,
        IPermissionExplainerService permissionExplainerService,
        IMicrophonePermissionService microphonePermissionService)
    {
        _preferenceStore = preferenceStore;
        _permissionExplainerService = permissionExplainerService;
        _microphonePermissionService = microphonePermissionService;
    }

    public async Task EnsureBackgroundPlaybackExplainedAsync()
    {
        await Task.CompletedTask;
    }

    public async Task<bool> EnsureMicrophonePermissionAsync()
    {
        var status = await _microphonePermissionService.CheckStatusAsync();
        if (status == PermissionStatus.Granted)
        {
            return true;
        }

        if (_preferenceStore.GetBool(MicrophoneExplainerSuppressedKey))
        {
            return false;
        }

        var explainerResult = await _permissionExplainerService.ShowAsync(MicrophoneRequest);
        if (!explainerResult.Accepted)
        {
            PersistDoNotAskAgainChoice(explainerResult);
            return false;
        }

        status = await _microphonePermissionService.RequestAsync();
        var granted = status == PermissionStatus.Granted;
        if (!granted)
        {
            PersistDoNotAskAgainChoice(explainerResult);
        }

        return granted;
    }

    private void PersistDoNotAskAgainChoice(PermissionExplainerResult explainerResult)
    {
        if (explainerResult.DoNotAskAgain)
        {
            _preferenceStore.SetBool(MicrophoneExplainerSuppressedKey, true);
        }
    }
}