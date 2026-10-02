namespace SCNexus.Models;

public sealed record TerminalOption(int Id, string Name, string System)
{
    public string Display => Id == 0 ? "Автоподбор конечной точки" : $"{System} · {Name}";
    public override string ToString() => Display;
}

public sealed record VoyageStop(int Number, string Terminal, string System, string Action,
    int CargoScu, int CapacityScu, decimal Cash)
{
    public string Heading => $"{Number}. {Terminal} · {System}";
    public string LoadDisplay => $"Трюм: {CargoScu:N0} / {CapacityScu:N0} SCU · свободно {CapacityScu - CargoScu:N0}";
    public string CashDisplay => $"Доступно после остановки: {Cash:N0} aUEC";
    public bool IsPyro => System.Equals("Pyro", StringComparison.OrdinalIgnoreCase);
}

public sealed record VoyagePlan(string Mode, IReadOnlyList<HaulingRoute> Trades,
    IReadOnlyList<VoyageStop> Stops, decimal StartingBudget, int CapacityScu)
{
    public decimal Investment => Trades.Sum(x => x.Investment);
    public decimal Revenue => Trades.Sum(x => x.Revenue);
    public decimal Profit => Revenue - Investment;
    public int PeakCargo => Stops.Max(x => x.CargoScu);
    public decimal FillPercent => CapacityScu <= 0 ? 0 : 100m * PeakCargo / CapacityScu;
    public string Title => $"{Mode} · остановок: {Stops.Count}";
    public string PathDisplay => string.Join(" → ", Stops.Select(x => $"{x.Terminal} ({x.System})"));
    public string ProfitDisplay => $"+{Profit:N0} aUEC";
    public string Summary => $"Макс. загрузка {PeakCargo:N0} / {CapacityScu:N0} SCU ({FillPercent:N0}%) · закупки всего {Investment:N0} aUEC";
    public string RiskDisplay => Trades.Any(x => x.IsPyroRoute) ? "⚠ ОПАСНО: маршрут через Pyro" : Trades.Any(x => x.Risky) ? "⚠ Терминалы NQA" : "Без Pyro и NQA";
    public string QuoteDisplay => $"Самая старая котировка: {Trades.Min(x => x.UpdatedAt).LocalDateTime:dd.MM.yyyy HH:mm}";
    public string Manifest => string.Join(Environment.NewLine, Stops.Select(x => $"{x.Heading}\n{x.Action}\n{x.LoadDisplay} · {x.CashDisplay}"));
    public string ExportText => $"SC NEXUS — {Title}\n{PathDisplay}\n{Summary}\nОжидаемая прибыль: {ProfitDisplay}\n{RiskDisplay}\n{QuoteDisplay}\n\n{Manifest}\n\nПроверь цены и наличие в игре. Время, топливо и стоимость перелётов не включены.\n";
}

public sealed record VoyageGuidanceState(VoyagePlan Plan, int StopIndex, bool Completed,
    DateTimeOffset StartedAtUtc);
