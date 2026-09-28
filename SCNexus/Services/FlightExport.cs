using System.Globalization;
using SCNexus.Models;

namespace SCNexus.Services;

public static class FlightExport
{
    private static string Cell(string value)
    {
        if (value.TrimStart().StartsWith('=') || value.TrimStart().StartsWith('+') || value.TrimStart().StartsWith('-') || value.TrimStart().StartsWith('@')) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
    public static string ToCsv(IEnumerable<FlightRecord> flights)
    {
        var lines = new List<string> { "Дата;Корабль;Откуда;Куда;Товар;Вложено;Получено;Расходы;Потери;Прибыль;Статус" };
        foreach (var f in flights)
            lines.Add(string.Join(';', Cell(f.DateDisplay), Cell(f.ShipName), Cell(f.Origin), Cell(f.Destination), Cell(f.Commodity),
                f.Investment.ToString(CultureInfo.GetCultureInfo("ru-RU")), f.Revenue.ToString(CultureInfo.GetCultureInfo("ru-RU")),
                f.Expenses.ToString(CultureInfo.GetCultureInfo("ru-RU")), f.Losses.ToString(CultureInfo.GetCultureInfo("ru-RU")),
                f.EndedAtUtc is null ? "" : f.Profit.ToString(CultureInfo.GetCultureInfo("ru-RU")), f.EndedAtUtc is null ? "В пути" : "Завершён"));
        return string.Join(Environment.NewLine, lines);
    }
}
