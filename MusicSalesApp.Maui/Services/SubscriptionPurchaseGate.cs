namespace MusicSalesApp.Maui.Services;

/// <summary>
/// Whether a subscription purchase may go ahead, and what to say when it may not.
///
/// A store purchase made while signed out cannot be attached to anything: the payment carries no
/// app account token, and the server's verify endpoint is authenticated, so the purchase completes
/// at the store and the server never learns about it. The customer is charged and gets nothing —
/// which is exactly what happened when the preview-limit prompt offered "Subscribe Now" to a
/// signed-out listener. Sign-in has to come first.
/// </summary>
public static class SubscriptionPurchaseGate
{
    public const string SignInRequiredTitle = "Sign In First";

    public const string SignInRequiredMessage =
        "A subscription has to be attached to your account, so please sign in or create an account before subscribing.";

    public const string SignInRequiredAccept = "Sign In";
    public const string SignInRequiredCancel = "Not Now";

    // --- The preview-limit prompt ---
    //
    // The prompt asks for the right thing up front rather than offering to subscribe and then
    // refusing: a signed-out listener was shown "Subscribe Now", tapped it, and got a second dialog
    // telling them to sign in first. One prompt, one answer.

    public const string PreviewLimitTitle = "Preview Limit";
    public const string PreviewLimitSubscribeMessage = "Subscribe for unlimited listening!";
    public const string PreviewLimitSignInMessage = "Sign in to subscribe for unlimited listening.";
    public const string PreviewLimitSubscribeAccept = "Subscribe Now";
    public const string PreviewLimitDecline = "Not Now";

    public static string PreviewLimitMessage(bool isSignedIn)
        => isSignedIn ? PreviewLimitSubscribeMessage : PreviewLimitSignInMessage;

    public static string PreviewLimitAccept(bool isSignedIn)
        => isSignedIn ? PreviewLimitSubscribeAccept : SignInRequiredAccept;

    public static bool RequiresSignIn(bool isSignedIn) => !isSignedIn;

    /// <summary>
    /// Sends the listener to sign in, flagged to come back to Home afterwards — where a signed-in
    /// non-subscriber is offered Subscribe Now. The flag is the same one the login, register and
    /// verify-email screens already hand between themselves, so registering instead of signing in
    /// still lands in the same place.
    /// </summary>
    public static Task GoToSignInAsync(INavigationService navigationService)
        => navigationService.GoToAsync(NavigationRoutes.LoginEntry, new Dictionary<string, object>
        {
            [NavigationRoutes.ReturnToHomeAfterAuthParameter] = true
        });

    /// <summary>
    /// Returns true when the purchase may proceed. When it may not, offers to take the listener to
    /// sign-in and returns false — the caller must not reach the store.
    /// </summary>
    public static async Task<bool> EnsureSignedInAsync(
        IAuthService authService,
        IAlertService alertService,
        INavigationService navigationService)
    {
        if (!RequiresSignIn(authService.IsLoggedIn))
        {
            return true;
        }

        var signIn = await alertService.ShowConfirmAsync(
            SignInRequiredTitle,
            SignInRequiredMessage,
            SignInRequiredAccept,
            SignInRequiredCancel);

        if (signIn)
        {
            await GoToSignInAsync(navigationService);
        }

        return false;
    }
}
