using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MusicSalesApp.Maui.ViewModels;

public partial class TipAmountPickerViewModel : ObservableObject
{
    public static readonly IReadOnlyList<decimal> PresetAmounts = [1.00m, 5.00m, 10.00m];

    [ObservableProperty]
    public partial string Title { get; set; }

    [ObservableProperty]
    public partial string Subtitle { get; set; }

    [ObservableProperty]
    public partial bool ShowCustomInput { get; set; }

    [ObservableProperty]
    public partial string CustomAmountText { get; set; } = "1.00";

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;

    public bool ShowCustomAmountButton => !ShowCustomInput;
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public TipAmountPickerViewModel(string songTitle)
    {
        Title = "Tip Creator";
        Subtitle = string.IsNullOrWhiteSpace(songTitle)
            ? "Tips are processed securely with PayPal."
            : songTitle;
    }

    public void RevealCustomInput()
    {
        ShowCustomInput = true;
        ErrorMessage = string.Empty;
        OnPropertyChanged(nameof(ShowCustomAmountButton));
    }

    public decimal SelectPreset(decimal amount)
    {
        ErrorMessage = string.Empty;
        return Math.Round(amount, 2, MidpointRounding.AwayFromZero);
    }

    public bool TryGetCustomAmount(out decimal amount)
    {
        amount = 0;

        if (!TryParseAmount(CustomAmountText, out var parsedAmount) || parsedAmount < 1.00m || parsedAmount > 50.00m)
        {
            ErrorMessage = "Please enter an amount between $1.00 and $50.00.";
            OnPropertyChanged(nameof(HasError));
            return false;
        }

        ErrorMessage = string.Empty;
        OnPropertyChanged(nameof(HasError));
        amount = Math.Round(parsedAmount, 2, MidpointRounding.AwayFromZero);
        return true;
    }

    private static bool TryParseAmount(string? rawAmount, out decimal amount)
    {
        if (string.IsNullOrWhiteSpace(rawAmount))
        {
            amount = 0;
            return false;
        }

        return decimal.TryParse(rawAmount, NumberStyles.Number, CultureInfo.CurrentCulture, out amount)
            || decimal.TryParse(rawAmount, NumberStyles.Number, CultureInfo.InvariantCulture, out amount);
    }
}