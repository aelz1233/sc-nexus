using Microsoft.Data.Sqlite;
using System.Net;
using System.Net.Http;
using System.Reflection;
using SCNexus.Controls;
using SCNexus.Models;
using SCNexus.Services;
using SCNexus.ViewModels;

namespace SCNexus.Tests;

public class WorkspaceTests
{
    private sealed class MarketHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var body = request.RequestUri!.AbsolutePath.EndsWith("terminals", StringComparison.Ordinal)
                ? """{"status":"ok","data":[{"id":1,"name":"Origin","city_name":"Area18","type":"commodity","is_available_live":1}]}"""
                : """{"status":"ok","data":[{"id_commodity":1,"id_terminal":1,"commodity_name":"Gold","price_buy":100,"scu_buy":10}]}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    [Fact]
    public async Task FindRoutesRefreshesAnOldInMemoryMarketSnapshot()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SCNexusTests", Guid.NewGuid().ToString("N"));
        using var client = new HttpClient(new MarketHandler()) { BaseAddress = new Uri("https://example.test/2.0/") };
        try
        {
            var data = new GameDataService(client, Path.Combine(directory, "cache"));
            var fresh = await data.GetSnapshotAsync();
            var field = typeof(MainViewModel).GetField("_haulingData", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var settings = new SettingsService(Path.Combine(directory, "nexus.db"));
            var vm = new MainViewModel(settings, new TradingService(data, new RouteService()),
                new FlightLogService(settings), data, new GameLogService(directory), new HaulingService(), new UpdateService());
            field.SetValue(vm, fresh with { PricesFetchedAt = DateTimeOffset.UtcNow.AddHours(-2) });

            await vm.FindRoutesCommand.ExecuteAsync(null);

            var refreshed = Assert.IsType<DataSnapshot>(field.GetValue(vm));
            Assert.True(DateTimeOffset.UtcNow - refreshed.PricesFetchedAt < TimeSpan.FromMinutes(1));
        }
        finally { SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("Stanton")]
    [InlineData("Pyro")]
    public void RouteStartSystemFiltersOriginsWithoutChangingDetectedLocation(string startSystem)
    {
        var dir = Path.Combine(Path.GetTempPath(), "SCNexusTests", Guid.NewGuid().ToString("N"));
        using var client = new HttpClient(new MarketHandler()) { BaseAddress = new Uri("https://example.test/2.0/") };
        try
        {
            var dataService = new GameDataService(client, Path.Combine(dir, "cache"));
            var settings = new SettingsService(Path.Combine(dir, "nexus.db"));
            var vm = new MainViewModel(settings, new TradingService(dataService, new RouteService()),
                new FlightLogService(settings), dataService, new GameLogService(dir), new HaulingService(), new UpdateService());
            vm.Balance = 10_000;
            var ship = new ShipSummary(new PersonalShip { Id = 1, Name = "C2", CargoScu = 10 }, 0);
            vm.Ships.Add(ship);
            vm.SelectedShip = ship;
            vm.CurrentLocation = "Area18";
            vm.CurrentSystem = "Stanton";

            var now = DateTimeOffset.UtcNow;
            var timestamp = now.ToUnixTimeSeconds();
            var snapshot = new DataSnapshot(
                [
                    new CommodityQuote { IdTerminal = 1, IdCommodity = 1, CommodityName = "Gold", PriceBuy = 100, ScuBuy = 10, DateModified = timestamp },
                    new CommodityQuote { IdTerminal = 2, IdCommodity = 1, CommodityName = "Gold", PriceBuy = 110, ScuBuy = 10, DateModified = timestamp },
                    new CommodityQuote { IdTerminal = 3, IdCommodity = 1, CommodityName = "Gold", PriceSell = 200, ScuSell = 20, DateModified = timestamp }
                ],
                [
                    new TradeTerminal { Id = 1, Name = "Area18", CityName = "Area18", StarSystemName = "Stanton", Type = "commodity", IsAvailableLive = 1 },
                    new TradeTerminal { Id = 2, Name = "Ruin", CityName = "Ruin", StarSystemName = "Pyro", Type = "commodity", IsAvailableLive = 1 },
                    new TradeTerminal { Id = 3, Name = "New Babbage", CityName = "New Babbage", StarSystemName = "Stanton", Type = "commodity", IsAvailableLive = 1 }
                ], now, now, false);
            typeof(MainViewModel).GetField("_haulingData", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(vm, snapshot);
            var recalculate = typeof(MainViewModel).GetMethod("RecalculateHauling", BindingFlags.Instance | BindingFlags.NonPublic)!;
            recalculate.Invoke(vm, null);

            Assert.Equal("Все системы", vm.RouteStartSystem);
            Assert.Contains(vm.RouteResults, result => result.Direct?.BuySystem == "Pyro");
            Assert.Contains(vm.RouteResults, result => result.Direct?.BuySystem == "Stanton");

            vm.RouteStartSystems.Add(startSystem);
            vm.RouteStartSystem = startSystem;
            Assert.NotEmpty(vm.RouteResults);
            Assert.All(vm.RouteResults, route => Assert.Equal(startSystem, route.Direct!.BuySystem));
            Assert.Equal("Stanton", vm.CurrentSystem);
            Assert.Equal("Area18", vm.CurrentLocation);

            vm.RouteStartSystem = "Все системы";
            Assert.Contains(vm.RouteResults, result => result.Direct?.BuySystem == "Pyro");
        }
        finally { SqliteConnection.ClearAllPools(); if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Theory]
    [InlineData("25 000 000", "25000000")]
    [InlineData("25\u00a0000\u202f000,50", "25000000.50")]
    [InlineData("150.75", "150.75")]
    public void RussianMoneyInputAcceptsSpacesAndBothDecimalSeparators(string text, string expected)
    {
        Assert.True(MoneyText.TryParse(text, out var value));
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), value);
    }

    [Theory]
    [InlineData("-100")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("1,5.2")]
    public void InvalidMoneyDoesNotReplaceSavedValue(string text) => Assert.False(MoneyText.TryParse(text, out _));

    [Fact]
    public async Task BackupContainsSettingsFleetAndActiveFlight()
    {
        var dir = Path.Combine(Path.GetTempPath(), "SCNexusTests", Guid.NewGuid().ToString("N"));
        try
        {
            var settings = new SettingsService(Path.Combine(dir, "source.db"));
            await settings.LoadAsync();
            await settings.SaveAsync(new PersonalSettings { Balance = 123456, MonitorEnabled = false, MonitorIntervalSeconds = 60, ShowRouteDetails = false });
            var log = new FlightLogService(settings);
            var ship = await log.AddShipAsync("C2", 696, "Грузоперевозки", "Основной");
            await log.StartFlightAsync(ship.Id, ship.Name, "A", "B", "Cargo", 1000);
            var destination = Path.Combine(dir, "copy.db");
            await settings.BackupAsync(destination);
            var restored = new SettingsService(destination);
            var saved = await restored.LoadAsync();
            Assert.Equal(123456, saved.Balance);
            Assert.False(saved.MonitorEnabled);
            Assert.Equal(60, saved.MonitorIntervalSeconds);
            Assert.False(saved.ShowRouteDetails);
            var data = await new FlightLogService(restored).LoadAsync();
            Assert.Single(data.Ships);
            Assert.Single(data.Flights);
            Assert.Null(data.Flights[0].EndedAtUtc);
        }
        finally { SqliteConnection.ClearAllPools(); if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void CsvQuotesNamesAndDoesNotReportActiveInvestmentAsLoss()
    {
        var csv = FlightExport.ToCsv([new FlightRecord { ShipName = "=formula", Commodity = "A;B", Investment = 100, StartedAtUtc = DateTime.UtcNow }]);
        Assert.Contains("\"'=formula\"", csv);
        Assert.Contains("\"A;B\"", csv);
        Assert.Contains(";;В пути", csv);
    }
}
