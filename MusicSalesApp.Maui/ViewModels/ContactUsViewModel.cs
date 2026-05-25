using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicSalesApp.Common.Helpers;
using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.ViewModels;

public partial class ContactUsViewModel : ObservableObject
{
    public const int MaxMessageLength = 4000;

    private readonly IAuthService _authService;
    private readonly IContactApiService _contactApiService;

    public IReadOnlyList<string> SubjectOptions { get; } = ContactRequestSubjectTypes.All;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSubmit))]
    [NotifyCanExecuteChangedFor(nameof(SubmitCommand))]
    public partial string? SelectedSubject { get; set; }

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

    public ContactUsViewModel(IAuthService authService, IContactApiService contactApiService)
    {
        _authService = authService;
        _contactApiService = contactApiService;
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
                Message = string.Empty;
                StatusMessage = "Your message has been sent.";
                return;
            }

            ErrorMessage = result.ErrorMessage ?? "Unable to send your message. Please try again later.";
        }
        finally
        {
            IsBusy = false;
        }
    }
}