using SCNexus.Models;

namespace SCNexus.Services;

public sealed class RouteService
{
    public IReadOnlyList<TradeRoute> FindRoutes(DataSnapshot data, PersonalSettings settings)
    {
        if (settings.CargoScu <= 0 || settings.Balance <= settings.Reserve ||
            string.IsNullOrWhiteSpace(settings.CurrentLocation) || settings.CurrentLocation == "Не указана")
            return [];

        var terminals = data.Terminals
            .Where(x => x.Type == "commodity" && x.IsAvailableLive == 1)
            .ToDictionary(x => x.Id);
        var sellQuotes = data.Quotes
            .Where(x => x.PriceSell > 0 && terminals.ContainsKey(x.IdTerminal))
            .GroupBy(x => x.IdCommodity)
            .ToDictionary(x => x.Key, x => x.ToArray());
        var routes = new List<TradeRoute>();
        foreach (var buy in data.Quotes)
        {
            if (buy.PriceBuy <= 0 || buy.StatusBuy == 1 || !terminals.TryGetValue(buy.IdTerminal, out var origin) ||
                !origin.MatchesLocation(settings.CurrentLocation) ||
                (!string.IsNullOrWhiteSpace(settings.CurrentSystem) &&
                 !string.Equals(origin.StarSystemName, settings.CurrentSystem, StringComparison.OrdinalIgnoreCase)) ||
                !sellQuotes.TryGetValue(buy.IdCommodity, out var destinations))
                continue;

            var affordable = (int)Math.Min(int.MaxValue, Math.Floor((settings.Balance - settings.Reserve) / buy.PriceBuy));
            var originStock = buy.ScuBuy > 0 ? (int)Math.Floor(buy.ScuBuy) : 0;
            foreach (var sell in destinations)
            {
                var destination = terminals[sell.IdTerminal];
                if (!settings.AllowRisky && (origin.IsNqa == 1 || destination.IsNqa == 1)) continue;
                if (destination.Id == origin.Id || destination.Location.Equals(origin.Location, StringComparison.OrdinalIgnoreCase) ||
                    sell.PriceSell <= buy.PriceBuy) continue;
                var demand = sell.ScuSell > 0 ? (int)Math.Floor(sell.ScuSell) : 0;
                var scu = Math.Min(Math.Min(settings.CargoScu, affordable), Math.Min(originStock, demand));
                if (scu <= 0) continue;
                var investment = scu * buy.PriceBuy;
                var revenue = scu * sell.PriceSell;
                var quoteTime = DateTimeOffset.FromUnixTimeSeconds(Math.Min(buy.DateModified, sell.DateModified));
                routes.Add(new TradeRoute(buy.CommodityName, origin.Name, destination.Name, scu,
                    investment, revenue, revenue - investment, (revenue - investment) / investment * 100,
                    quoteTime, true, origin.IsNqa == 1 || destination.IsNqa == 1));
            }
        }
        return routes.OrderByDescending(x => x.Profit).ThenByDescending(x => x.RoiPercent).Take(5).ToArray();
    }
}
