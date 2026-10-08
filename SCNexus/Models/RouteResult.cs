using SCNexus.Services;

namespace SCNexus.Models;

// A presentation row keeps the planner's original object for the existing actions.
public sealed record RouteResult(HaulingRoute? Direct, VoyagePlan? Plan)
{
    public IReadOnlyList<HaulingRoute> Trades => Plan?.Trades ?? (Direct is { } route ? new[] { route } : []);
    public string Commodity => string.Join(", ", Trades.Select(x => x.Commodity).Distinct());
    public string Origin => Plan?.Stops.FirstOrDefault()?.Terminal ?? Direct?.BuyAt ?? "";
    public string Destination => Plan?.Stops.LastOrDefault()?.Terminal ?? Direct?.SellAt ?? "";
    public string RoutePath => $"{Origin} → {Destination}";
    public string Systems => Plan is { } p ? string.Join(" → ", p.Stops.Select(x => x.System).Distinct()) : $"{Direct?.BuySystem} → {Direct?.SellSystem}";
    public decimal Profit => Plan?.Profit ?? Direct?.Profit ?? 0;
    public decimal Investment => Plan?.Investment ?? Direct?.Investment ?? 0;
    public decimal Revenue => Plan?.Revenue ?? Direct?.Revenue ?? 0;
    public decimal MarginPercent => Investment > 0 ? Profit / Investment * 100 : 0;
    public int Cargo => Plan?.PeakCargo ?? Direct?.Scu ?? 0;
    public int Capacity => Plan?.CapacityScu ?? Direct?.CargoScu ?? 0;
    public decimal FillPercent => Capacity > 0 ? Cargo * 100m / Capacity : 0;
    public string Load => $"{Cargo:N0} / {Capacity:N0}";
    public bool IsDangerous => Trades.Any(x => x.IsDangerous);
    public bool IsNqaRisk => !Trades.Any(x => x.IsPyroRoute) && Trades.Any(x => x.Risky);
    public int RiskRank => Trades.Any(x => x.IsPyroRoute) ? 2 : IsNqaRisk ? 1 : 0;
    public string RiskTitle => Trades.Any(x => x.IsPyroRoute)
        ? LocalizationService.T("Опасно")
        : IsNqaRisk
            ? LocalizationService.T("Повышенный риск")
            : LocalizationService.T("Стандартный");
    public string RiskDetail => Trades.Any(x => x.IsPyroRoute) ? "Pyro" : IsNqaRisk ? "NQA" : "";
    public bool HasRiskDetail => !string.IsNullOrEmpty(RiskDetail);
    public string Risk => LocalizationService.T(Trades.Any(x => x.IsPyroRoute) ? "⚠ ОПАСНО: Pyro" : Trades.Any(x => x.Risky) ? "⚠ Терминал NQA" : "Без Pyro и NQA");
    public DateTimeOffset UpdatedAt => Trades.Count > 0 ? Trades.Min(x => x.UpdatedAt) : DateTimeOffset.MinValue;
    public string Freshness => LocalizationService.IsEnglish ? $"UEX · Oldest quote: {UpdatedAt.LocalDateTime:g}" : $"UEX · Самая старая котировка: {UpdatedAt.LocalDateTime:g}";
    public string CostsNote => LocalizationService.IsEnglish ? "Fuel, travel time and fees are not included." : "Топливо, время перелёта и сборы не учтены.";
    public string Stops => Plan is { } p ? string.Join("\n\n", p.Stops.Select(x => $"{x.Heading}\n{LocalizationService.T(x.Action)}")) : $"1. {Origin} · {Direct?.BuySystem}\n{(LocalizationService.IsEnglish ? "Buy" : "Купить")} {Cargo:N0} SCU × {Direct?.BuyPrice:N0} aUEC\n\n2. {Destination} · {Direct?.SellSystem}\n{(LocalizationService.IsEnglish ? "Sell" : "Продать")} {Cargo:N0} SCU × {Direct?.SellPrice:N0} aUEC";
}
