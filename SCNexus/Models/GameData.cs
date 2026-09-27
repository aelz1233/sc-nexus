using System.Text.Json.Serialization;

namespace SCNexus.Models;

public sealed class UexResponse<T>
{
    public string Status { get; set; } = "";
    public List<T> Data { get; set; } = [];
}

public sealed class CommodityQuote
{
    public int IdCommodity { get; set; }
    public int IdTerminal { get; set; }
    public string CommodityName { get; set; } = "";
    public decimal PriceBuy { get; set; }
    public decimal PriceSell { get; set; }
    public decimal ScuBuy { get; set; }
    public decimal ScuSell { get; set; }
    public int? StatusBuy { get; set; }
    public int? StatusSell { get; set; }
    public long DateModified { get; set; }
}

public sealed class TradeTerminal
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Displayname { get; set; } = "";
    public string? CityName { get; set; }
    public string? OutpostName { get; set; }
    public string? PoiName { get; set; }
    public string? SpaceStationName { get; set; }
    public string? MoonName { get; set; }
    public string? PlanetName { get; set; }
    public string? StarSystemName { get; set; }
    public string Type { get; set; } = "";
    public int IsAvailableLive { get; set; }
    public int IsNqa { get; set; }

    [JsonIgnore]
    public string Location => First(CityName, OutpostName, PoiName, SpaceStationName, MoonName, PlanetName, Displayname, Name);

    private static string First(params string?[] values) => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "Неизвестно";

    public bool MatchesLocation(string location) =>
        new[] { CityName, OutpostName, PoiName, SpaceStationName, MoonName, PlanetName, Displayname, Name }
            .Any(x => !string.IsNullOrWhiteSpace(x) && x.Contains(location.Trim(), StringComparison.OrdinalIgnoreCase));
}

public sealed record DataSnapshot(
    IReadOnlyList<CommodityQuote> Quotes,
    IReadOnlyList<TradeTerminal> Terminals,
    DateTimeOffset PricesFetchedAt,
    DateTimeOffset TerminalsFetchedAt,
    bool UsedOldCache);

public sealed record TradeRoute(
    string Commodity, string BuyAt, string SellAt, int Scu,
    decimal Investment, decimal Revenue, decimal Profit, decimal RoiPercent,
    DateTimeOffset QuoteUpdatedAt, bool DemandEstimate, bool Risky)
{
    public string BuySystem { get; init; } = "Неизвестно";
    public string SellSystem { get; init; } = "Неизвестно";
    public int CargoCapacityScu { get; init; }
    public int OriginStockScu { get; init; }
    public int DestinationDemandScu { get; init; }
    public decimal BuyPricePerScu { get; init; }
    public decimal SellPricePerScu { get; init; }
    public bool UsesNqaTerminal { get; init; }
    public bool IsPyroRoute => BuySystem.Equals("Pyro", StringComparison.OrdinalIgnoreCase) ||
        SellSystem.Equals("Pyro", StringComparison.OrdinalIgnoreCase);
    public decimal FillPercent => CargoCapacityScu <= 0 ? 0 : (decimal)Scu / CargoCapacityScu * 100;
    public double? PersonalDurationMinutes { get; init; }
    public string PersonalDurationDisplay => PersonalDurationMinutes is { } minutes
        ? $"Обычно у тебя: {minutes:N0} мин" : "Твоего времени пока нет";
    public string ProfitDisplay => $"+{Profit:N0} aUEC";
    public string InvestmentDisplay => $"{Investment:N0} aUEC";
    public string RoiDisplay => $"{RoiPercent:N1}%";
    public string ScuDisplay => $"{Scu} SCU";
    public string CargoDisplay => $"{Scu:N0} / {CargoCapacityScu:N0} SCU · заполнено {FillPercent:N0}%";
    public string StockDemandDisplay => $"Запас: {OriginStockScu:N0} SCU · спрос: {DestinationDemandScu:N0} SCU";
    public string PricesDisplay => $"Покупка: {BuyPricePerScu:N0} / SCU · продажа: {SellPricePerScu:N0} / SCU";
    public string RevenueDisplay => $"{Revenue:N0} aUEC";
    public string BuySystemDisplay => $"Система: {BuySystem}";
    public string SellSystemDisplay => $"Система: {SellSystem}";
    public string FreshnessDisplay => $"Котировка: {QuoteUpdatedAt.LocalDateTime:dd.MM HH:mm}";
    public string RiskDisplay => IsPyroRoute
        ? UsesNqaTerminal ? "⚠ ОПАСНО: Pyro · NQA" : "⚠ ОПАСНО: Pyro"
        : UsesNqaTerminal ? "⚠ Терминал NQA" : "Риск не оценён";
    public string RiskColor => IsPyroRoute || UsesNqaTerminal ? "#FF9A8F" : "#8D9AB5";
}
