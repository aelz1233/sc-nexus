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
    public double? PersonalDurationMinutes { get; init; }
    public string PersonalDurationDisplay => PersonalDurationMinutes is { } minutes
        ? $"Обычно у тебя: {minutes:N0} мин" : "Твоего времени пока нет";
    public string ProfitDisplay => $"+{Profit:N0} aUEC";
    public string InvestmentDisplay => $"{Investment:N0} aUEC";
    public string RoiDisplay => $"{RoiPercent:N1}%";
    public string ScuDisplay => $"{Scu} SCU";
    public string FreshnessDisplay => $"Котировка: {QuoteUpdatedAt.LocalDateTime:dd.MM HH:mm}";
    public string RiskDisplay => Risky ? "NQA / риск" : "Риск не оценён";
}
