using System.Globalization;

namespace Noted;

internal static class SpreadsheetAmountHelpers
{
    internal const string DefaultCurrency = "SEK";

    /// <summary>For syntax highlighting: numeric part of a cell (drops trailing <c> kr</c> or <c> CUR</c>).</summary>
    internal static string TrimTrailingCurrencyToken(string s)
    {
        s = StripLegacyCellSuffix(s).Trim();
        int ls = s.LastIndexOf(' ');
        if (ls > 0 && ls < s.Length - 1)
        {
            var left = s[..ls].TrimEnd();
            if (decimal.TryParse(left, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
                return left;
        }

        return s;
    }

    /// <summary>Legacy cell suffix from older saves.</summary>
    internal static string StripLegacyCellSuffix(string s)
    {
        var t = s.Trim();
        if (t.EndsWith(" kr", StringComparison.OrdinalIgnoreCase))
            return t[..^3].TrimEnd();
        return t;
    }

    internal static bool TryParseDecimal(string s, out decimal value)
    {
        s = s.Trim();
        if (decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out value))
            return true;

        s = StripLegacyCellSuffix(s);
        if (decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out value))
            return true;

        // Sum column text like "1234.5 SEK"
        int lastSpace = s.LastIndexOf(' ');
        if (lastSpace > 0 && lastSpace < s.Length - 1)
        {
            var numPart = s[..lastSpace].TrimEnd();
            if (decimal.TryParse(numPart, NumberStyles.Number, CultureInfo.InvariantCulture, out value))
                return true;
        }

        value = default;
        return false;
    }

    /// <summary>Whole numbers without ".00"; fractional uses up to 2 decimals without trailing zeros.</summary>
    internal static string FormatNumber(decimal value)
    {
        var inv = CultureInfo.InvariantCulture;
        if (value == decimal.Truncate(value))
            return decimal.Truncate(value).ToString("0", inv);
        return value.ToString("0.##", inv);
    }

    /// <summary>
    /// Unit price / line total / sum: whole values stay plain (<c>75</c>); fractional values use exactly two decimals (<c>300.50</c>).
    /// </summary>
    internal static string FormatMoneyAmount(decimal value)
    {
        var inv = CultureInfo.InvariantCulture;
        decimal rounded = decimal.Round(value, 2, MidpointRounding.AwayFromZero);
        if (rounded == decimal.Truncate(rounded))
            return decimal.Truncate(rounded).ToString("0", inv);
        return rounded.ToString("0.00", inv);
    }

    /// <summary>Amount + space + currency (used on Sum row in the document only).</summary>
    internal static string FormatSumWithCurrency(decimal amount, string currency)
    {
        currency = string.IsNullOrWhiteSpace(currency) ? DefaultCurrency : currency.Trim();
        return FormatMoneyAmount(amount) + " " + currency;
    }

    internal static bool TrySafeMultiply(decimal a, decimal b, out decimal product)
    {
        try
        {
            product = decimal.Multiply(a, b);
            return true;
        }
        catch (OverflowException)
        {
            product = default;
            return false;
        }
    }

    internal static bool TrySafeAdd(decimal a, decimal b, out decimal sum)
    {
        try
        {
            sum = decimal.Add(a, b);
            return true;
        }
        catch (OverflowException)
        {
            sum = default;
            return false;
        }
    }
}
