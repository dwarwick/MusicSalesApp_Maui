using Microsoft.Maui.Authentication;

namespace MusicSalesApp.Maui.Services;

public class WebAuthenticatorService : IWebAuthenticatorService
{
    public Task<WebAuthenticatorResult> AuthenticateAsync(Uri startUri, Uri callbackUri)
    {
        return WebAuthenticator.Default.AuthenticateAsync(new WebAuthenticatorOptions
        {
            Url = startUri,
            CallbackUrl = callbackUri,
            PrefersEphemeralWebBrowserSession = true
        });
    }
}