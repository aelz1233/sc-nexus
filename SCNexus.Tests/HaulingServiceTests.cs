using SCNexus.Models;
using SCNexus.Services;

namespace SCNexus.Tests;

public class HaulingServiceTests
{
    [Fact]
    public void RespectsBudgetCargoAndInterstellarFilter()
    {
        var now = DateTimeOffset.UtcNow;
        var data = new DataSnapshot(
            [
                new CommodityQuote { IdCommodity = 1, IdTerminal = 1, CommodityName = "Titanium", PriceBuy = 100, ScuBuy = 20, DateModified = now.ToUnixTimeSeconds() },
                new CommodityQuote { IdCommodity = 1, IdTerminal = 2, CommodityName = "Titanium", PriceSell = 140, ScuSell = 8, DateModified = now.ToUnixTimeSeconds() },
                new CommodityQuote { IdCommodity = 1, IdTerminal = 3, CommodityName = "Titanium", PriceSell = 180, ScuSell = 10, DateModified = now.ToUnixTimeSeconds() }
            ],
            [
                new TradeTerminal { Id = 1, Name = "Origin", StarSystemName = "Stanton", PlanetName = "Hurston", Type = "commodity", IsAvailableLive = 1 },
                new TradeTerminal { Id = 2, Name = "Same", StarSystemName = "Stanton", PlanetName = "Crusader", Type = "commodity", IsAvailableLive = 1 },
                new TradeTerminal { Id = 3, Name = "Cross", StarSystemName = "Pyro", PlanetName = "Pyro I", Type = "commodity", IsAvailableLive = 1 }
            ], now, now, false);
        var service = new HaulingService();
        var all = service.Calculate(data, 10, 500, true, false, "За рейс");
        Assert.Equal(2, all.Count);
        Assert.Equal("Cross", all[0].SellAt);
        Assert.Equal(5, all[0].Scu);
        Assert.Equal(50, all[0].FillPercent);
        Assert.Equal("Межзвёздный", all[0].Category);
        Assert.True(all[0].IsPyroRoute);
        Assert.True(all[0].IsDangerous);
        Assert.Contains("ОПАСНО: Pyro", all[0].RiskDisplay);
        Assert.Single(service.Calculate(data, 10, 500, true, true, "За рейс"));
        var stellar = service.Calculate(data, 10, 500, true, false, "За рейс", "Звёздный");
        Assert.Single(stellar);
        Assert.Equal("Same", stellar[0].SellAt);
        Assert.Empty(service.Calculate(data, 10, 500, true, false, "За рейс",
            startLocation: "Missing"));
        Assert.Equal(2, service.Calculate(data, 10, 500, true, false, "За рейс",
            startLocation: "Origin", startSystem: "Stanton").Count);
        Assert.Empty(service.Calculate(data, 10, 500, true, false, "За рейс",
            startLocation: "Origin", startSystem: "Pyro"));
        Assert.Equal(2, service.Calculate(data, 10, 500, true, false, "За рейс",
            startSystem: "Stanton").Count);
        Assert.Empty(service.Calculate(data, 10, 500, true, false, "За рейс",
            startSystem: "Pyro"));
        Assert.Single(service.Calculate(data, 10, 500, true, false, "За рейс", avoidPyro: true));
        Assert.Empty(service.Calculate(data, 10, 500, true, false, "За рейс", minimumFillPercent: 51));
        Assert.Single(service.Calculate(data, 10, 500, true, false, "За рейс", minimumProfit: 300));
    }
}
