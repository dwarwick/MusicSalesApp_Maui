namespace MusicSalesApp.Maui.Services;

public interface IAudioVisualizerService
{
    IReadOnlyList<float> Levels { get; }

    bool IsVisualizationAvailable { get; }

    event Action? VisualizationChanged;

    Task EnsureInitializedAsync();
}