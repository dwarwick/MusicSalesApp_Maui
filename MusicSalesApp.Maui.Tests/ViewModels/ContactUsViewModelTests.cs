using Moq;
using MusicSalesApp.Common.Helpers;
using MusicSalesApp.Maui.Services;
using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Tests.ViewModels;

[TestFixture]
public class ContactUsViewModelTests
{
    private Mock<IAuthService> _mockAuthService = null!;
    private Mock<IAlertService> _mockAlertService = null!;
    private Mock<IContactApiService> _mockContactApiService = null!;
    private Mock<INavigationService> _mockNavigationService = null!;
    private ContactUsViewModel _viewModel = null!;

    [SetUp]
    public void SetUp()
    {
        _mockAuthService = new Mock<IAuthService>();
        _mockAlertService = new Mock<IAlertService>();
        _mockContactApiService = new Mock<IContactApiService>();
        _mockNavigationService = new Mock<INavigationService>();
        SetAuthState(isLoggedIn: true, emailConfirmed: true);
        _viewModel = new ContactUsViewModel(
            _mockAuthService.Object,
            _mockAlertService.Object,
            _mockContactApiService.Object,
            _mockNavigationService.Object);
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
        _viewModel = new ContactUsViewModel(
            _mockAuthService.Object,
            _mockAlertService.Object,
            _mockContactApiService.Object,
            _mockNavigationService.Object)
        {
            SelectedSubject = ContactRequestSubjectTypes.BugReport,
            Message = "Hello"
        };

        Assert.That(_viewModel.CanSubmit, Is.False);
        Assert.That(_viewModel.SubmitCommand.CanExecute(null), Is.False);
    }

    [Test]
    public void SelectedSubjectIndex_FirstOption_PopulatesBugReportSubject()
    {
        _viewModel.SelectedSubjectIndex = 0;

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.SelectedSubject, Is.EqualTo(ContactRequestSubjectTypes.BugReport));
            Assert.That(_viewModel.SelectedSubjectIndex, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task SubmitAsync_ValidRequest_ShowsPopupAndNavigatesBack()
    {
        _viewModel.SelectedSubject = ContactRequestSubjectTypes.AppSuggestion;
        _viewModel.Message = "Please add a compact player.";
        _mockContactApiService
            .Setup(service => service.SubmitContactRequestAsync(ContactRequestSubjectTypes.AppSuggestion, "Please add a compact player."))
            .ReturnsAsync(new ContactSubmitResult(true));

        await _viewModel.SubmitCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            _mockAlertService.Verify(
                service => service.DisplayAlertAsync(
                    "Message Sent",
                    "Your message was sent. Check your email for a copy of the message.",
                    "OK"),
                Times.Once);
            _mockNavigationService.Verify(service => service.GoBackAsync(), Times.Once);
            Assert.That(_viewModel.SelectedSubject, Is.Null);
            Assert.That(_viewModel.SelectedSubjectIndex, Is.EqualTo(-1));
            Assert.That(_viewModel.Message, Is.Empty);
            Assert.That(_viewModel.StatusMessage, Is.Null);
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
            _mockAlertService.Verify(service => service.DisplayAlertAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            _mockNavigationService.Verify(service => service.GoBackAsync(), Times.Never);
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