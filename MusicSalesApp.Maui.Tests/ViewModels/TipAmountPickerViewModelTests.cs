using MusicSalesApp.Maui.ViewModels;

namespace MusicSalesApp.Maui.Tests.ViewModels;

[TestFixture]
public class TipAmountPickerViewModelTests
{
    [Test]
    public void Constructor_WithSongTitle_SetsTitleAndSubtitle()
    {
        var viewModel = new TipAmountPickerViewModel("Skyline Drive");

        Assert.That(viewModel.Title, Is.EqualTo("Tip Creator"));
        Assert.That(viewModel.Subtitle, Is.EqualTo("Skyline Drive"));
        Assert.That(viewModel.ShowCustomInput, Is.False);
    }

    [Test]
    public void RevealCustomInput_ShowsCustomSection_AndClearsError()
    {
        var viewModel = new TipAmountPickerViewModel("Test Song")
        {
            ErrorMessage = "Old error"
        };

        viewModel.RevealCustomInput();

        Assert.That(viewModel.ShowCustomInput, Is.True);
        Assert.That(viewModel.ErrorMessage, Is.Empty);
    }

    [Test]
    public void SelectPreset_RoundsAmount_AndClearsError()
    {
        var viewModel = new TipAmountPickerViewModel("Test Song")
        {
            ErrorMessage = "Old error"
        };

        var amount = viewModel.SelectPreset(5.129m);

        Assert.That(amount, Is.EqualTo(5.13m));
        Assert.That(viewModel.ErrorMessage, Is.Empty);
    }

    [Test]
    public void TryGetCustomAmount_WithInvalidAmount_ReturnsFalseAndSetsError()
    {
        var viewModel = new TipAmountPickerViewModel("Test Song")
        {
            CustomAmountText = "0.50"
        };

        var success = viewModel.TryGetCustomAmount(out var amount);

        Assert.That(success, Is.False);
        Assert.That(amount, Is.EqualTo(0m));
        Assert.That(viewModel.ErrorMessage, Does.Contain("between $1.00 and $50.00"));
    }

    [Test]
    public void TryGetCustomAmount_WithValidAmount_ReturnsRoundedAmount()
    {
        var viewModel = new TipAmountPickerViewModel("Test Song")
        {
            CustomAmountText = "12.345"
        };

        var success = viewModel.TryGetCustomAmount(out var amount);

        Assert.That(success, Is.True);
        Assert.That(amount, Is.EqualTo(12.35m));
        Assert.That(viewModel.ErrorMessage, Is.Empty);
    }
}