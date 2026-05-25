using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicSalesApp.Common.Helpers;
using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.ViewModels;

public partial class ContactUsViewModel : ObservableObject
{
    public const int MaxMessageLength = 4000;
    private const string SuccessAlertTitle = "Message Sent";
    private const string SuccessAlertBody = "Your message was sent. Check your email for a copy of the message.";

    private readonly IAuthService _authService;
    private readonly IAlertService _alertService;
    private readonly IContactApiService _contactApiService;
    private readonly INavigationService _navigationService;

    public IReadOnlyList<string> SubjectOptions { get; } = ContactRequestSubjectTypes.All;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSubmit))]
    [NotifyCanExecuteChangedFor(nameof(SubmitCommand))]
    public partial string? SelectedSubject { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSubmit))]
    [NotifyCanExecuteChangedFor(nameof(SubmitCommand))]
    public partial int SelectedSubjectIndex { get; set; } = -1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSubmit))]
    [NotifyCanExecuteChangedFor(nameof(SubmitCommand))]
    public partial string Message { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSubmit))]
    [NotifyCanExecuteChangedFor(nameof(SubmitCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    public bool CanSubmit =>
        !IsBusy &&
        _authService.IsLoggedIn &&
        _authService.EmailConfirmed &&
        !string.IsNullOrWhiteSpace(SelectedSubject) &&
        !string.IsNullOrWhiteSpace(Message) &&
        Message.Trim().Length <= MaxMessageLength;

    public ContactUsViewModel(
        IAuthService authService,
        IAlertService alertService,
        IContactApiService contactApiService,
        INavigationService navigationService)
    {
        _authService = authService;
        _alertService = alertService;
        _contactApiService = contactApiService;
        _navigationService = navigationService;
    }

    partial void OnSelectedSubjectChanged(string? value)
    {
        var selectedIndex = GetSelectedSubjectIndex(value);
        if (SelectedSubjectIndex != selectedIndex)
        {
            SelectedSubjectIndex = selectedIndex;
        }
    }

    partial void OnSelectedSubjectIndexChanged(int value)
    {
        var selectedSubject = value >= 0 && value < SubjectOptions.Count
            ? SubjectOptions[value]
            : null;

        if (!string.Equals(SelectedSubject, selectedSubject, StringComparison.Ordinal))
        {
            SelectedSubject = selectedSubject;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSubmit))]
    private async Task SubmitAsync()
    {
        ErrorMessage = null;
        StatusMessage = null;

        if (!_authService.IsLoggedIn || !_authService.EmailConfirmed)
        {
            ErrorMessage = "Please verify your email before contacting us.";
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedSubject))
        {
            ErrorMessage = "Please select a subject.";
            return;
        }

        if (!ContactRequestSubjectTypes.All.Contains(SelectedSubject, StringComparer.Ordinal))
        {
            ErrorMessage = "Please select a valid subject.";
            return;
        }

        var trimmedMessage = Message.Trim();
        if (string.IsNullOrWhiteSpace(trimmedMessage))
        {
            ErrorMessage = "Please enter a message.";
            return;
        }

        if (trimmedMessage.Length > MaxMessageLength)
        {
            ErrorMessage = $"Please keep your message under {MaxMessageLength} characters.";
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _contactApiService.SubmitContactRequestAsync(SelectedSubject, trimmedMessage);
            if (result.Success)
            {
                await _alertService.DisplayAlertAsync(SuccessAlertTitle, SuccessAlertBody, "OK");

                SelectedSubjectIndex = -1;
                Message = string.Empty;
                await _navigationService.GoBackAsync();
                return;
            }

            ErrorMessage = result.ErrorMessage ?? "Unable to send your message. Please try again later.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Unable to complete your request: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private int GetSelectedSubjectIndex(string? subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            return -1;
        }

        for (var index = 0; index < SubjectOptions.Count; index++)
        {
            if (string.Equals(SubjectOptions[index], subject, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }
}