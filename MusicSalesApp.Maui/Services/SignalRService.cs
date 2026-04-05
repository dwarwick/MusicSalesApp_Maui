using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MusicSalesApp.Maui.Services;

/// <summary>
/// SignalR client service for the MAUI app. Connects to the server's stream-count
/// and like-count hubs to receive real-time updates.
/// </summary>
public class SignalRService : ISignalRService
{
    private readonly HubConnection _streamCountHub;
    private readonly HubConnection _likeCountHub;
    private readonly ILogger<SignalRService> _logger;
    private bool _isStarted;

    // Must match SignalRMethodNames on the server
    private const string ReceiveStreamCountUpdate = "ReceiveStreamCountUpdate";
    private const string ReceiveLikeCountUpdate = "ReceiveLikeCountUpdate";

    public event Action<int, int>? OnStreamCountUpdated;
    public event Action<int, int, int>? OnLikeCountUpdated;

    public bool IsConnected =>
        _streamCountHub.State == HubConnectionState.Connected &&
        _likeCountHub.State == HubConnectionState.Connected;

    public SignalRService(IConfiguration configuration, ILogger<SignalRService> logger)
    {
        _logger = logger;

        var baseUrl = GetHubBaseUrl(configuration);

        _streamCountHub = BuildHub($"{baseUrl}/streamcounthub");
        _likeCountHub = BuildHub($"{baseUrl}/likecounthub");

        _streamCountHub.On<int, int>(ReceiveStreamCountUpdate, (songMetadataId, newCount) =>
        {
            _logger.LogDebug("SignalR: Stream count update for song {Id}: {Count}", songMetadataId, newCount);
            OnStreamCountUpdated?.Invoke(songMetadataId, newCount);
        });

        _likeCountHub.On<int, int, int>(ReceiveLikeCountUpdate, (songMetadataId, likeCount, dislikeCount) =>
        {
            _logger.LogDebug("SignalR: Like count update for song {Id}: {Likes}/{Dislikes}", songMetadataId, likeCount, dislikeCount);
            OnLikeCountUpdated?.Invoke(songMetadataId, likeCount, dislikeCount);
        });
    }

    public async Task StartAsync()
    {
        if (_isStarted) return;
        _isStarted = true;

        await StartHubAsync(_streamCountHub, "StreamCount");
        await StartHubAsync(_likeCountHub, "LikeCount");
    }

    private async Task StartHubAsync(HubConnection hub, string name)
    {
        if (hub.State != HubConnectionState.Disconnected) return;

        try
        {
            await hub.StartAsync();
            _logger.LogInformation("SignalR: {Hub} hub connected", name);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SignalR: {Hub} hub connection failed (non-fatal)", name);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _streamCountHub.DisposeAsync();
        await _likeCountHub.DisposeAsync();
    }

    private static HubConnection BuildHub(string url)
    {
        var builder = new HubConnectionBuilder()
            .WithUrl(url, options =>
            {
#if DEBUG
                // Accept any certificate in debug builds (dev tunnels, ngrok, etc.)
                options.HttpMessageHandlerFactory = _ => new SocketsHttpHandler
                {
                    SslOptions = new System.Net.Security.SslClientAuthenticationOptions
                    {
                        RemoteCertificateValidationCallback = (_, _, _, _) => true
                    }
                };
#endif
            })
            .WithAutomaticReconnect();

        var hub = builder.Build();

        // Reduce keepalive traffic — default is 15s per hub; with 2 hubs that doubles.
        hub.KeepAliveInterval = TimeSpan.FromSeconds(60);
        hub.ServerTimeout = TimeSpan.FromSeconds(120);

        return hub;
    }

    private static string GetHubBaseUrl(IConfiguration configuration)
    {
        var apiBaseUrl = configuration["ApiBaseUrl"] ?? "https://localhost:7173";

#if ANDROID && DEBUG
        // Android can't reach the host's "localhost" directly.
        // Emulator: 10.0.2.2 routes to the host PC.
        // Physical device via USB: use "adb reverse tcp:7173 tcp:7173" then localhost works.
        if (apiBaseUrl.Contains("localhost"))
        {
            var isEmulator = Android.OS.Build.Hardware == "ranchu" || Android.OS.Build.Hardware == "goldfish";
            if (isEmulator)
            {
                apiBaseUrl = apiBaseUrl.Replace("localhost", "10.0.2.2");
            }
        }
#endif

        return apiBaseUrl.TrimEnd('/');
    }
}
