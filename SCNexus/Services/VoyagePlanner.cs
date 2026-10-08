using SCNexus.Models;

namespace SCNexus.Services;

public sealed record VoyageRequest(string Mode, int Capacity, decimal Budget, int MaxPurchases = 3,
    int DestinationId = 0, string? StartLocation = null, string? StartSystem = null,
    bool AllowRisky = false, bool AvoidPyro = false, bool SameSystemOnly = false,
    int MinimumFill = 0, decimal MinimumProfit = 0, string Category = "Все маршруты",
    IReadOnlySet<string>? AllowedSystems = null, bool ExcludeDangerousRoutes = false);

/// <summary>Bounded search over reported quotes. No distance or travel-time assumptions.</summary>
public sealed class VoyagePlanner
{
    public IReadOnlyList<VoyagePlan> Calculate(DataSnapshot data, VoyageRequest request, CancellationToken token = default)
    {
        if (request.Capacity <= 0 || request.Budget <= 0) return [];
        var searchBudget = Math.Max(request.Budget, data.Quotes.Select(x => x.PriceBuy).DefaultIfEmpty().Max() * request.Capacity);
        var edges = new HaulingService().Calculate(data, request.Capacity, searchBudget,
            request.AllowRisky && !request.ExcludeDangerousRoutes, request.SameSystemOnly, "За рейс", request.Category,
            avoidPyro: request.AvoidPyro || request.ExcludeDangerousRoutes, allowedSystems: request.AllowedSystems)
            .Where(x => !request.ExcludeDangerousRoutes || !x.IsDangerous).ToArray();
        var starts = data.Terminals.Where(x =>
                (string.IsNullOrWhiteSpace(request.StartLocation) || request.StartLocation == "Не указана" ||
                 x.MatchesLocation(request.StartLocation)) &&
                (string.IsNullOrWhiteSpace(request.StartSystem) ||
                 string.Equals(x.StarSystemName, request.StartSystem, StringComparison.OrdinalIgnoreCase)))
            .Select(x => x.Id).ToHashSet();
        var max = Math.Clamp(request.MaxPurchases, 2, 5);
        var plans = request.Mode == "Сбор груза"
            ? Collect(edges, request, starts, max, token)
            : Chain(edges, request, starts, max, token);
        return plans.Where(x => x.Profit >= request.MinimumProfit && x.FillPercent >= request.MinimumFill)
            .OrderByDescending(x => x.Profit).ThenBy(x => x.Stops.Count)
            .DistinctBy(x => string.Join(";", x.Trades.Select(t => $"{t.BuyTerminalId}/{t.SellTerminalId}/{t.CommodityId}/{t.Scu}")))
            .Take(12).ToArray();
    }

    private static HaulingRoute Resize(HaulingRoute route, int scu) => route with
    { Scu = scu, Investment = scu * route.BuyPrice, Revenue = scu * route.SellPrice };

    private static int Quantity(decimal budget, decimal price, int capacity, decimal stock, decimal demand) =>
        (int)Math.Max(0, Math.Min(capacity, Math.Min(Math.Floor(budget / price), Math.Min(Math.Floor(stock), Math.Floor(demand)))));

    private sealed record ChainState(List<HaulingRoute> Trades, HashSet<int> Visited, decimal Cash);

    private static List<VoyagePlan> Chain(HaulingRoute[] edges, VoyageRequest r, HashSet<int> starts, int max, CancellationToken token)
    {
        var outgoing = edges.GroupBy(x => x.BuyTerminalId).ToDictionary(x => x.Key, x => x.ToArray());
        var frontier = edges.Where(x => starts.Contains(x.BuyTerminalId))
            .Select(x => Resize(x, Quantity(r.Budget, x.BuyPrice, r.Capacity, x.Stock, x.Demand)))
            .Where(x => x.Scu > 0 && x.FillPercent >= r.MinimumFill)
            .Select(x => new ChainState([x], [x.BuyTerminalId, x.SellTerminalId], r.Budget + x.Profit))
            .OrderByDescending(x => x.Cash).Take(160).ToList();
        var results = new List<VoyagePlan>();
        for (var depth = 2; depth <= max; depth++)
        {
            var next = new List<ChainState>();
            foreach (var state in frontier)
            {
                token.ThrowIfCancellationRequested();
                var last = state.Trades[^1];
                if (r.DestinationId != 0 && last.SellTerminalId == r.DestinationId) continue;
                if (!outgoing.TryGetValue(last.SellTerminalId, out var options)) continue;
                foreach (var edge in options)
                {
                    if (state.Visited.Contains(edge.SellTerminalId)) continue;
                    var scu = Quantity(state.Cash, edge.BuyPrice, r.Capacity, edge.Stock, edge.Demand);
                    if (scu <= 0 || 100m * scu / r.Capacity < r.MinimumFill) continue;
                    var trade = Resize(edge, scu);
                    var candidate = new ChainState([.. state.Trades, trade], [.. state.Visited, trade.SellTerminalId], state.Cash + trade.Profit);
                    next.Add(candidate);
                    if (r.DestinationId == 0 || trade.SellTerminalId == r.DestinationId)
                        results.Add(CreateChain(candidate.Trades, r));
                }
            }
            frontier = next.OrderByDescending(x => x.Cash).Take(160).ToList();
            // Bound memory while retaining the strongest completed plans.
            results = results.OrderByDescending(x => x.Profit).Take(160).ToList();
        }
        return results;
    }

    private static VoyagePlan CreateChain(List<HaulingRoute> trades, VoyageRequest r)
    {
        var stops = new List<VoyageStop>();
        var cash = r.Budget;
        for (var i = 0; i < trades.Count; i++)
        {
            var current = trades[i];
            var action = "";
            if (i > 0)
            {
                var previous = trades[i - 1];
                cash += previous.Revenue;
                action = $"Продать {previous.Commodity}: {previous.Scu:N0} SCU за {previous.Revenue:N0} aUEC.\n";
            }
            cash -= current.Investment;
            action += $"Купить {current.Commodity}: {current.Scu:N0} SCU за {current.Investment:N0} aUEC.";
            stops.Add(new(i + 1, current.BuyAt, current.BuySystem, action, current.Scu, r.Capacity, cash));
        }
        var last = trades[^1];
        stops.Add(new(stops.Count + 1, last.SellAt, last.SellSystem,
            $"Продать {last.Commodity}: {last.Scu:N0} SCU за {last.Revenue:N0} aUEC.", 0, r.Capacity, cash + last.Revenue));
        return new("Цепочка", trades, stops, r.Budget, r.Capacity);
    }

    private static List<VoyagePlan> Collect(HaulingRoute[] edges, VoyageRequest r, HashSet<int> starts, int max, CancellationToken token)
    {
        var plans = new List<VoyagePlan>();
        foreach (var destination in edges.Where(x => r.DestinationId == 0 || x.SellTerminalId == r.DestinationId).GroupBy(x => x.SellTerminalId))
        {
            token.ThrowIfCancellationRequested();
            // Try different first stops and two rankings: margin per SCU and return on budget.
            foreach (var seed in destination.Where(x => starts.Contains(x.BuyTerminalId)).OrderByDescending(x => x.Profit).DistinctBy(x => x.BuyTerminalId).Take(24))
            foreach (var byRoi in new[] { false, true })
            {
                var choices = (byRoi ? destination.OrderByDescending(x => x.RoiPercent) : destination.OrderByDescending(x => x.ProfitPerScu)).ToArray();
                var trades = new List<HaulingRoute>();
                var vendors = new List<int> { seed.BuyTerminalId };
                var cash = r.Budget;
                var free = r.Capacity;
                var usedDemand = new Dictionary<int, int>();
                var usedStock = new Dictionary<(int, int), int>();
                var nextUnitPrice = choices.Where(x => x.BuyTerminalId != seed.BuyTerminalId).Select(x => x.BuyPrice).DefaultIfEmpty(r.Budget).Min();
                void Buy(HaulingRoute edge)
                {
                    var key = (edge.BuyTerminalId, edge.CommodityId);
                    var reserveForNext = edge.BuyTerminalId == seed.BuyTerminalId;
                    var demandReserve = reserveForNext && choices.Any(x => x.BuyTerminalId != seed.BuyTerminalId && x.CommodityId == edge.CommodityId) ? 1 : 0;
                    var scu = Quantity(Math.Max(0, cash - (reserveForNext ? nextUnitPrice : 0)), edge.BuyPrice, Math.Max(0, free - (reserveForNext ? 1 : 0)),
                        edge.Stock - usedStock.GetValueOrDefault(key), edge.Demand - usedDemand.GetValueOrDefault(edge.CommodityId) - demandReserve);
                    if (scu <= 0) return;
                    trades.Add(Resize(edge, scu));
                    usedDemand[edge.CommodityId] = usedDemand.GetValueOrDefault(edge.CommodityId) + scu;
                    usedStock[key] = usedStock.GetValueOrDefault(key) + scu;
                    free -= scu;
                    cash -= scu * edge.BuyPrice;
                    if (!vendors.Contains(edge.BuyTerminalId)) vendors.Add(edge.BuyTerminalId);
                }
                foreach (var edge in choices.Where(x => x.BuyTerminalId == seed.BuyTerminalId)) Buy(edge);
                if (trades.Count == 0) continue;
                foreach (var edge in choices.Where(x => x.BuyTerminalId != seed.BuyTerminalId))
                    if (vendors.Contains(edge.BuyTerminalId) || vendors.Count < max) Buy(edge);
                if (vendors.Count < 2 || trades.Count == 0) continue;
                var stops = new List<VoyageStop>();
                var load = 0;
                cash = r.Budget;
                foreach (var vendor in vendors)
                {
                    var cargo = trades.Where(x => x.BuyTerminalId == vendor).ToArray();
                    if (cargo.Length == 0) continue;
                    load += cargo.Sum(x => x.Scu);
                    cash -= cargo.Sum(x => x.Investment);
                    stops.Add(new(stops.Count + 1, cargo[0].BuyAt, cargo[0].BuySystem,
                        string.Join("\n", cargo.Select(x => $"Купить {x.Commodity}: {x.Scu:N0} SCU за {x.Investment:N0} aUEC.")), load, r.Capacity, cash));
                }
                var last = trades[0];
                stops.Add(new(stops.Count + 1, last.SellAt, last.SellSystem,
                    string.Join("\n", trades.GroupBy(x => x.CommodityId).Select(g => $"Продать {g.First().Commodity}: {g.Sum(x => x.Scu):N0} SCU за {g.Sum(x => x.Revenue):N0} aUEC.")),
                    0, r.Capacity, cash + trades.Sum(x => x.Revenue)));
                plans.Add(new("Сбор груза", trades, stops, r.Budget, r.Capacity));
            }
        }
        return plans;
    }
}
