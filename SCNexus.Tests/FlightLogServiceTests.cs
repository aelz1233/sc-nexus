using Microsoft.Data.Sqlite;
using SCNexus.Services;

namespace SCNexus.Tests;

public class FlightLogServiceTests
{
    [Fact]
    public async Task ExistingDatabaseCanTrackFleetAndActualProfitAcrossRestart()
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

            var settings = new SettingsService(path);
            await settings.LoadAsync();
            var log = new FlightLogService(settings);
            var ship = await log.AddShipAsync("C2 Hercules", 696, "Торговля", "Грузовой билд");
            var start = new DateTime(2026, 9, 27, 10, 0, 0, DateTimeKind.Utc);
            var flight = await log.StartFlightAsync(ship.Id, ship.Name, "New Babbage", "Area18", "Gold", 1_000_000, start);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                log.StartFlightAsync(ship.Id, ship.Name, "Area18", "New Babbage", "Gold", 500_000, start));
            var finished = await log.FinishFlightAsync(flight.Id, 1_000_000, 1_500_000, 50_000, 25_000, start.AddMinutes(30));
            Assert.Equal(425_000, finished.Profit);
            Assert.Equal(850_000, finished.ProfitPerHour);

            var restarted = new FlightLogService(new SettingsService(path));
            var (ships, flights) = await restarted.LoadAsync();
            Assert.Equal(425_000, Assert.Single(ships).Earned);
            Assert.Equal(425_000, Assert.Single(flights).Profit);
            var removed = await restarted.DeleteFinishedFlightAsync(finished.Id);
            Assert.NotNull(removed);
            Assert.Equal(425_000, removed.Profit);
            Assert.Empty((await restarted.LoadAsync()).Flights);

            var second = await restarted.StartFlightAsync(ship.Id, ship.Name, "A", "B", "Gold", 100, start.AddHours(1));
            await Assert.ThrowsAsync<InvalidOperationException>(() => restarted.DeleteFinishedFlightAsync(second.Id));
            await restarted.DeleteShipAsync(ship.Id);
            var afterDelete = await restarted.LoadAsync();
            Assert.Empty(afterDelete.Ships);
            Assert.Equal("C2 Hercules", Assert.Single(afterDelete.Flights).ShipName);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, true);
        }
    }
}
