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
    private readonly HubConnection _adminMessageHub;
    private readonly SignalRConnectionStarter _connectionStarter = new();
    private readonly IAuthService _authService;
    private readonly ILogger<SignalRService> _logger;

    // Must match SignalRMethodNames on the server
    private const string ReceiveStreamCountUpdate = "ReceiveStreamCountUpdate";
    private const string ReceiveLikeCountUpdate = "ReceiveLikeCountUpdate";
    private const string ReceiveAdminMessageRefresh = "ReceiveAdminMessageRefresh";

    public event Action<int, int>? OnStreamCountUpdated;
    public event Action<int, int, int>? OnLikeCountUpdated;
    public event Action? OnAdminMessagesUpdated;

    public bool IsConnected =>
        _streamCountHub.State == HubConnectionState.Connected &&
        _likeCountHub.State == HubConnectionState.Connected;

    public SignalRService(IConfiguration configuration, IAuthService authService, ILogger<SignalRService> logger)
    {
        _authService = authService;
        _logger = logger;

        var baseUrl = GetHubBaseUrl(configuration);

        _streamCountHub = BuildHub($"{baseUrl}/streamcounthub");
        _likeCountHub = BuildHub($"{baseUrl}/likecounthub");
        _adminMessageHub = BuildHub($"{baseUrl}/adminmessagehub", () => Task.FromResult(_authService.Token));

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

        _adminMessageHub.On(ReceiveAdminMessageRefresh, () =>
        {
            _logger.LogDebug("SignalR: Admin message refresh requested");
            OnAdminMessagesUpdated?.Invoke();
        });

        _adminMessageHub.Reconnected += connectionId =>
        {
            _logger.LogInformation("SignalR: AdminMessage hub reconnected with connection ID {ConnectionId}", connectionId);
            OnAdminMessagesUpdated?.Invoke();
            return Task.CompletedTask;
        };
    }

    public Task StartAsync()
    {
        var targets = new List<SignalRStartTarget>
        {
            new(
                "StreamCount",
                () => _streamCountHub.State == HubConnectionState.Disconnected,
                () => _streamCountHub.StartAsync()),
            new(
                "LikeCount",
                () => _likeCountHub.State == HubConnectionState.Disconnected,
                () => _likeCountHub.StartAsync())
        };

        if (_authService.IsLoggedIn && !string.IsNullOrWhiteSpace(_authService.Token))
        {
            targets.Add(new SignalRStartTarget(
                "AdminMessage",
                () => _adminMessageHub.State == HubConnectionState.Disconnected,
                () => _adminMessageHub.StartAsync()));
        }

        return _connectionStarter.StartAsync(
            targets,
            name => _logger.LogInformation("SignalR: {Hub} hub connected", name),
            (name, ex) => _logger.LogWarning(ex, "SignalR: {Hub} hub connection failed (non-fatal)", name));
    }

    public async Task SyncAdminMessageConnectionAsync()
    {
        if (_authService.IsLoggedIn && !string.IsNullOrWhiteSpace(_authService.Token))
        {
            await _connectionStarter.StartAsync(
                [new SignalRStartTarget(
                    "AdminMessage",
                    () => _adminMessageHub.State == HubConnectionState.Disconnected,
                    () => _adminMessageHub.StartAsync())],
                name => _logger.LogInformation("SignalR: {Hub} hub connected", name),
                (name, ex) => _logger.LogWarning(ex, "SignalR: {Hub} hub connection failed (non-fatal)", name));
            return;
        }

        if (_adminMessageHub.State != HubConnectionState.Disconnected)
        {
            try
            {
                await _adminMessageHub.StopAsync();
                _logger.LogInformation("SignalR: AdminMessage hub disconnected after logout");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SignalR: Failed to stop AdminMessage hub after logout");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _streamCountHub.DisposeAsync();
        await _likeCountHub.DisposeAsync();
        await _adminMessageHub.DisposeAsync();
    }

    private static HubConnection BuildHub(string url, Func<Task<string?>>? accessTokenProvider = null)
    {
        var builder = new HubConnectionBuilder()
            .WithUrl(url, options =>
            {
                if (accessTokenProvider != null)
                {
                    options.AccessTokenProvider = accessTokenProvider;
                }

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
