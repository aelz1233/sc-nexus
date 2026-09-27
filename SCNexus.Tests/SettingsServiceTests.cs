using Microsoft.Data.Sqlite;
using SCNexus.Models;
using SCNexus.Services;

namespace SCNexus.Tests;

public class SettingsServiceTests
{
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
                CurrentLocation = "New Babbage"
            });

            var restarted = new SettingsService(path);
            var saved = await restarted.LoadAsync();
            Assert.Equal(10_430_000, saved.Balance);
            Assert.Equal("C2 Hercules", saved.CurrentShip);
            Assert.Equal("New Babbage", saved.CurrentLocation);

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
