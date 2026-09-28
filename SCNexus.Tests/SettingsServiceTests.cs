using Microsoft.Data.Sqlite;
using SCNexus.Models;
using SCNexus.Services;

namespace SCNexus.Tests;

public class SettingsServiceTests
{
    [Fact]
    public async Task ExistingStageOneDatabaseGetsNewSettingsColumns()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SCNexusTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "legacy.db");
        try
        {
            await using (var connection = new SqliteConnection($"Data Source={path}"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE PersonalSettings (Id INTEGER NOT NULL PRIMARY KEY, Balance TEXT NOT NULL, CurrentShip TEXT NOT NULL, CurrentLocation TEXT NOT NULL); INSERT INTO PersonalSettings VALUES (1, '5000000', 'C2', 'New Babbage');";
                await command.ExecuteNonQueryAsync();
            }
            var service = new SettingsService(path);
            var settings = await service.LoadAsync();
            Assert.Equal(5_000_000, settings.Balance);
            settings.CargoScu = 696;
            settings.Reserve = 1_000_000;
            settings.CurrentSystem = "Stanton";
            settings.AvoidPyro = true;
            settings.MinimumFillPercent = 50;
            settings.MinimumProfit = 100_000;
            settings.GameDirectoryPath = @"D:\RSI\StarCitizen\LIVE";
            await service.SaveAsync(settings);
            Assert.Equal(696, (await service.LoadAsync()).CargoScu);
            Assert.Equal("Stanton", (await service.LoadAsync()).CurrentSystem);
            var saved = await service.LoadAsync();
            Assert.True(saved.AvoidPyro);
            Assert.Equal(50, saved.MinimumFillPercent);
            Assert.Equal(100_000, saved.MinimumProfit);
            Assert.Equal(@"D:\RSI\StarCitizen\LIVE", saved.GameDirectoryPath);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task SettingsSurviveServiceRestart()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SCNexusTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "nexus.db");
        try
        {
            var first = new SettingsService(path);
            var defaults = await first.LoadAsync();
            Assert.Equal(0, defaults.Balance);

            await first.SaveAsync(new PersonalSettings
            {
                Balance = 10_430_000,
                CurrentShip = "C2 Hercules",
                CurrentLocation = "New Babbage",
                CargoScu = 696,
                Reserve = 1_000_000
            });

            var restarted = new SettingsService(path);
            var saved = await restarted.LoadAsync();
            Assert.Equal(10_430_000, saved.Balance);
            Assert.Equal("C2 Hercules", saved.CurrentShip);
            Assert.Equal("New Babbage", saved.CurrentLocation);
            Assert.Equal(696, saved.CargoScu);
            Assert.Equal(1_000_000, saved.Reserve);

            await restarted.SaveAsync(new PersonalSettings
            {
                Balance = 11_000_000,
                CurrentShip = "C2 Hercules",
                CurrentLocation = "Area18"
            });
            Assert.Equal("Area18", (await first.LoadAsync()).CurrentLocation);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
