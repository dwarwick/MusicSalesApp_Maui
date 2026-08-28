using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.ViewModels;

/// <summary>
/// Explains a refused thumbs-up/down.
///
/// One copy of the wording for all four screens that host the buttons, and the reason a blocked tap does
/// something visible: without it the button simply fails to respond, which reads as a broken control
/// rather than a rule.
/// </summary>
public static class RatingRequiresStreamNotice
{
    public const string Title = "Listen First";

    public const string Message = "Play this song for a little longer before rating it.";

    public static Task ReportAsync(LikeApplyOutcome outcome, IAlertService alertService)
    {
        return outcome == LikeApplyOutcome.NeedsStream
            ? alertService.DisplayAlertAsync(Title, Message, "OK")
            : Task.CompletedTask;
    }
}
