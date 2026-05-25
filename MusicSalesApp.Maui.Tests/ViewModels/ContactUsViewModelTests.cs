using Moq;
using MusicSalesApp.Common.Helpers;
using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Tests.ViewModels;

[TestFixture]
public class ContactUsViewModelTests
{
    private Mock<IAuthService> _mockAuthService = null!;
    private Mock<IContactApiService> _mockContactApiService = null!;
    private ContactUsViewModel _viewModel = null!;

    [SetUp]
    public void SetUp()
    {
        _mockAuthService = new Mock<IAuthService>();
        _mockContactApiService = new Mock<IContactApiService>();
        SetAuthState(isLoggedIn: true, emailConfirmed: true);
        _viewModel = new ContactUsViewModel(_mockAuthService.Object, _mockContactApiService.Object);
    }

    [Test]
    public void SubjectOptions_UsesSharedContactSubjectConstants()
    {
        Assert.That(_viewModel.SubjectOptions, Is.EqualTo(ContactRequestSubjectTypes.All));
    }

    [Test]
    public void CanSubmit_IsFalse_WhenSubjectMissing()
    {
        _viewModel.Message = "Hello";

        Assert.That(_viewModel.CanSubmit, Is.False);
    }

    [Test]
    public void CanSubmit_IsFalse_WhenUserEmailIsNotConfirmed()
    {
        SetAuthState(isLoggedIn: true, emailConfirmed: false);
        _viewModel = new ContactUsViewModel(_mockAuthService.Object, _mockContactApiService.Object)
        {
            SelectedSubject = ContactRequestSubjectTypes.BugReport,
            Message = "Hello"
        };

        Assert.That(_viewModel.CanSubmit, Is.False);
        Assert.That(_viewModel.SubmitCommand.CanExecute(null), Is.False);
    }

    [Test]
    public async Task SubmitAsync_ValidRequest_ClearsMessageAndSetsStatus()
    {
        _viewModel.SelectedSubject = ContactRequestSubjectTypes.AppSuggestion;
        _viewModel.Message = "Please add a compact player.";
        _mockContactApiService
            .Setup(service => service.SubmitContactRequestAsync(ContactRequestSubjectTypes.AppSuggestion, "Please add a compact player."))
            .ReturnsAsync(new ContactSubmitResult(true));

        await _viewModel.SubmitCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.Message, Is.Empty);
            Assert.That(_viewModel.StatusMessage, Is.EqualTo("Your message has been sent."));
            Assert.That(_viewModel.ErrorMessage, Is.Null);
            Assert.That(_viewModel.IsBusy, Is.False);
        });
    }

    [Test]
    public async Task SubmitAsync_ApiFailure_PreservesMessageAndSetsError()
    {
        _viewModel.SelectedSubject = ContactRequestSubjectTypes.BugReport;
        _viewModel.Message = "Playback fails after one minute.";
        _mockContactApiService
            .Setup(service => service.SubmitContactRequestAsync(ContactRequestSubjectTypes.BugReport, "Playback fails after one minute."))
            .ReturnsAsync(new ContactSubmitResult(false, "Please wait before sending another message."));

        await _viewModel.SubmitCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.Message, Is.EqualTo("Playback fails after one minute."));
            Assert.That(_viewModel.ErrorMessage, Is.EqualTo("Please wait before sending another message."));
            Assert.That(_viewModel.StatusMessage, Is.Null);
            Assert.That(_viewModel.IsBusy, Is.False);
        });
    }

    [Test]
    public void CanSubmit_IsFalse_WhenMessageExceedsLimit()
    {
        _viewModel.SelectedSubject = ContactRequestSubjectTypes.BugReport;
        _viewModel.Message = new string('x', ContactUsViewModel.MaxMessageLength + 1);

        Assert.That(_viewModel.CanSubmit, Is.False);
    }

    private void SetAuthState(bool isLoggedIn, bool emailConfirmed)
    {
        _mockAuthService.SetupGet(service => service.IsLoggedIn).Returns(isLoggedIn);
        _mockAuthService.SetupGet(service => service.EmailConfirmed).Returns(emailConfirmed);
    }
}