namespace MusicSalesApp.Maui.Services;

public sealed record PermissionExplainerRequest(
    string Overline,
    string Title,
    string Message,
    string PrimaryButtonText,
    string? SecondaryButtonText = null,
    bool AllowBackdropDismiss = true,
    bool ShowDoNotAskAgainOption = false,
    string DoNotAskAgainText = "Don't ask me again");

public sealed record PermissionExplainerResult(bool Accepted, bool DoNotAskAgain);