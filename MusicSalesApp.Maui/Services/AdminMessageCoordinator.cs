using System.Globalization;
using Microsoft.Extensions.Logging;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Services;

public sealed class AdminMessageCoordinator : IAdminMessageCoordinator
{
    private const string DefaultDialogTitle = "Message from StreamTunes";

    private readonly IAdminMessageApiService _adminMessageApiService;
    private readonly IAlertService _alertService;
    private readonly IAuthService _authService;
    private readonly ISignalRService _signalRService;
    private readonly ILogger<AdminMessageCoordinator> _logger;
    private readonly SemaphoreSlim _processLock = new(1, 1);

    private bool _initialized;

    public AdminMessageCoordinator(
        IAdminMessageApiService adminMessageApiService,
        IAlertService alertService,
        IAuthService authService,
        ISignalRService signalRService,
        ILogger<AdminMessageCoordinator> logger)
    {
        _adminMessageApiService = adminMessageApiService;
        _alertService = alertService;
        _authService = authService;
        _signalRService = signalRService;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        _authService.AuthStateChanged += OnAuthStateChanged;
        _signalRService.OnAdminMessagesUpdated += OnAdminMessagesUpdated;

        await _signalRService.SyncAdminMessageConnectionAsync();
    }

    public async Task ProcessPendingMessagesAsync()
    {
        if (!_initialized || !_authService.IsLoggedIn || !_authService.UserId.HasValue)
        {
            return;
        }

        await _processLock.WaitAsync();

        try
        {
            if (!_authService.IsLoggedIn || !_authService.UserId.HasValue)
            {
                return;
            }

            var messages = await _adminMessageApiService.GetPendingDialogMessagesAsync();
            foreach (var message in messages.OrderBy(message => message.CreatedAtUtc))
            {
                if (!_authService.IsLoggedIn)
                {
                    return;
                }

                var dialogTitle = GetDialogTitle(message);
                var displayText = BuildDisplayText(message);
                await _alertService.DisplayAlertAsync(dialogTitle, displayText, "Acknowledge");

                var acknowledged = await _adminMessageApiService.AcknowledgeMessageAsync(message.MessageId);
                if (!acknowledged)
                {
                    _logger.LogWarning("Admin message {MessageId} may not have been acknowledged on the server", message.MessageId);
                }
            }
        }
        finally
        {
            _processLock.Release();
        }
    }

    private void OnAuthStateChanged()
    {
        _ = HandleAuthStateChangedAsync();
    }

    private void OnAdminMessagesUpdated()
    {
        _ = ProcessPendingMessagesAsync();
    }

    private async Task HandleAuthStateChangedAsync()
    {
        try
        {
            await _signalRService.SyncAdminMessageConnectionAsync();
            await ProcessPendingMessagesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to sync the admin message SignalR connection after auth state changed");
        }
    }

    private static string BuildDisplayText(PendingAdminMessageDto message)
    {
        var createdLocal = message.CreatedAtUtc.Kind == DateTimeKind.Utc
            ? message.CreatedAtUtc.ToLocalTime()
            : DateTime.SpecifyKind(message.CreatedAtUtc, DateTimeKind.Utc).ToLocalTime();

        var createdDate = FormatCreatedDate(createdLocal, CultureInfo.CurrentCulture);
        var sourcePrefix = string.IsNullOrWhiteSpace(message.Subject)
            ? string.Empty
            : $"{DefaultDialogTitle}\n";

        return $"{sourcePrefix}Created: {createdDate}\n\n{message.MessageText}";
    }

    private static string GetDialogTitle(PendingAdminMessageDto message)
    {
        return string.IsNullOrWhiteSpace(message.Subject)
            ? DefaultDialogTitle
            : message.Subject.Trim();
    }

    private static string FormatCreatedDate(DateTime createdLocal, CultureInfo culture)
    {
        if (IsUnitedStatesCulture(culture))
        {
            return createdLocal.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture);
        }

        return createdLocal.ToString("d", culture);
    }

    private static bool IsUnitedStatesCulture(CultureInfo culture)
    {
        try
        {
            var regionCulture = culture.IsNeutralCulture
                ? CultureInfo.CreateSpecificCulture(culture.Name)
                : culture;

            return new RegionInfo(regionCulture.Name).TwoLetterISORegionName == "US";
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}