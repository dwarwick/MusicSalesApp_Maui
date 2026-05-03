namespace MusicSalesApp.Maui.Services;

public sealed class NoOpMediaPlaybackOnboardingService : IMediaPlaybackOnboardingService
{
    public Task EnsureBackgroundPlaybackExplainedAsync() => Task.CompletedTask;

    public Task<bool> EnsureMicrophonePermissionAsync() => Task.FromResult(true);
}