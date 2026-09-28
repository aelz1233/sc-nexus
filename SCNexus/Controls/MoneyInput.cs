using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;

namespace SCNexus.Controls;

public static class MoneyText
{
    public static bool TryParse(string? text, out decimal value)
    {
        var normalized = string.Concat((text ?? "").Where(c => !char.IsWhiteSpace(c))).Replace(',', '.');
        return decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value)
            && value >= 0;
    }
}

public sealed class MoneyConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is decimal number ? number.ToString(number == decimal.Truncate(number) ? "N0" : "N2", CultureInfo.GetCultureInfo("ru-RU")) : "";
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        MoneyText.TryParse(value?.ToString(), out var number) ? number : Binding.DoNothing;
}

public sealed class MoneyRule : ValidationRule
{
    public override ValidationResult Validate(object value, CultureInfo cultureInfo) =>
        MoneyText.TryParse(value?.ToString(), out _) ? ValidationResult.ValidResult :
            new ValidationResult(false, "Введи сумму от 0. Например: 25 000 000 или 150,50.");
}
