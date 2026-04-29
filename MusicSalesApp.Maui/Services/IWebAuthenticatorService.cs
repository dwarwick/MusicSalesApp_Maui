using Microsoft.Maui.Authentication;

namespace MusicSalesApp.Maui.Services;

public interface IWebAuthenticatorService
{
    Task<WebAuthenticatorResult> AuthenticateAsync(Uri startUri, Uri callbackUri);
}