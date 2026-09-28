using SCNexus.Models;

namespace SCNexus.Services;

public sealed class HaulingService
{
    public static readonly string[] SortOptions =
        ["За рейс", "За SCU", "Маржа", "Вход", "Заполнение"];
    public static readonly string[] Categories =
        ["Все маршруты", "Местный", "Планетарный", "Звёздный", "Межзвёздный"];

    public IReadOnlyList<HaulingRoute> Calculate(DataSnapshot data, int cargoScu, decimal budget,
        bool allowRisky, bool sameSystemOnly, string sortMode, string category = "Все маршруты",
        string? startLocation = null, string? startSystem = null,
        bool avoidPyro = false, int minimumFillPercent = 0, decimal minimumProfit = 0,
        int limit = int.MaxValue, IReadOnlySet<string>? allowedSystems = null)
    {
        if (cargoScu <= 0 || budget <= 0) return [];
        var terminals = data.Terminals.Where(x => x.Type == "commodity" && x.IsAvailableLive == 1)
            .Where(x => allowedSystems is null || allowedSystems.Contains(x.StarSystemName ?? "Неизвестно"))
            .ToDictionary(x => x.Id);
        var sellers = data.Quotes.Where(x => x.PriceSell > 0 && x.ScuSell > 0 && x.StatusSell != 1 && terminals.ContainsKey(x.IdTerminal))
            .GroupBy(x => x.IdCommodity).ToDictionary(x => x.Key, x => x.ToArray());
        var routes = new List<HaulingRoute>();
        foreach (var buy in data.Quotes)
        {
            if (buy.PriceBuy <= 0 || buy.ScuBuy <= 0 || buy.StatusBuy == 1 ||
                !terminals.TryGetValue(buy.IdTerminal, out var origin) ||
                (!string.IsNullOrWhiteSpace(startLocation) && startLocation != "Не указана" &&
                 (!origin.MatchesLocation(startLocation) ||
                  (!string.IsNullOrWhiteSpace(startSystem) &&
                   !string.Equals(origin.StarSystemName, startSystem, StringComparison.OrdinalIgnoreCase)))) ||
                !sellers.TryGetValue(buy.IdCommodity, out var destinations)) continue;
            var affordable = (int)Math.Min(int.MaxValue, Math.Floor(budget / buy.PriceBuy));
            var stock = (int)Math.Min(int.MaxValue, Math.Floor(buy.ScuBuy));
            foreach (var sell in destinations)
            {
                var destination = terminals[sell.IdTerminal];
                if (destination.Id == origin.Id || destination.Location.Equals(origin.Location, StringComparison.OrdinalIgnoreCase) ||
                    sell.PriceSell <= buy.PriceBuy ||
                    (!allowRisky && (origin.IsNqa == 1 || destination.IsNqa == 1))) continue;
                var originSystem = origin.StarSystemName ?? "Неизвестно";
                var destinationSystem = destination.StarSystemName ?? "Неизвестно";
                if (sameSystemOnly && !originSystem.Equals(destinationSystem, StringComparison.OrdinalIgnoreCase)) continue;
                if (avoidPyro && (originSystem.Equals("Pyro", StringComparison.OrdinalIgnoreCase) ||
                                  destinationSystem.Equals("Pyro", StringComparison.OrdinalIgnoreCase))) continue;
                var demand = (int)Math.Min(int.MaxValue, Math.Floor(sell.ScuSell));
                var scu = Math.Min(Math.Min(cargoScu, affordable), Math.Min(stock, demand));
                if (scu <= 0) continue;
                if ((decimal)scu / cargoScu * 100 < minimumFillPercent ||
                    scu * (sell.PriceSell - buy.PriceBuy) < minimumProfit) continue;
                DateTimeOffset updated;
                try { updated = DateTimeOffset.FromUnixTimeSeconds(Math.Min(buy.DateModified, sell.DateModified)); }
                catch (ArgumentOutOfRangeException) { updated = data.PricesFetchedAt; }
                var routeCategory = !originSystem.Equals(destinationSystem, StringComparison.OrdinalIgnoreCase)
                    ? "Межзвёздный" :
                    !string.IsNullOrWhiteSpace(origin.MoonName) && origin.MoonName.Equals(destination.MoonName, StringComparison.OrdinalIgnoreCase)
                        ? "Местный" :
                    !string.IsNullOrWhiteSpace(origin.PlanetName) && origin.PlanetName.Equals(destination.PlanetName, StringComparison.OrdinalIgnoreCase)
                        ? "Планетарный" : "Звёздный";
                if (category != "Все маршруты" && category != routeCategory) continue;
                routes.Add(new HaulingRoute(buy.CommodityName, origin.Name, destination.Name,
                    originSystem, destinationSystem, scu, cargoScu, buy.PriceBuy, sell.PriceSell,
                    buy.ScuBuy, sell.ScuSell, scu * buy.PriceBuy, scu * sell.PriceSell,
                    origin.IsNqa == 1 || destination.IsNqa == 1, routeCategory, updated)
                    { BuyTerminalId = origin.Id, SellTerminalId = destination.Id, CommodityId = buy.IdCommodity });
            }
        }
        IOrderedEnumerable<HaulingRoute> sorted = sortMode switch
        {
            "За SCU" => routes.OrderByDescending(x => x.ProfitPerScu),
            "Маржа" => routes.OrderByDescending(x => x.RoiPercent),
            "Вход" => routes.OrderBy(x => x.Investment),
            "Заполнение" => routes.OrderByDescending(x => x.FillPercent),
            _ => routes.OrderByDescending(x => x.Profit)
        };
        return sorted.ThenByDescending(x => x.Profit).Take(limit).ToArray();
    }
}
