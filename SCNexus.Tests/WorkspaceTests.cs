using Microsoft.Data.Sqlite;
using SCNexus.Controls;
using SCNexus.Models;
using SCNexus.Services;

namespace SCNexus.Tests;

public class WorkspaceTests
{
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
