namespace MusicSalesApp.Maui.Services;

/// <summary>
/// Interface for the SignalR hub service that receives real-time updates from the server.
/// </summary>
public interface ISignalRService : IAsyncDisposable
{
    /// <summary>
    /// Fired when a song's stream count is updated.
    /// Parameters: songMetadataId, newCount
    /// </summary>
    event Action<int, int>? OnStreamCountUpdated;

    /// <summary>
    /// Fired when a song's like/dislike counts are updated.
    /// Parameters: songMetadataId, likeCount, dislikeCount
    /// </summary>
    event Action<int, int, int>? OnLikeCountUpdated;

    /// <summary>
    /// Starts all SignalR hub connections.
    /// </summary>
    Task StartAsync();

    /// <summary>
    /// Returns true if all hub connections are established.
    /// </summary>
    bool IsConnected { get; }
}
