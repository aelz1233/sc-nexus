using System.Globalization;
using System.Text.RegularExpressions;

namespace SCNexus.Services;

internal static class BalanceText
{
    internal static bool TryParse(string text, out decimal amount)
    {
        amount = 0;
        var compact = string.Concat(text.Where(c => !char.IsWhiteSpace(c)));
        if (Regex.IsMatch(compact, @"^\d+([,.])\d{3}(?:\1\d{3})*$"))
            compact = compact.Replace(",", "").Replace(".", "");
        else if (Regex.IsMatch(compact, @"^\d{1,3}(?:,\d{3})+\.\d{1,2}$")) compact = compact.Replace(",", "");
        else if (Regex.IsMatch(compact, @"^\d{1,3}(?:\.\d{3})+,\d{1,2}$")) compact = compact.Replace(".", "").Replace(',', '.');
        else if (Regex.IsMatch(compact, @"^\d+(?:[,.]\d{1,2})?$")) compact = compact.Replace(',', '.');
        else return false;
        return decimal.TryParse(compact, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount) && amount >= 0;
    }
}
