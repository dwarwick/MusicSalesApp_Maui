using Microsoft.Extensions.Logging;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Services;

public class TipFlowHandler : ITipFlowHandler
{
    internal const string LoginTitle = "Log in to tip creators";
    internal const string LoginMessage = "Log in and verify your account to send tips to creators.";
    internal const string ValidationTitle = "Account not verified";
    internal const string ValidationMessage = "Your account must be fully verified before you can send tips.";
    internal const string OwnSongTitle = "Can't send tip";
    internal const string OwnSongMessage = "You can't send tips to your own songs.";

    private readonly IAuthService _authService;
    private readonly ITipApiService _tipApiService;
    private readonly ITipAmountPicker _tipAmountPicker;
    private readonly IAlertService _alertService;
    private readonly IBrowserService _browserService;
    private readonly INetworkStatusService? _networkStatusService;
    private readonly ILogger<TipFlowHandler> _logger;

    public TipFlowHandler(
        IAuthService authService,
        ITipApiService tipApiService,
        ITipAmountPicker tipAmountPicker,
        IAlertService alertService,
        IBrowserService browserService,
        ILogger<TipFlowHandler> logger,
        INetworkStatusService? networkStatusService = null)
    {
        _authService = authService;
        _tipApiService = tipApiService;
        _tipAmountPicker = tipAmountPicker;
        _alertService = alertService;
        _browserService = browserService;
        _networkStatusService = networkStatusService;
        _logger = logger;
    }

    public bool CanShowTipButton(int? creatorId, int? creatorUserId)
    {
        // Tipping opens a PayPal approval flow in the browser, so it cannot work offline. Gating here
        // covers every TipButton placement at once. HasNoNetworkAccess rather than IsOffline: on a
        // constrained or unknown connection the flow still works, and hiding the button would take a
        // working feature away.
        if (_networkStatusService?.HasNoNetworkAccess == true)
            return false;

        if (!_authService.IsValidatedUser)
            return false;

        if (!creatorId.HasValue || creatorId.Value <= 0)
            return false;

        if (_authService.CreatorId.HasValue && _authService.CreatorId.Value == creatorId.Value)
            return false;

        return !_authService.UserId.HasValue
            || !creatorUserId.HasValue
            || _authService.UserId.Value != creatorUserId.Value;
    }

    public async Task ShowAsync(int songMetadataId, string songTitle, int? creatorId, int? creatorUserId)
    {
        if (songMetadataId <= 0 || !creatorId.HasValue || creatorId.Value <= 0)
            return;

        if (!_authService.IsLoggedIn)
        {
            await _alertService.DisplayAlertAsync(LoginTitle, LoginMessage, "OK");
            return;
        }

        if (!_authService.IsValidatedUser)
        {
            await _alertService.DisplayAlertAsync(ValidationTitle, ValidationMessage, "OK");
            return;
        }

        if (!CanShowTipButton(creatorId, creatorUserId))
        {
            await _alertService.DisplayAlertAsync(OwnSongTitle, OwnSongMessage, "OK");
            return;
        }

        var amount = await _tipAmountPicker.PickAmountAsync(songTitle);
        if (!amount.HasValue)
            return;

        var response = await _tipApiService.CreateOrderAsync(creatorId.Value, songMetadataId, amount.Value);
        if (response.Success &&
            response.ResultKind == TipResultKinds.RequiresApproval &&
            !string.IsNullOrWhiteSpace(response.ApprovalUrl))
        {
            try
            {
                await _browserService.OpenExternalAsync(response.ApprovalUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to open PayPal approval URL");
                await _alertService.DisplayAlertAsync("Tip Error", "Unable to open PayPal. Please try again.", "OK");
            }

            return;
        }

        await ShowOperationResultAsync(response);
    }

    public async Task<bool> HandleAppLinkAsync(Uri uri)
    {
        if (!IsTipCallback(uri))
            return false;

        await EnsureLoggedInAsync();
        if (!_authService.IsLoggedIn)
        {
            await _alertService.DisplayAlertAsync("Login Required", "Please log in again to finish your tip.", "OK");
            return true;
        }

        var status = GetQueryValue(uri, "tip_status");
        var payPalOrderId = GetQueryValue(uri, "token");

        TipOperationResponseDto response;
        if (string.Equals(status, "approved", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(payPalOrderId))
        {
            response = await _tipApiService.CaptureAsync(payPalOrderId);
        }
        else if (string.Equals(status, "cancelled", StringComparison.OrdinalIgnoreCase))
        {
            response = !string.IsNullOrWhiteSpace(payPalOrderId)
                ? await _tipApiService.CancelAsync(payPalOrderId)
                : new TipOperationResponseDto
                {
                    Success = true,
                    ResultKind = TipResultKinds.Cancelled,
                    Message = "Tip payment was cancelled."
                };
        }
        else
        {
            response = new TipOperationResponseDto
            {
                Success = false,
                ResultKind = TipResultKinds.PaymentFailure,
                Message = "Unable to process the PayPal return for this tip."
            };
        }

        await ShowOperationResultAsync(response);
        return true;
    }

    private async Task EnsureLoggedInAsync()
    {
        if (_authService.IsLoggedIn)
            return;

        try
        {
            await _authService.TryRestoreSessionAsync();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Session restore failed while handling a tip callback");
        }
    }

    private async Task ShowOperationResultAsync(TipOperationResponseDto response)
    {
        var title = response.ResultKind switch
        {
            TipResultKinds.Succeeded => "Tip Sent!",
            TipResultKinds.Cancelled => "Tip Cancelled",
            TipResultKinds.ValidationBlocked => "Can't send tip",
            TipResultKinds.FraudPrevented => "Tip blocked",
            _ => "Tip Error"
        };

        var message = !string.IsNullOrWhiteSpace(response.Message)
            ? response.Message
            : response.ResultKind switch
            {
                TipResultKinds.RequiresApproval => "Unable to open PayPal. Please try again.",
                TipResultKinds.Cancelled => "Tip payment was cancelled.",
                _ => "Unable to process the tip. Please try again."
            };

        await _alertService.DisplayAlertAsync(title, message, "OK");
    }

    private static bool IsTipCallback(Uri uri)
    {
        return uri.Scheme.Equals("streamtunes", StringComparison.OrdinalIgnoreCase)
            && uri.Host.Equals("tip", StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetQueryValue(Uri uri, string key)
    {
        if (string.IsNullOrWhiteSpace(uri.Query))
            return null;

        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pieces = pair.Split('=', 2);
            if (pieces.Length == 0)
                continue;

            if (!pieces[0].Equals(key, StringComparison.OrdinalIgnoreCase))
                continue;

            return pieces.Length == 2 ? Uri.UnescapeDataString(pieces[1]) : string.Empty;
        }

        return null;
    }
}