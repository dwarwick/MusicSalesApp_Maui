namespace MusicSalesApp.Maui.ViewModels;

public static class TipResultKinds
{
    public const string RequiresApproval = "RequiresApproval";
    public const string Succeeded = "Succeeded";
    public const string Cancelled = "Cancelled";
    public const string ValidationBlocked = "ValidationBlocked";
    public const string FraudPrevented = "FraudPrevented";
    public const string PaymentFailure = "PaymentFailure";
}

public class CreateTipOrderRequestDto
{
    public int CreatorId { get; set; }
    public int? SongMetadataId { get; set; }
    public decimal Amount { get; set; }
    public string? DeviceFingerprint { get; set; }
}

public class TipOrderRequestDto
{
    public string PayPalOrderId { get; set; } = string.Empty;
}

public class TipOperationResponseDto
{
    public bool Success { get; set; }
    public string ResultKind { get; set; } = TipResultKinds.PaymentFailure;
    public string? Message { get; set; }
    public string? ApprovalUrl { get; set; }
    public decimal? Amount { get; set; }
}