using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace MusicSalesApp.Maui.Services;

/// <inheritdoc />
public class FollowService : IFollowService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IAuthService _authService;
    private readonly IArtistFollowNotifier _notifier;
    private readonly ILogger<FollowService> _logger;

    public FollowService(
        IHttpClientFactory httpClientFactory,
        IAuthService authService,
        IArtistFollowNotifier notifier,
        ILogger<FollowService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _authService = authService;
        _notifier = notifier;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<FollowStateResult?> SetFollowStateAsync(
        int personaId,
        bool following,
        int? sourceSongId = null)
    {
        if (personaId <= 0 || !_authService.IsLoggedIn)
        {
            return null;
        }

        var client = _httpClientFactory.CreateClient("MusicSalesApi");

        try
        {
            // followAsPersonaId is deliberately never sent. It would let a creator be NAMED to the
            // artist they follow, and the server only accepts it when they have switched that
            // consent on - but there is no mobile endpoint that reports which of their personas are
            // eligible, so the app cannot ask. Sending nothing follows anonymously, which is the
            // safe default in a privacy feature. Add the picker when the options endpoint exists.
            var response = await client
                .PutAsJsonAsync(
                    $"api/mobile/follows/{personaId}",
                    new { Following = following, SourceSongId = sourceSongId })
                .ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content
                    .ReadFromJsonAsync<FollowStateResult>()
                    .ConfigureAwait(false);

                // Trust the server's answer rather than what was asked for: following an artist you
                // already follow answers AlreadyFollowing with Following=true, and a refusal that
                // still returned 200 would otherwise be recorded as success.
                var settled = result?.Following ?? following;
                _notifier.NotifyFollowStateChanged(personaId, settled);

                return result ?? new FollowStateResult(personaId, settled, null);
            }

            // Every domain refusal - following yourself, a blocked artist, an unavailable persona -
            // is a 400 by contract, not a 5xx. Logged at Information rather than Warning because it
            // is the server working correctly, and Warning here would train the reader to ignore it.
            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                _logger.LogInformation(
                    "The server refused a follow change for persona {PersonaId}.", personaId);
            }
            else
            {
                _logger.LogWarning(
                    "Follow change for persona {PersonaId} failed with {StatusCode}.",
                    personaId,
                    response.StatusCode);
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to change the follow state for persona {PersonaId}.", personaId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<HashSet<int>> GetFollowedPersonaIdsAsync(IEnumerable<int> personaIds)
    {
        var ids = personaIds?.Where(id => id > 0).Distinct().ToList() ?? [];

        if (ids.Count == 0 || !_authService.IsLoggedIn)
        {
            return [];
        }

        var client = _httpClientFactory.CreateClient("MusicSalesApi");

        try
        {
            // POST rather than a query string: IIS caps a URL at 2048 characters and a full library
            // page of persona ids passes that easily. Same reason the bulk likes endpoint is a POST.
            var response = await client
                .PostAsJsonAsync("api/mobile/follows/states", new { PersonaIds = ids })
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Bulk follow-state lookup failed with {StatusCode}.", response.StatusCode);
                return [];
            }

            var followed = await response.Content
                .ReadFromJsonAsync<List<int>>()
                .ConfigureAwait(false);

            return followed is null ? [] : [.. followed];
        }
        catch (Exception ex)
        {
            // Empty renders as "not following", which is the safe direction: the bell is a control
            // the user can correct, not a claim about their data. Failing loudly here would put an
            // error in front of someone who only opened the library.
            _logger.LogWarning(ex, "Failed to load follow states for {Count} personas.", ids.Count);
            return [];
        }
    }
}
