namespace MusicSalesApp.Maui.ViewModels;

public class ContactRequestDto
{
    public string Subject { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;
}

public sealed record ContactSubmitResult(bool Success, string? ErrorMessage = null);