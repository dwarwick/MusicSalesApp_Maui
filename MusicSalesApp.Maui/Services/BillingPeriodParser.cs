namespace MusicSalesApp.Maui.Services;

public static class BillingPeriodParser
{
    public static int? ParseIso8601PeriodDays(string? billingPeriod)
    {
        if (string.IsNullOrWhiteSpace(billingPeriod) || char.ToUpperInvariant(billingPeriod[0]) != 'P')
        {
            return null;
        }

        var years = 0;
        var months = 0;
        var weeks = 0;
        var days = 0;
        var hasComponent = false;
        var index = 1;

        while (index < billingPeriod.Length)
        {
            var valueStart = index;
            while (index < billingPeriod.Length && char.IsDigit(billingPeriod[index]))
            {
                index++;
            }

            if (valueStart == index || index >= billingPeriod.Length || !int.TryParse(billingPeriod[valueStart..index], out var value))
            {
                return null;
            }

            switch (char.ToUpperInvariant(billingPeriod[index]))
            {
                case 'Y':
                    years = value;
                    break;
                case 'M':
                    months = value;
                    break;
                case 'W':
                    weeks = value;
                    break;
                case 'D':
                    days = value;
                    break;
                default:
                    return null;
            }

            hasComponent = true;
            index++;
        }

        if (!hasComponent)
        {
            return null;
        }

        var totalDays = (years * 365L) + (months * 30L) + (weeks * 7L) + days;
        return totalDays > int.MaxValue ? null : (int)totalDays;
    }
}