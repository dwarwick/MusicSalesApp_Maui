using MusicSalesApp.Common.Helpers;

namespace MusicSalesApp.Maui.ViewModels;

public sealed record SubscriptionOfferDisplay(
    string Title,
    string Body,
    string PriceText,
    string DisclosureText);

public static class SubscriptionOfferDisplayBuilder
{
    public const int DefaultFreeTrialDays = 3;

    public static SubscriptionOfferDisplay Create(
        bool showFreeTrialTerms,
        int? freeTrialDays,
        string? renewalPrice,
        string fallbackPrice,
        bool allowFallbackPrice = true)
    {
        var priceText = FormatMonthlyPriceOrEmpty(renewalPrice, fallbackPrice, allowFallbackPrice);
        var monthlyPriceText = string.IsNullOrWhiteSpace(priceText)
            ? "the monthly price shown in Google Play"
            : $"{priceText}/month";
        if (showFreeTrialTerms)
        {
            var days = freeTrialDays.GetValueOrDefault(DefaultFreeTrialDays);
            return new SubscriptionOfferDisplay(
                $"Start your {days} day free trial today!",
                $"After {days} days, your subscription will automatically renew at {monthlyPriceText}. You can cancel anytime in your Google Play subscription settings.",
                priceText,
                "Full subscription benefits are included during the trial.");
        }

        return new SubscriptionOfferDisplay(
            "Subscribe for unlimited music",
            $"Stream full songs, create playlists, and listen without the preview limit for {monthlyPriceText}.",
            priceText,
            "Subscription automatically renews monthly. You can cancel anytime in your Google Play subscription settings.");
    }

    public static string FormatMonthlyPrice(string? renewalPrice, string fallbackPrice)
        => CurrencyDisplayHelper.FormatCurrencyText(renewalPrice, string.IsNullOrWhiteSpace(fallbackPrice) ? "3.99" : fallbackPrice);

    public static string FormatMonthlyPriceOrEmpty(string? renewalPrice, string fallbackPrice, bool allowFallbackPrice)
    {
        if (!allowFallbackPrice && string.IsNullOrWhiteSpace(renewalPrice))
        {
            return string.Empty;
        }

        return FormatMonthlyPrice(renewalPrice, fallbackPrice);
    }
}
