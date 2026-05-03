namespace MusicSalesApp.Maui.Services;

public sealed class NoAudioVisualizerService : IAudioVisualizerService
{
    private static readonly float[] EmptyLevels = [];

    public IReadOnlyList<float> Levels => EmptyLevels;

    public bool IsVisualizationAvailable => false;

    public event Action? VisualizationChanged
    {
        add { }
        remove { }
    }

    public Task EnsureInitializedAsync() => Task.CompletedTask;

    public void Suspend()
    {
    }
}