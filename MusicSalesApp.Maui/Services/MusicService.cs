using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Networking;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Services;

public class MusicService : IMusicService
{
    private const string SongsRequestPath = "api/music/songs";
    private const string PendingStreamRecordsPreferenceKey = "pending_stream_records_v1";
    private const int MaxPendingStreamRecords = 1000;
    private static readonly TimeSpan DefaultPendingStreamRetryInterval = TimeSpan.FromSeconds(15);
    // With HttpCompletionOption.ResponseHeadersRead, HttpClient.Timeout only covers the headers,
    // not the streamed body/deserialize. This explicit timeout guards the whole operation so a
    // stalled body read can't hang the songs load forever when the caller passes no token.
    private static readonly TimeSpan DefaultSongsRequestTimeout = TimeSpan.FromSeconds(100);
    private static readonly JsonSerializerOptions SongsSerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions PendingStreamRecordsSerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IAppSettingsService _appSettingsService;
    private readonly IAppPreferenceStore _appPreferenceStore;
    private readonly IConnectivity _connectivity;
    private readonly ILogger<MusicService> _logger;
    private readonly SemaphoreSlim _pendingStreamFlushLock = new(1, 1);
    private readonly TimeSpan _pendingStreamRetryInterval;
    private readonly TimeSpan _songsRequestTimeout;
    private readonly object _pendingStreamRetryLoopLock = new();
    private Task? _pendingStreamRetryLoopTask;

    public event Action<int, int>? OnStreamCountRecorded;

    public string? LastSongsError { get; private set; }

    public MusicService(
        IHttpClientFactory httpClientFactory,
        IAppSettingsService appSettingsService,
        IAppPreferenceStore appPreferenceStore,
        IConnectivity connectivity,
        ILogger<MusicService> logger,
        TimeSpan? pendingStreamRetryInterval = null,
        TimeSpan? songsRequestTimeout = null)
    {
        _httpClientFactory = httpClientFactory;
        _appSettingsService = appSettingsService;
        _appPreferenceStore = appPreferenceStore;
        _connectivity = connectivity;
        _logger = logger;
        _pendingStreamRetryInterval = pendingStreamRetryInterval ?? DefaultPendingStreamRetryInterval;
        _songsRequestTimeout = songsRequestTimeout ?? DefaultSongsRequestTimeout;

        _connectivity.ConnectivityChanged += OnConnectivityChanged;

        _ = InitializePendingStreamRetryLoopAsync();
    }

    public Task<List<SongDto>> GetSongsAsync()
        => GetSongsAsync(CancellationToken.None);

    public async Task<List<SongDto>> GetSongsAsync(CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("MusicSalesApi");
        LastSongsError = null;

        // Linked timeout covering the entire request — headers, body stream, and deserialize —
        // because ResponseHeadersRead leaves the body read outside HttpClient.Timeout.
        using var timeoutCts = new CancellationTokenSource(_songsRequestTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        var requestToken = linkedCts.Token;

        try
        {
            using var response = await client.GetAsync(
                    SongsRequestPath,
                    HttpCompletionOption.ResponseHeadersRead,
                    requestToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content
                    .ReadAsStringAsync(requestToken)
                    .ConfigureAwait(false);
                LastSongsError = ApiErrorMessageFormatter.FormatRequestFailure(
                    client.BaseAddress,
                    SongsRequestPath,
                    response.StatusCode,
                    responseBody);

                _logger.LogWarning(
                    "Failed to fetch songs from {RequestUri}: {StatusCode} {ResponseBody}",
                    new Uri(client.BaseAddress ?? new Uri("https://localhost/"), SongsRequestPath),
                    response.StatusCode,
                    responseBody);

                return [];
            }

            try
            {
                await using var responseStream = await response.Content
                    .ReadAsStreamAsync(requestToken)
                    .ConfigureAwait(false);

                // Even an in-memory/cached response must not deserialize a large catalog on the UI thread.
                return await Task.Run(
                        async () => await JsonSerializer.DeserializeAsync<List<SongDto>>(
                                responseStream,
                                SongsSerializerOptions,
                                requestToken)
                            .ConfigureAwait(false) ?? [],
                        requestToken)
                    .ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                LastSongsError = ApiErrorMessageFormatter.FormatException(client.BaseAddress, SongsRequestPath, ex);
                _logger.LogError(
                    ex,
                    "Failed to deserialize songs response from {RequestUri}",
                    new Uri(client.BaseAddress ?? new Uri("https://localhost/"), SongsRequestPath));
                return [];
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LastSongsError = ApiErrorMessageFormatter.FormatException(client.BaseAddress, SongsRequestPath, ex);
            _logger.LogError(ex, "Failed to fetch songs from API");
            return [];
        }
    }

    public async Task<SongDto?> GetSongByTitleAsync(string title)
    {
        var client = _httpClientFactory.CreateClient("MusicSalesApi");
        try
        {
            var encoded = Uri.EscapeDataString(title);
            return await client.GetFromJsonAsync<SongDto>($"api/music/song-by-title/{encoded}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch song by title '{Title}'", title);
            return null;
        }
    }

    public Task<int> GetStreamQualifyingSecondsAsync()
    {
        return _appSettingsService.GetStreamQualifyingSecondsAsync();
    }

    public async Task<int?> RecordStreamAsync(int songMetadataId)
    {
        var attempt = await SendStreamRecordAsync(songMetadataId).ConfigureAwait(false);
        if (attempt.StreamCount.HasValue)
        {
            TriggerPendingStreamFlush("a stream was recorded successfully");
            return attempt.StreamCount;
        }

        if (attempt.ShouldQueueForRetry)
        {
            await QueuePendingStreamRecordAsync(songMetadataId).ConfigureAwait(false);
            EnsurePendingStreamRetryLoopStarted();

            _logger.LogInformation(
                _connectivity.NetworkAccess == NetworkAccess.Internet
                    ? "Queued stream record for song {SongMetadataId} after a transient request failure"
                    : "Queued stream record for song {SongMetadataId} because connectivity currently reports no internet access",
                songMetadataId);
        }

        return null;
    }

    public async Task FlushPendingStreamRecordsAsync()
    {
        await _pendingStreamFlushLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var pendingStreamRecords = await LoadPendingStreamRecordsAsync().ConfigureAwait(false);
            for (var index = 0; index < pendingStreamRecords.Count; index++)
            {
                var pendingStreamRecord = pendingStreamRecords[index];
                var attempt = await SendStreamRecordAsync(pendingStreamRecord.SongMetadataId).ConfigureAwait(false);

                if (attempt.StreamCount.HasValue)
                {
                    pendingStreamRecords.RemoveAt(index);
                    index--;
                    await SavePendingStreamRecordsAsync(pendingStreamRecords).ConfigureAwait(false);
                    continue;
                }

                if (attempt.ShouldDropPendingRecord)
                {
                    _logger.LogWarning(
                        "Dropping pending stream record for song {SongMetadataId} after a non-retryable response",
                        pendingStreamRecord.SongMetadataId);
                    pendingStreamRecords.RemoveAt(index);
                    index--;
                    await SavePendingStreamRecordsAsync(pendingStreamRecords).ConfigureAwait(false);
                    continue;
                }

                break;
            }

            if (pendingStreamRecords.Count > 0)
            {
                EnsurePendingStreamRetryLoopStarted();
            }
        }
        finally
        {
            _pendingStreamFlushLock.Release();
        }
    }

    public Task ClearPendingStreamRecordsAsync() =>
        Task.Run(() => _appPreferenceStore.Remove(PendingStreamRecordsPreferenceKey));

    public async Task<List<LikeCountDto>> GetBulkLikeCountsAsync(IEnumerable<int> songIds)
    {
        var client = _httpClientFactory.CreateClient("MusicSalesApi");
        try
        {
            var ids = string.Join(",", songIds);
            if (string.IsNullOrEmpty(ids)) return [];

            var result = await client.GetFromJsonAsync<List<LikeCountDto>>($"api/music/likes/bulk?ids={ids}");
            return result ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch bulk like counts");
            return [];
        }
    }

    public async Task<LikeToggleResult?> ToggleLikeAsync(int songMetadataId)
    {
        var client = _httpClientFactory.CreateClient("MusicSalesApi");
        try
        {
            var response = await client.PostAsync($"api/music/like/{songMetadataId}", null);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<LikeToggleResult>();
            }
            _logger.LogWarning("ToggleLike returned {StatusCode}", response.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to toggle like for song {SongMetadataId}", songMetadataId);
            return null;
        }
    }

    public async Task<LikeToggleResult?> ToggleDislikeAsync(int songMetadataId)
    {
        var client = _httpClientFactory.CreateClient("MusicSalesApi");
        try
        {
            var response = await client.PostAsync($"api/music/dislike/{songMetadataId}", null);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<LikeToggleResult>();
            }
            _logger.LogWarning("ToggleDislike returned {StatusCode}", response.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to toggle dislike for song {SongMetadataId}", songMetadataId);
            return null;
        }
    }

    public async Task<Dictionary<int, bool?>> GetBulkUserLikeStatusAsync(IEnumerable<int> songIds)
    {
        var client = _httpClientFactory.CreateClient("MusicSalesApi");
        try
        {
            var ids = string.Join(",", songIds);
            if (string.IsNullOrEmpty(ids)) return new();

            var result = await client.GetFromJsonAsync<List<UserLikeStatusDto>>($"api/music/likes/user-status?ids={ids}");
            if (result == null) return new();

            return result.ToDictionary(r => r.SongMetadataId, r => r.UserLikeStatus);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch bulk user like status");
            return new();
        }
    }

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        if (e.NetworkAccess == NetworkAccess.Internet)
        {
            TriggerPendingStreamFlush("connectivity was restored");
        }
    }

    private void TriggerPendingStreamFlush(string reason)
    {
        _ = TriggerPendingStreamFlushAsync(reason);
    }

    private async Task TriggerPendingStreamFlushAsync(string reason)
    {
        if (!await HasPendingStreamRecordsAsync().ConfigureAwait(false))
        {
            return;
        }

        _logger.LogInformation("Triggering pending stream flush because {Reason}", reason);
        await FlushPendingStreamRecordsAsync().ConfigureAwait(false);
    }

    private async Task InitializePendingStreamRetryLoopAsync()
    {
        if (await HasPendingStreamRecordsAsync().ConfigureAwait(false))
        {
            EnsurePendingStreamRetryLoopStarted();
        }
    }

    private void EnsurePendingStreamRetryLoopStarted()
    {
        lock (_pendingStreamRetryLoopLock)
        {
            if (_pendingStreamRetryLoopTask is { IsCompleted: false })
            {
                return;
            }

            _pendingStreamRetryLoopTask = RunPendingStreamRetryLoopAsync();
        }
    }

    private async Task RunPendingStreamRetryLoopAsync()
    {
        try
        {
            while (await HasPendingStreamRecordsAsync().ConfigureAwait(false))
            {
                await Task.Delay(_pendingStreamRetryInterval).ConfigureAwait(false);

                if (!await HasPendingStreamRecordsAsync().ConfigureAwait(false))
                {
                    break;
                }

                _logger.LogInformation(
                    "Retrying pending stream records after waiting {RetryIntervalSeconds} seconds",
                    _pendingStreamRetryInterval.TotalSeconds);

                await FlushPendingStreamRecordsAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pending stream retry loop failed unexpectedly");
        }
    }

    private async Task<StreamRecordAttemptResult> SendStreamRecordAsync(int songMetadataId)
    {
        var client = _httpClientFactory.CreateClient("MusicSalesApi");
        try
        {
            var response = await client.PostAsync($"api/music/stream/{songMetadataId}", null).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("RecordStream returned {StatusCode} for song {SongMetadataId}", response.StatusCode, songMetadataId);
                return new StreamRecordAttemptResult(null, false, ShouldDropPendingRecord(response.StatusCode));
            }

            var result = await response.Content.ReadFromJsonAsync<StreamRecordResponse>().ConfigureAwait(false);
            if (result is { CountIncremented: true, StreamCount: int streamCount })
            {
                OnStreamCountRecorded?.Invoke(songMetadataId, streamCount);
            }
            else if (result is { CountIncremented: false, StreamCount: int suppressedCount })
            {
                _logger.LogInformation(
                    "Server accepted stream report for song {SongMetadataId} without incrementing the count. Current count: {StreamCount}",
                    songMetadataId,
                    suppressedCount);
            }

            return new StreamRecordAttemptResult(result?.StreamCount, false, false);
        }
        catch (HttpRequestException ex) when (ShouldQueueStreamRecordRetry(ex))
        {
            _logger.LogWarning(ex, "Transient failure recording stream for song {SongMetadataId}; queuing for retry", songMetadataId);
            return new StreamRecordAttemptResult(null, true, false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record stream for song {SongMetadataId}", songMetadataId);
            return new StreamRecordAttemptResult(null, false, false);
        }
    }

    private async Task QueuePendingStreamRecordAsync(int songMetadataId)
    {
        await _pendingStreamFlushLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var pendingStreamRecords = await LoadPendingStreamRecordsAsync().ConfigureAwait(false);
            pendingStreamRecords.Add(new PendingStreamRecord(songMetadataId, DateTime.UtcNow));
            if (pendingStreamRecords.Count > MaxPendingStreamRecords)
            {
                var overflowCount = pendingStreamRecords.Count - MaxPendingStreamRecords;
                pendingStreamRecords.RemoveRange(0, overflowCount);
                _logger.LogWarning(
                    "Trimmed {OverflowCount} old pending stream records to enforce the queue size limit",
                    overflowCount);
            }

            await SavePendingStreamRecordsAsync(pendingStreamRecords).ConfigureAwait(false);
        }
        finally
        {
            _pendingStreamFlushLock.Release();
        }
    }

    private async Task<bool> HasPendingStreamRecordsAsync()
        => (await LoadPendingStreamRecordsAsync().ConfigureAwait(false)).Count > 0;

    private Task<List<PendingStreamRecord>> LoadPendingStreamRecordsAsync() =>
        Task.Run(LoadPendingStreamRecords);

    private List<PendingStreamRecord> LoadPendingStreamRecords()
    {
        var serializedPendingStreamRecords = _appPreferenceStore.GetString(PendingStreamRecordsPreferenceKey);
        if (string.IsNullOrWhiteSpace(serializedPendingStreamRecords))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<PendingStreamRecord>>(
                       serializedPendingStreamRecords,
                       PendingStreamRecordsSerializerOptions)
                   ?? [];
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize pending stream records. Clearing the saved queue.");
            _appPreferenceStore.Remove(PendingStreamRecordsPreferenceKey);
            return [];
        }
    }

    private void SavePendingStreamRecords(List<PendingStreamRecord> pendingStreamRecords)
    {
        if (pendingStreamRecords.Count == 0)
        {
            _appPreferenceStore.Remove(PendingStreamRecordsPreferenceKey);
            return;
        }

        _appPreferenceStore.SetString(
            PendingStreamRecordsPreferenceKey,
            JsonSerializer.Serialize(pendingStreamRecords, PendingStreamRecordsSerializerOptions));
    }

    private Task SavePendingStreamRecordsAsync(List<PendingStreamRecord> pendingStreamRecords) =>
        Task.Run(() => SavePendingStreamRecords(pendingStreamRecords));

    private bool ShouldQueueStreamRecordRetry(HttpRequestException exception)
    {
        if (_connectivity.NetworkAccess != NetworkAccess.Internet)
        {
            return true;
        }

        return ExceptionChainContains(exception, "UnknownHostException")
            || ExceptionChainContains(exception, "Unable to resolve host")
            || ExceptionChainContains(exception, "SocketException")
            || ExceptionChainContains(exception, "ConnectException")
            || ExceptionChainContains(exception, "Network is unreachable")
            || ExceptionChainContains(exception, "No route to host");
    }

    private static bool ExceptionChainContains(Exception exception, string value)
    {
        for (var current = exception; current != null; current = current.InnerException!)
        {
            if ((current.Message?.Contains(value, StringComparison.OrdinalIgnoreCase) ?? false)
                || (current.GetType().Name.Contains(value, StringComparison.OrdinalIgnoreCase))
                || (current.GetType().FullName?.Contains(value, StringComparison.OrdinalIgnoreCase) ?? false))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ShouldDropPendingRecord(HttpStatusCode statusCode)
        => statusCode == HttpStatusCode.BadRequest || statusCode == HttpStatusCode.NotFound;

    private sealed record UserLikeStatusDto(int SongMetadataId, bool? UserLikeStatus);
    private sealed record PendingStreamRecord(int SongMetadataId, DateTime RecordedUtc);
    private sealed record StreamRecordAttemptResult(int? StreamCount, bool ShouldQueueForRetry, bool ShouldDropPendingRecord);

    public Task<(bool Success, string ErrorMessage)> VerifyGooglePlayPurchaseAsync(string purchaseToken, string? orderId)
        => VerifySubscriptionPurchaseAsync(BillingPurchaseVerificationRequest.ForGooglePlay(purchaseToken, orderId));

    public Task<(bool Success, string ErrorMessage)> VerifySubscriptionPurchaseAsync(BillingPurchaseVerificationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Provider))
        {
            return Task.FromResult((false, "Billing provider is required."));
        }

        return request.Provider switch
        {
            BillingProviders.GooglePlay => VerifyGooglePlayPurchaseInternalAsync(request),
            BillingProviders.Apple => VerifyApplePurchaseInternalAsync(request),
            _ => Task.FromResult((false, $"Unsupported billing provider '{request.Provider}'."))
        };
    }

    private async Task<(bool Success, string ErrorMessage)> VerifyGooglePlayPurchaseInternalAsync(BillingPurchaseVerificationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.PurchaseToken))
        {
            return (false, "Purchase token is required.");
        }

        var client = _httpClientFactory.CreateClient("MusicSalesApi");
        try
        {
            var payload = new
            {
                PurchaseToken = request.PurchaseToken,
                OrderId = request.OrderId ?? string.Empty,
                PriceAmountMicros = request.PriceAmountMicros,
                PriceCurrencyCode = request.PriceCurrencyCode ?? string.Empty,
                FormattedPrice = request.FormattedPrice ?? string.Empty,
                TimeZoneId = GetLocalTimeZoneId()
            };
            var response = await client.PostAsJsonAsync("api/subscription/google-play/verify", payload);

            if (response.IsSuccessStatusCode)
            {
                return (true, string.Empty);
            }

            var errorMessage = await ApiErrorMessageFormatter.ReadDisplayMessageAsync(response);
            _logger.LogWarning("Google Play purchase verification failed: {StatusCode} {ErrorMessage}",
                response.StatusCode, errorMessage);
            return (false, errorMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to verify Google Play purchase with server");
            return (false, $"Unable to connect to server: {ex.Message}");
        }
    }

    private async Task<(bool Success, string ErrorMessage)> VerifyApplePurchaseInternalAsync(BillingPurchaseVerificationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.TransactionId))
        {
            return (false, "Transaction ID is required.");
        }

        var client = _httpClientFactory.CreateClient("MusicSalesApi");
        try
        {
            var payload = new
            {
                TransactionId = request.TransactionId,
                AppAccountToken = request.AppAccountToken ?? string.Empty,
                TimeZoneId = GetLocalTimeZoneId()
            };
            var response = await client.PostAsJsonAsync("api/subscription/app-store/verify", payload);

            if (response.IsSuccessStatusCode)
            {
                return (true, string.Empty);
            }

            var errorMessage = await ApiErrorMessageFormatter.ReadDisplayMessageAsync(response);
            _logger.LogWarning("Apple App Store purchase verification failed: {StatusCode} {ErrorMessage}",
                response.StatusCode, errorMessage);
            return (false, errorMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to verify Apple App Store purchase with server");
            return (false, $"Unable to connect to server: {ex.Message}");
        }
    }

    private static string GetLocalTimeZoneId()
    {
        try
        {
            return TimeZoneInfo.Local.Id;
        }
        catch
        {
            return "UTC";
        }
    }

    public async Task<SubscriptionStatusDto?> GetSubscriptionStatusAsync()
    {
        var client = _httpClientFactory.CreateClient("MusicSalesApi");
        try
        {
            return await client.GetFromJsonAsync<SubscriptionStatusDto>("api/subscription/status");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load subscription status");
            return null;
        }
    }

    public async Task<(bool Success, DateTime? EndDate)> CancelSubscriptionAsync()
    {
        var client = _httpClientFactory.CreateClient("MusicSalesApi");
        try
        {
            var response = await client.PostAsync("api/subscription/cancel", null);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<CancelResponse>();
                return (result?.Success ?? false, result?.EndDate);
            }

            var errorBody = await response.Content.ReadAsStringAsync();
            _logger.LogWarning("Cancel subscription failed: {Status} {Body}", response.StatusCode, errorBody);
            return (false, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cancel subscription");
            return (false, null);
        }
    }

    private sealed record CancelResponse(bool Success, DateTime? EndDate);

    private sealed record StreamRecordResponse(int SongMetadataId, int StreamCount, bool CountIncremented);

    public async Task<bool> ReportSongAsync(int songMetadataId, string reason)
    {
        var client = _httpClientFactory.CreateClient("MusicSalesApi");
        try
        {
            var response = await client.PostAsJsonAsync($"api/music/report/{songMetadataId}", new { Reason = reason });
            if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
                throw new InvalidOperationException("You have already reported this song.");
            return response.IsSuccessStatusCode;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to report song {SongMetadataId}", songMetadataId);
            return false;
        }
    }
}
