using SCNexus.Models;
using SCNexus.Services;

namespace SCNexus.Tests;

public class RouteServiceTests
{
    [Fact]
    public void LimitsPurchaseByBudgetCargoStockAndDemand()
    {
        var now = DateTimeOffset.UtcNow;
        var data = new DataSnapshot(
            [
                new CommodityQuote { IdCommodity = 1, IdTerminal = 10, CommodityName = "Gold", PriceBuy = 100, ScuBuy = 50, DateModified = now.ToUnixTimeSeconds() },
                new CommodityQuote { IdCommodity = 1, IdTerminal = 20, CommodityName = "Gold", PriceSell = 150, ScuSell = 30, StatusSell = 1, DateModified = now.ToUnixTimeSeconds() }
            ],
            [
                new TradeTerminal { Id = 10, Name = "TDD New Babbage", CityName = "New Babbage", Type = "commodity", IsAvailableLive = 1 },
                new TradeTerminal { Id = 20, Name = "TDD Area18", CityName = "Area18", Type = "commodity", IsAvailableLive = 1 }
            ], now, now, false);
        var settings = new PersonalSettings { Balance = 3000, Reserve = 1000, CargoScu = 40, CurrentLocation = "New Babbage" };

        var route = Assert.Single(new RouteService().FindRoutes(data, settings));
        Assert.Equal(20, route.Scu);
        Assert.Equal(50, route.FillPercent);
        Assert.Equal(50, route.OriginStockScu);
        Assert.Equal(30, route.DestinationDemandScu);
        Assert.Equal(100, route.BuyPricePerScu);
        Assert.Equal(150, route.SellPricePerScu);
        Assert.Equal(2000, route.Investment);
        Assert.Equal(1000, route.Profit);

        settings.Balance = 10_000;
        route = Assert.Single(new RouteService().FindRoutes(data, settings));
        Assert.Equal(30, route.Scu);
    }

    [Fact]
    public void ExcludesNqaUnlessOptedIn()
    {
        var now = DateTimeOffset.UtcNow;
        var data = new DataSnapshot(
            [new CommodityQuote { IdCommodity = 1, IdTerminal = 10, PriceBuy = 100, ScuBuy = 10, DateModified = now.ToUnixTimeSeconds() },
             new CommodityQuote { IdCommodity = 1, IdTerminal = 20, PriceSell = 200, ScuSell = 10, DateModified = now.ToUnixTimeSeconds() }],
            [new TradeTerminal { Id = 10, CityName = "New Babbage", Type = "commodity", IsAvailableLive = 1 },
             new TradeTerminal { Id = 20, CityName = "Area18", Type = "commodity", IsAvailableLive = 1, IsNqa = 1 }],
            now, now, false);
        var settings = new PersonalSettings { Balance = 5000, CargoScu = 10, CurrentLocation = "New Babbage" };

        Assert.Empty(new RouteService().FindRoutes(data, settings));
        settings.AllowRisky = true;
        Assert.True(Assert.Single(new RouteService().FindRoutes(data, settings)).Risky);
    }

    [Fact]
    public void UsesSelectedStarSystemWhenLocationsShareAName()
    {
        var now = DateTimeOffset.UtcNow;
        var data = new DataSnapshot(
            [new CommodityQuote { IdCommodity = 1, IdTerminal = 10, PriceBuy = 100, ScuBuy = 10, DateModified = now.ToUnixTimeSeconds() },
             new CommodityQuote { IdCommodity = 1, IdTerminal = 11, PriceBuy = 100, ScuBuy = 10, DateModified = now.ToUnixTimeSeconds() },
             new CommodityQuote { IdCommodity = 1, IdTerminal = 20, PriceSell = 200, ScuSell = 10, DateModified = now.ToUnixTimeSeconds() }],
            [new TradeTerminal { Id = 10, Name = "Terminal A", CityName = "Shared", StarSystemName = "Stanton", Type = "commodity", IsAvailableLive = 1 },
             new TradeTerminal { Id = 11, Name = "Terminal B", CityName = "Shared", StarSystemName = "Pyro", Type = "commodity", IsAvailableLive = 1 },
             new TradeTerminal { Id = 20, Name = "Destination", CityName = "Elsewhere", StarSystemName = "Stanton", Type = "commodity", IsAvailableLive = 1 }],
            now, now, false);
        var settings = new PersonalSettings { Balance = 5000, CargoScu = 10, CurrentLocation = "Shared", CurrentSystem = "Pyro" };

        Assert.Equal("Terminal B", Assert.Single(new RouteService().FindRoutes(data, settings)).BuyAt);
    }

    [Fact]
    public void WithoutStartingLocationFindsRoutesFromAllOrigins()
    {
        var now = DateTimeOffset.UtcNow;
        var data = new DataSnapshot(
            [new CommodityQuote { IdCommodity = 1, IdTerminal = 10, CommodityName = "Gold", PriceBuy = 100, ScuBuy = 10, DateModified = now.ToUnixTimeSeconds() },
             new CommodityQuote { IdCommodity = 1, IdTerminal = 11, CommodityName = "Gold", PriceBuy = 100, ScuBuy = 10, DateModified = now.ToUnixTimeSeconds() },
             new CommodityQuote { IdCommodity = 1, IdTerminal = 20, CommodityName = "Gold", PriceSell = 200, ScuSell = 10, DateModified = now.ToUnixTimeSeconds() }],
            [new TradeTerminal { Id = 10, Name = "Stanton Origin", CityName = "Area18", StarSystemName = "Stanton", Type = "commodity", IsAvailableLive = 1 },
             new TradeTerminal { Id = 11, Name = "Pyro Origin", CityName = "Ruin", StarSystemName = "Pyro", Type = "commodity", IsAvailableLive = 1 },
             new TradeTerminal { Id = 20, Name = "Destination", CityName = "Lorville", StarSystemName = "Stanton", Type = "commodity", IsAvailableLive = 1 }], now, now, false);
        var settings = new PersonalSettings { Balance = 5000, CargoScu = 10, CurrentLocation = "Не указана", CurrentSystem = "Stanton" };

        var routes = new RouteService().FindRoutes(data, settings);
        Assert.Equal(2, routes.Count);
        Assert.Contains(routes, x => x.BuyAt == "Stanton Origin");
        var pyro = Assert.Single(routes, x => x.BuyAt == "Pyro Origin");
        Assert.Equal("Pyro", pyro.BuySystem);
        Assert.Equal("Stanton", pyro.SellSystem);
        Assert.True(pyro.IsPyroRoute);
        Assert.True(pyro.Risky);
        Assert.Contains("ОПАСНО: Pyro", pyro.RiskDisplay);
    }
}
