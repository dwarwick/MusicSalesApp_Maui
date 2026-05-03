namespace MusicSalesApp.Maui.Services;

public interface IMediaPlaybackOnboardingService
{
    Task EnsureBackgroundPlaybackExplainedAsync();

    Task<bool> EnsureMicrophonePermissionAsync();
}