using Microsoft.Data.Sqlite;
using SCNexus.Models;
using SCNexus.Services;

namespace SCNexus.Tests;

public class SettingsServiceTests
{
    private sealed class TestClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task OcrIntervalDefaultsToFiveAndSurvivesRestartAndOldSchemaUpgrade()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SCNexusTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "nexus.db");
        try
        {
            var service = new SettingsService(path);
            var settings = await service.LoadAsync();
            Assert.Equal(5, settings.OcrIntervalSeconds);
            settings.OcrIntervalSeconds = 10;
            settings.LightTheme = true;
            settings.AutoFleetOcrEnabled = false;
            settings.FleetOcrAutoScroll = false;
            await service.SaveAsync(settings);
            var saved = await new SettingsService(path).LoadAsync();
            Assert.Equal(10, saved.OcrIntervalSeconds);
            Assert.True(saved.LightTheme);
            Assert.False(saved.AutoFleetOcrEnabled);
            Assert.False(saved.FleetOcrAutoScroll);
            await using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "ALTER TABLE PersonalSettings DROP COLUMN OcrIntervalSeconds";
                await command.ExecuteNonQueryAsync();
                command.CommandText = "ALTER TABLE PersonalSettings DROP COLUMN BalanceManualUpdatedAt";
                await command.ExecuteNonQueryAsync();
            }
            var upgradedService = new SettingsService(path);
            var upgraded = await upgradedService.LoadAsync();
            Assert.Equal(5, upgraded.OcrIntervalSeconds);
            Assert.Null(upgraded.BalanceManualUpdatedAt);
            upgraded.BalanceManualUpdatedAt = new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
            await upgradedService.SaveAsync(upgraded);
            Assert.Equal(upgraded.BalanceManualUpdatedAt, (await new SettingsService(path).LoadAsync()).BalanceManualUpdatedAt);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task CorruptDatabaseIsRestoredFromTheLatestVerifiedBackup()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SCNexusTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "nexus.db");
        try
        {
            var service = new SettingsService(path);
            await service.LoadAsync();
            await service.SaveAsync(new PersonalSettings { Balance = 12_400_000, CurrentShip = "C2 Hercules" });
            Directory.CreateDirectory(service.BackupDirectory);
            await service.BackupAsync(Path.Combine(service.BackupDirectory, "nexus-2026-10-02.db"));

            await using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                await connection.OpenAsync();
                await using var checkpoint = connection.CreateCommand();
                checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)";
                await checkpoint.ExecuteNonQueryAsync();
            }

            SqliteConnection.ClearAllPools();
            await File.WriteAllTextAsync(path, "this is not a sqlite database");

            var recovered = new SettingsService(path);
            var settings = await recovered.LoadAsync();
            Assert.Equal(12_400_000, settings.Balance);
            Assert.Equal("C2 Hercules", settings.CurrentShip);
            Assert.Contains("восстановлена", recovered.StartupRecoveryMessage);
            Assert.Single(Directory.GetFiles(directory, "nexus-corrupt-*.db"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task NullValuesFromAnOlderDatabaseAreNormalizedBeforeSave()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SCNexusTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "nexus.db");
        try
        {
            var service = new SettingsService(path);
            await service.LoadAsync();
            await service.SaveAsync(new PersonalSettings
            {
                CurrentShip = null!, CurrentLocation = null!, CurrentSystem = null!,
                GameDirectoryPath = null!, Language = null!, OverlayAnchor = null!,
                OverlayHotkey = null!, ActiveVoyageJson = null!, LastSessionSummary = null!
            });

            var saved = await service.LoadAsync();
            Assert.Equal("Не выбран", saved.CurrentShip);
            Assert.Equal("Не указана", saved.CurrentLocation);
            Assert.Equal("", saved.CurrentSystem);
            Assert.Equal("", saved.OverlayHotkey);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task InvalidStoredTypesProduceRecoveryMessageBeforeLoadingRecords()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SCNexusTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "invalid.db");
        try
        {
            await using (var connection = new SqliteConnection($"Data Source={path}"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE PersonalSettings (Id INTEGER PRIMARY KEY, CargoScu INTEGER NOT NULL CHECK(typeof(CargoScu) = 'integer')); PRAGMA ignore_check_constraints=ON; INSERT INTO PersonalSettings VALUES (1, 'broken'); PRAGMA ignore_check_constraints=OFF;";
                await command.ExecuteNonQueryAsync();
            }
            var error = await Assert.ThrowsAsync<InvalidDataException>(() => new SettingsService(path).LoadAsync());
            Assert.Contains("Повреждена локальная база", error.Message);
            Assert.Contains(path, error.Message);
            await using var check = new SqliteConnection($"Data Source={path}");
            await check.OpenAsync();
            await using var query = check.CreateCommand();
            query.CommandText = "SELECT CargoScu FROM PersonalSettings WHERE Id = 1";
            Assert.Equal("broken", await query.ExecuteScalarAsync());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, true);
        }
    }

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
            settings.OcrEnabled = true;
            settings.Language = "en";
            settings.OverlayEnabled = false;
            settings.OverlayExpanded = true;
            settings.OverlayOpacity = .75;
            settings.OverlayHotkey = "Ctrl+Alt+O";
            settings.ActiveVoyageJson = "{\"route\":1}";
            settings.ActiveShoppingJson = "{\"shopping\":1}";
            await service.SaveAsync(settings);
            Assert.Equal(696, (await service.LoadAsync()).CargoScu);
            Assert.Equal("Stanton", (await service.LoadAsync()).CurrentSystem);
            var saved = await service.LoadAsync();
            Assert.True(saved.AvoidPyro);
            Assert.Equal(50, saved.MinimumFillPercent);
            Assert.Equal(100_000, saved.MinimumProfit);
            Assert.Equal(@"D:\RSI\StarCitizen\LIVE", saved.GameDirectoryPath);
            Assert.True(saved.OcrEnabled);
            Assert.Equal("en", saved.Language);
            Assert.False(saved.OverlayEnabled);
            Assert.True(saved.OverlayExpanded);
            Assert.Equal(.75, saved.OverlayOpacity, 2);
            Assert.Equal("Ctrl+Alt+O", saved.OverlayHotkey);
            Assert.Equal("{\"route\":1}", saved.ActiveVoyageJson);
            Assert.Equal("{\"shopping\":1}", saved.ActiveShoppingJson);
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

    [Fact]
    public async Task ExistingDatabaseGetsOneDailyAutomaticBackup()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SCNexusTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "nexus.db");
        try
        {
            var first = new SettingsService(path);
            await first.LoadAsync();
            await first.SaveAsync(new PersonalSettings { Balance = 12_400_000, CurrentShip = "Guardian MX" });

            var restarted = new SettingsService(path);
            await restarted.LoadAsync();
            var backup = Assert.Single(Directory.GetFiles(restarted.BackupDirectory, "nexus-*.db"));

            var backupSettings = await new SettingsService(backup).LoadAsync();
            Assert.Equal(12_400_000, backupSettings.Balance);
            Assert.Equal("Guardian MX", backupSettings.CurrentShip);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task RunningAppCreatesANewBackupAfterTheDateChanges()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SCNexusTests", Guid.NewGuid().ToString("N"));
        var clock = new TestClock(new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero));
        try
        {
            var service = new SettingsService(Path.Combine(directory, "nexus.db"), clock);
            await service.LoadAsync();
            await service.SaveAsync(new PersonalSettings { Balance = 100 });
            Assert.Single(Directory.GetFiles(service.BackupDirectory, "nexus-*.db"));

            clock.Now = clock.Now.AddDays(1);
            await service.SaveAsync(new PersonalSettings { Balance = 200 });
            var backups = Directory.GetFiles(service.BackupDirectory, "nexus-*.db");
            Assert.Equal(2, backups.Length);
            var newer = backups.OrderByDescending(Path.GetFileName).First();
            Assert.Equal(200, (await new SettingsService(newer).LoadAsync()).Balance);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task PreUpdateBackupContainsCurrentSettings()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SCNexusTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "nexus.db");
        try
        {
            var service = new SettingsService(path);
            await service.LoadAsync();
            await service.SaveAsync(new PersonalSettings { Balance = 7_654_321, CurrentShip = "C2 Hercules" });

            var backup = await service.CreatePreUpdateBackupAsync(new Version(0, 5, 0));

            Assert.Contains("before-v0.5.0", Path.GetFileName(backup));
            var restored = await new SettingsService(backup).LoadAsync();
            Assert.Equal(7_654_321, restored.Balance);
            Assert.Equal("C2 Hercules", restored.CurrentShip);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
