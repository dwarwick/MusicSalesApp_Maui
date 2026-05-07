namespace MusicSalesApp.Maui.ViewModels;

public class PendingAdminMessageDto
{
    public int MessageId { get; set; }

    public string Subject { get; set; } = string.Empty;

    public string MessageText { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }
}