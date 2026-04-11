namespace MusicSalesApp.Maui.Services;

/// <summary>
/// Attaches the JWT Bearer token to every outgoing HTTP request
/// so that authenticated API calls (like/dislike, stream recording)
/// automatically include the user's credentials.
/// </summary>
public class AuthDelegatingHandler : DelegatingHandler
{
    private readonly IAuthService _authService;

    public AuthDelegatingHandler(IAuthService authService)
    {
        _authService = authService;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (_authService.IsLoggedIn && !string.IsNullOrEmpty(_authService.Token))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _authService.Token);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
