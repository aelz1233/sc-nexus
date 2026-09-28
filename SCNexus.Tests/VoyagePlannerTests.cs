using SCNexus.Models;
using SCNexus.Services;

namespace SCNexus.Tests;

public class VoyagePlannerTests
{
    private static CommodityQuote Quote(int terminal, int commodity, decimal buy = 0, decimal sell = 0, decimal stock = 20, decimal demand = 20) =>
        new() { IdTerminal = terminal, IdCommodity = commodity, CommodityName = $"Cargo {commodity}", PriceBuy = buy, PriceSell = sell,
            ScuBuy = stock, ScuSell = demand, DateModified = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
    private static DataSnapshot Data(params CommodityQuote[] quotes) => new(quotes,
        Enumerable.Range(1, 5).Select(i => new TradeTerminal { Id = i, Name = $"T{i}", StarSystemName = i == 5 ? "Pyro" : "Stanton", Type = "commodity", IsAvailableLive = 1 }).ToArray(),
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, false);

    [Fact]
    public void ChainReinvestsSalesAndConnectsExactTerminals()
    {
        var data = Data(Quote(1, 1, buy: 100), Quote(2, 1, sell: 200), Quote(2, 2, buy: 1500), Quote(3, 2, sell: 2000));
        var plan = Assert.Single(new VoyagePlanner().Calculate(data, new("Цепочка", 10, 1000, DestinationId: 3, StartLocation: "T1")));
        Assert.Equal(3, plan.Stops.Count);
        Assert.Equal(10, plan.Trades[0].Scu);
        Assert.Equal(1, plan.Trades[1].Scu); // This cargo costs more than the original budget.
        Assert.Equal(1500, plan.Profit);
        Assert.Equal(2500, plan.Stops[^1].Cash);
        Assert.All(plan.Stops, s => Assert.True(s.Cash >= 0));
    }

    [Fact]
    public void CollectionSharesCapacityBudgetAndDemandAcrossVendors()
    {
        var data = Data(Quote(1, 1, buy: 100, stock: 4), Quote(2, 1, buy: 110, stock: 20), Quote(3, 1, sell: 200, demand: 7));
        var plans = new VoyagePlanner().Calculate(data, new("Сбор груза", 10, 650, DestinationId: 3, StartLocation: "T1"));
        Assert.NotEmpty(plans);
        foreach (var plan in plans)
        {
            Assert.Equal(3, plan.Stops.Count);
            Assert.True(plan.Trades.Sum(x => x.Scu) <= 7);
            Assert.True(plan.Investment <= 650);
            Assert.True(plan.Trades.Where(x => x.BuyTerminalId == 1).Sum(x => x.Scu) <= 4);
            Assert.All(plan.Stops, s => { Assert.InRange(s.CargoScu, 0, 10); Assert.True(s.Cash >= 0); });
            Assert.Equal(650 + plan.Profit, plan.Stops[^1].Cash);
            Assert.Equal(0, plan.Stops[^1].CargoScu);
            Assert.All(plan.Trades, x => Assert.Equal(3, x.SellTerminalId));
        }
    }

    [Fact]
    public void CollectionCombinesDifferentCargoWithoutOverfilling()
    {
        var data = Data(Quote(1, 1, buy: 10, stock: 4), Quote(2, 2, buy: 20, stock: 5), Quote(3, 1, sell: 30), Quote(3, 2, sell: 40));
        var plans = new VoyagePlanner().Calculate(data, new("Сбор груза", 7, 200, DestinationId: 3));
        Assert.NotEmpty(plans);
        Assert.All(plans, p => { Assert.Equal(2, p.Trades.Select(x => x.CommodityId).Distinct().Count()); Assert.Equal(7, p.PeakCargo); });
    }

    [Fact]
    public void SearchHonorsPyroStartDestinationAndStopLimit()
    {
        var data = Data(Quote(1, 1, buy: 10), Quote(2, 1, sell: 20), Quote(2, 2, buy: 10), Quote(5, 2, sell: 20));
        var request = new VoyageRequest("Цепочка", 10, 1000, 2, 5, "T1");
        var plan = Assert.Single(new VoyagePlanner().Calculate(data, request));
        Assert.Contains("Pyro", plan.RiskDisplay);
        Assert.Equal(3, plan.Stops.Count);
        Assert.Empty(new VoyagePlanner().Calculate(data, request with { AvoidPyro = true }));
        Assert.Empty(new VoyagePlanner().Calculate(data, request with { SameSystemOnly = true }));
        Assert.Empty(new VoyagePlanner().Calculate(data, request with { StartSystem = "Pyro" }));
        Assert.Empty(new VoyagePlanner().Calculate(data, request with { DestinationId = 4 }));
    }

    [Fact]
    public void ChainDoesNotRevisitTerminalOrInventProfitableCycles()
    {
        var data = Data(Quote(1, 1, buy: 10), Quote(2, 1, sell: 20), Quote(2, 2, buy: 10), Quote(1, 2, sell: 20));
        Assert.Empty(new VoyagePlanner().Calculate(data, new("Цепочка", 10, 1000)));
    }

    [Fact]
    public void NoPlansForNoBudgetSingleCargoUnitOrUnavailableSales()
    {
        var data = Data(Quote(1, 1, buy: 10), Quote(2, 1, buy: 10), Quote(3, 1, sell: 20));
        Assert.Empty(new VoyagePlanner().Calculate(data, new("Сбор груза", 10, 0)));
        Assert.Empty(new VoyagePlanner().Calculate(data, new("Сбор груза", 1, 1000)));
        data.Quotes[^1].StatusSell = 1;
        Assert.Empty(new VoyagePlanner().Calculate(data, new("Сбор груза", 10, 1000)));
    }

    [Fact]
    public void CollectionHonorsTotalMinimumsAndCancellation()
    {
        var data = Data(Quote(1, 1, buy: 10, stock: 2), Quote(2, 1, buy: 10, stock: 2), Quote(3, 1, sell: 20));
        var request = new VoyageRequest("Сбор груза", 10, 1000);
        Assert.Empty(new VoyagePlanner().Calculate(data, request with { MinimumFill = 50 }));
        Assert.Empty(new VoyagePlanner().Calculate(data, request with { MinimumProfit = 100 }));
        Assert.Throws<OperationCanceledException>(() => new VoyagePlanner().Calculate(data, request, new CancellationToken(true)));
    }
}
