#if IOS
using AuthenticationServices;
using Foundation;
using Microsoft.Extensions.Logging;
using UIKit;

namespace MusicSalesApp.Maui.Services;

/// <summary>
/// Native Sign in with Apple, through AuthenticationServices.
/// </summary>
/// <remarks>
/// <para>
/// This is deliberately NOT the server-brokered web flow Google uses. Apple's native sheet
/// authenticates against the Apple ID already signed in on the device, so the user gets Face ID
/// rather than a web view asking for a password and a 2FA code. The price is that the app - not
/// the server - ends up holding the identity token, which it forwards to
/// <c>api/mobile-auth/apple/token</c> for verification.
/// </para>
/// <para>
/// This needs the <c>com.apple.developer.applesignin</c> entitlement and the matching capability
/// on the App ID. Without them the request fails at runtime with an "unknown" authorization error
/// rather than anything that names the real cause.
/// </para>
/// <para>
/// Hand-rolled rather than using <c>Microsoft.Maui.Authentication.AppleSignInAuthenticator</c>,
/// which drives the same sheet: that one surfaces a dismissed sheet and a genuine failure as the
/// same <see cref="TaskCanceledException"/>, and this flow has to tell them apart - a dismissal
/// shows the user nothing, a failure shows an error. It also returns an untyped property bag
/// rather than the identity token and authorization code as such. (Note the framework type is why
/// this one is named ...Service: the interface names would otherwise collide, since MAUI's
/// implicit usings pull in that namespace.)
/// </para>
/// </remarks>
public sealed class AppleSignInService : IAppleSignInService
{
    private readonly ILogger<AppleSignInService> _logger;

    public AppleSignInService(ILogger<AppleSignInService> logger) => _logger = logger;

    public bool IsSupported => true;

    public Task<AppleSignInResult> AuthenticateAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(AppleSignInResult.Cancelled());
        }

        var completion = new TaskCompletionSource<AppleSignInResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        // The sheet is UI, so it has to be raised on the main thread.
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                // Resolve the anchor BEFORE presenting. AuthenticationServices gives no callback at
                // all if it cannot present, and the await would then never return - leaving IsBusy
                // stuck true and every sign-in button disabled until the app is force-quit. Failing
                // here turns that hang into an error the user can act on.
                var anchor = FindPresentationAnchor();
                if (anchor is null)
                {
                    _logger.LogWarning("No key window to present the Sign in with Apple sheet in");
                    completion.TrySetResult(
                        AppleSignInResult.Failed("Sign in with Apple could not be started."));
                    return;
                }

                var request = new ASAuthorizationAppleIdProvider().CreateRequest();
                request.RequestedScopes = [ASAuthorizationScope.FullName, ASAuthorizationScope.Email];

                var controller = new ASAuthorizationController([request]);
                var coordinator = new AuthorizationCoordinator(controller, anchor, completion, _logger);
                controller.Delegate = coordinator;
                controller.PresentationContextProvider = coordinator;

                // The sheet itself cannot be dismissed programmatically before iOS 16, so this
                // releases the caller rather than closing the sheet - enough to stop a cancelled
                // caller waiting forever.
                cancellationToken.Register(() =>
                    completion.TrySetResult(AppleSignInResult.Cancelled()));

                controller.PerformRequests();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unable to start Sign in with Apple");
                completion.TrySetResult(
                    AppleSignInResult.Failed("Sign in with Apple could not be started."));
            }
        });

        return completion.Task;
    }

    /// <summary>
    /// The window the sheet is presented in, or null when there is none - during launch, a scene
    /// transition, or a return from background.
    /// </summary>
    private static UIWindow? FindPresentationAnchor()
        => Platform.GetCurrentUIViewController()?.View?.Window
           ?? UIApplication.SharedApplication.ConnectedScenes.ToArray()
               .OfType<UIWindowScene>()
               .SelectMany(scene => scene.Windows)
               .FirstOrDefault(window => window.IsKeyWindow);

    /// <summary>
    /// Bridges the two AuthenticationServices protocols onto the awaiting task.
    /// </summary>
    /// <remarks>
    /// ASAuthorizationController holds its delegate weakly, and nothing else references this
    /// object once <c>PerformRequests</c> returns - so instances root themselves in
    /// <see cref="InFlight"/> for the lifetime of the request. Dropping that would let the GC
    /// collect the delegate mid-sheet and the callback would never arrive.
    /// </remarks>
    private sealed class AuthorizationCoordinator
        : NSObject, IASAuthorizationControllerDelegate, IASAuthorizationControllerPresentationContextProviding
    {
        private static readonly HashSet<AuthorizationCoordinator> InFlight = [];

        private readonly ASAuthorizationController _controller;
        private readonly UIWindow _anchor;
        private readonly TaskCompletionSource<AppleSignInResult> _completion;
        private readonly ILogger _logger;

        public AuthorizationCoordinator(
            ASAuthorizationController controller,
            UIWindow anchor,
            TaskCompletionSource<AppleSignInResult> completion,
            ILogger logger)
        {
            _controller = controller;
            _anchor = anchor;
            _completion = completion;
            _logger = logger;

            lock (InFlight)
            {
                InFlight.Add(this);
            }
        }

        [Export("authorizationController:didCompleteWithAuthorization:")]
        public void DidComplete(ASAuthorizationController controller, ASAuthorization authorization)
        {
            try
            {
                if (authorization.GetCredential<ASAuthorizationAppleIdCredential>() is not { } credential)
                {
                    Finish(AppleSignInResult.Failed("Apple sign-in returned an unexpected credential."));
                    return;
                }

                var identityToken = DecodeUtf8(credential.IdentityToken);
                if (string.IsNullOrWhiteSpace(identityToken))
                {
                    Finish(AppleSignInResult.Failed("Apple sign-in did not return an identity token."));
                    return;
                }

                // Email and name are populated on the FIRST authorization only. On every later
                // sign-in they are null and the server has to fall back to what it stored.
                _logger.LogInformation(
                    "Sign in with Apple succeeded (first authorization: {IsFirstAuthorization})",
                    credential.Email is not null);

                Finish(new AppleSignInResult(
                    identityToken,
                    DecodeUtf8(credential.AuthorizationCode),
                    credential.Email ?? string.Empty,
                    FormatFullName(credential.FullName),
                    WasCancelled: false,
                    ErrorMessage: string.Empty));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to read the Apple sign-in credential");
                Finish(AppleSignInResult.Failed("Apple sign-in could not be completed."));
            }
        }

        [Export("authorizationController:didCompleteWithError:")]
        public void DidComplete(ASAuthorizationController controller, NSError error)
        {
            // Dismissing the sheet is not a failure worth showing the user an error banner for.
            if ((ASAuthorizationError)(long)error.Code is ASAuthorizationError.Canceled)
            {
                _logger.LogInformation("Sign in with Apple was cancelled by the user");
                Finish(AppleSignInResult.Cancelled());
                return;
            }

            _logger.LogWarning(
                "Sign in with Apple failed: {Code} {Description}", error.Code, error.LocalizedDescription);
            Finish(AppleSignInResult.Failed("Apple sign-in could not be completed. Please try again."));
        }

        [Export("presentationAnchorForAuthorizationController:")]
        public UIWindow GetPresentationAnchor(ASAuthorizationController controller) => _anchor;

        private void Finish(AppleSignInResult result)
        {
            _completion.TrySetResult(result);

            lock (InFlight)
            {
                InFlight.Remove(this);
            }

            _controller.Delegate = null;
            _controller.PresentationContextProvider = null;
        }

        private static string DecodeUtf8(NSData? data)
            => data is null ? string.Empty : NSString.FromData(data, NSStringEncoding.UTF8)?.ToString() ?? string.Empty;

        private static string FormatFullName(NSPersonNameComponents? name)
        {
            if (name is null)
            {
                return string.Empty;
            }

            return string.Join(' ', new[] { name.GivenName, name.FamilyName }
                .Where(part => !string.IsNullOrWhiteSpace(part)));
        }
    }
}
#endif
