using System.Globalization;

namespace SiNet.Application.Billing;

/// <summary>
/// Israeli shekel display for Billing Preparation. Whole shekels unless agorot are present.
/// </summary>
public static class BillingMoneyFormatter
{
    public static string FormatShekels(decimal amount)
    {
        var rounded = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        if (rounded == decimal.Truncate(rounded))
            return "₪ " + decimal.Truncate(rounded).ToString("#,0", CultureInfo.InvariantCulture);
        return "₪ " + rounded.ToString("#,0.00", CultureInfo.InvariantCulture);
    }
}
