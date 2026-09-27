using System;
using System.Globalization;

namespace CardStatement.Core.Banks.Bac;

internal static class BacAmountParser
{
    public static bool TryParse(string token, out decimal amount)
    {
        var cleaned = token.Replace(",", "", StringComparison.Ordinal);
        return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out amount);
    }

    public static decimal Parse(string token)
    {
        if (!TryParse(token, out var amount))
            throw new FormatException($"Unrecognized amount: '{token}'");
        return amount;
    }
}
