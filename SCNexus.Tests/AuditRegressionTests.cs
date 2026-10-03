using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SCNexus.Models;
using SCNexus.Services;
using SCNexus.ViewModels;
using System.Net.Http;
using System.Reflection;

namespace SCNexus.Tests;

public class AuditRegressionTests
{
    [Fact]
    public void ComponentChecklistRestoresProgressAndDoesNotSpendBalance()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SCNexusTests", Guid.NewGuid().ToString("N"));
        using var client = new HttpClient();
        try
        {
            MainViewModel Create()
            {
                var settings = new SettingsService(Path.Combine(directory, "nexus.db"));
                var data = new GameDataService(client, Path.Combine(directory, "cache"));
                return new MainViewModel(settings, new TradingService(data, new RouteService()), new FlightLogService(settings),
                    data, new GameLogService(directory), new HaulingService(), new UpdateService());
            }
            var vm = Create();
            vm.Balance = 10000;
            var item = new ComponentShoppingItem("FR-76", 2, 100);
            var plan = new ComponentShoppingPlan("Balanced",
                [new(1, "Stanton", "Area18", "Shop A", [item], 200),
                 new(2, "Pyro", "Ruin Station", "Shop B", [new("XL-1", 1, 500)], 500)], 700, 700, 0);
            vm.TrackComponentShopping(plan, "C2");
            Assert.False(vm.CompleteChecklistStopCommand.CanExecute(null));
            vm.OverlayChecklist.Single().IsDone = true;
            Assert.True(item.IsPurchased);
            Assert.True(vm.CompleteChecklistStopCommand.CanExecute(null));
            vm.CompleteChecklistStopCommand.Execute(null);
            Assert.True(vm.OverlayChecklistDanger);
            Assert.Equal(10000, vm.Balance);
            var saved = typeof(MainViewModel).GetMethod("SerializeShoppingGuidance", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(vm, null);
            var restored = Create();
            typeof(MainViewModel).GetMethod("RestoreShoppingGuidance", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(restored, [saved]);
            Assert.True(restored.OverlayChecklistDanger);
            Assert.Contains("XL-1", restored.OverlayChecklist.Single().Text);
            restored.PreviousChecklistStopCommand.Execute(null);
            Assert.True(restored.OverlayChecklist.Single().IsDone);
            restored.OverlayChecklist.Single().IsDone = false;
            Assert.False(restored.CompleteChecklistStopCommand.CanExecute(null));
            restored.CloseShoppingGuidanceCommand.Execute(null);
            Assert.False(restored.HasShoppingGuidance);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static readonly VehicleCatalogItem[] Vehicles =
    [
        new() { Id = 1, Name = "Guardian", IsSpaceship = 1 },
        new() { Id = 2, Name = "Guardian MX", IsSpaceship = 1 },
        new() { Id = 3, Name = "C2 Hercules Starlifter", IsSpaceship = 1 }
    ];

    [Theory]
    [InlineData("Vehicle Loadout\nGuardian MX")]
    [InlineData("Fleet Manager\nGuardian MX\nStored")]
    public void OcrFleetDoesNotInventCurrentShipOrShorterVariant(string text)
    {
        var result = OcrProvider.ParseText(text, Vehicles, DateTimeOffset.UtcNow);
        Assert.DoesNotContain(result.Values, x => x.Key == "player.ship");
        var ship = Assert.IsType<DetectedShip>(Assert.Single(result.Records).Value);
        Assert.Equal("Guardian MX", ship.Name.Value);
        Assert.False(ship.IsCurrent);
    }

    [Theory]
    [InlineData("Current Ship: Guardian MX", "Guardian MX")]
    [InlineData("Current Ship: Vehicle Loadout", null)]
    [InlineData("Guardian MX", null)]
    public void OcrRequiresKnownModelAndExplicitCurrentShipLabel(string text, string? expected)
    {
        var result = OcrProvider.ParseText(text, Vehicles, DateTimeOffset.UtcNow);
        Assert.Equal(expected, result.Values.FirstOrDefault(x => x.Key == "player.ship")?.Value);
    }

    [Fact]
    public void RepeatedObservationsPreserveManualCorrectionsAndStartupDoesNotNotifyRecovery()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SCNexusTests", Guid.NewGuid().ToString("N"));
        try
        {
            var settings = new SettingsService(Path.Combine(directory, "nexus.db"));
            using var client = new HttpClient();
            var data = new GameDataService(client, Path.Combine(directory, "cache"));
            var vm = new MainViewModel(settings, new TradingService(data, new RouteService()), new FlightLogService(settings),
                data, new GameLogService(directory), new HaulingService(), new UpdateService());
            var apply = typeof(MainViewModel).GetMethod("ApplyDataSnapshot", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var now = DateTimeOffset.UtcNow;
            var snapshot = new DataCollectionSnapshot { Player = new PlayerState
            {
                Balance = new(10000, DataSourceKind.GameLog, now, 1),
                CurrentLocation = new("Area18", DataSourceKind.GameLog, now, 1),
                CurrentSystem = new("Stanton", DataSourceKind.GameLog, now, 1)
            } };
            apply.Invoke(vm, [snapshot]);
            Assert.Equal(10000, vm.Balance);
            vm.Balance = 12000;
            vm.CurrentLocation = "Orison";
            vm.SelectedSystem = "Pyro";
            apply.Invoke(vm, [snapshot]);
            Assert.Equal(12000, vm.Balance);
            Assert.Equal("Pyro", vm.CurrentSystem);
            Assert.Equal("Не указана", vm.CurrentLocation);
            vm.SelectedSystem = null!;
            Assert.Equal("", vm.CurrentSystem);
            Assert.False(vm.OverlayDanger);
            var notify = typeof(MainViewModel).GetMethod("UpdateProviderNotifications", BindingFlags.NonPublic | BindingFlags.Instance)!;
            foreach (var status in new[] { "Waiting", "Updating", "OK" })
                notify.Invoke(vm, [new[] { new DataSourceInfo { Name = "UEX", Source = DataSourceKind.Uex, Status = status, IsAvailable = status == "OK" } }]);
            Assert.Empty(vm.Notifications);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void OcrDoesNotTreatShopPricesAsPlayerBalance()
    {
        Assert.DoesNotContain(OcrProvider.ParseText("BUY 12,400,000 aUEC", Vehicles, DateTimeOffset.UtcNow).Values, x => x.Key == "player.balance");
        Assert.Equal("12400000", Assert.Single(OcrProvider.ParseText("Balance: 12,400,000 aUEC", Vehicles, DateTimeOffset.UtcNow).Values).Value);
    }

    [Fact]
    public async Task FinishingAndDeletingFlightUpdateBalanceAtomically()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SCNexusTests", Guid.NewGuid().ToString("N"));
        try
        {
            var settings = new SettingsService(Path.Combine(directory, "nexus.db"));
            await settings.LoadAsync();
            await settings.SaveAsync(new PersonalSettings { Balance = 10000 });
            var log = new FlightLogService(settings);
            var flight = await log.StartFlightAsync(null, "C2", "A", "B", "Gold", 100, DateTime.UtcNow.AddHours(-1));
            await using (var db = settings.CreateDbContext())
                await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER reject_balance BEFORE UPDATE OF Balance ON PersonalSettings BEGIN SELECT RAISE(ABORT, 'simulated disk failure'); END;");
            await Assert.ThrowsAsync<DbUpdateException>(() => log.FinishFlightAsync(flight.Id, 100, 200, 0, 0, updateBalance: true));
            Assert.Null(Assert.Single((await log.LoadAsync()).Flights).EndedAtUtc);
            Assert.Equal(10000, (await settings.LoadAsync()).Balance);
            await using (var db = settings.CreateDbContext())
                await db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_balance;");
            await log.FinishFlightAsync(flight.Id, 100, 200, 0, 0, updateBalance: true);
            Assert.Equal(10100, (await settings.LoadAsync()).Balance);
            await Assert.ThrowsAsync<InvalidOperationException>(() => log.FinishFlightAsync(flight.Id, 100, 200, 0, 0, updateBalance: true));
            await log.DeleteFinishedFlightAsync(flight.Id, updateBalance: true);
            Assert.Equal(10000, (await settings.LoadAsync()).Balance);
            Assert.Empty((await log.LoadAsync()).Flights);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task RecoverySkipsUnrelatedButHealthySqliteBackup()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SCNexusTests", Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(directory, "nexus.db");
            var settings = new SettingsService(path);
            await settings.LoadAsync();
            await settings.SaveAsync(new PersonalSettings { Balance = 123456 });
            Directory.CreateDirectory(settings.BackupDirectory);
            var valid = Path.Combine(settings.BackupDirectory, "nexus-valid.db");
            await settings.BackupAsync(valid);
            File.SetLastWriteTimeUtc(valid, DateTime.UtcNow.AddDays(-1));
            var unrelated = Path.Combine(settings.BackupDirectory, "nexus-unrelated.db");
            await using (var db = new SqliteConnection($"Data Source={unrelated};Pooling=False"))
            {
                await db.OpenAsync();
                await using var command = db.CreateCommand();
                command.CommandText = "CREATE TABLE OtherData (Id INTEGER)";
                await command.ExecuteNonQueryAsync();
            }
            await using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                await connection.OpenAsync();
                await using var checkpoint = connection.CreateCommand();
                checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)";
                await checkpoint.ExecuteNonQueryAsync();
            }
            SqliteConnection.ClearAllPools();
            await File.WriteAllTextAsync(path, "corrupt original");
            var recovered = new SettingsService(path);
            Assert.Equal(123456, (await recovered.LoadAsync()).Balance);
            Assert.Contains("nexus-valid.db", recovered.StartupRecoveryMessage);
            Assert.Equal("corrupt original", await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(directory, "nexus-corrupt-*.db"))));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task DailyRetentionKeepsPreUpdateBackups()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SCNexusTests", Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(directory, "nexus.db");
            var settings = new SettingsService(path);
            await settings.LoadAsync();
            await settings.SaveAsync(new PersonalSettings { Balance = 100 });
            var preUpdate = await settings.CreatePreUpdateBackupAsync(new Version(0, 5, 4));
            File.SetLastWriteTimeUtc(preUpdate, DateTime.UtcNow.AddYears(-1));
            for (var day = 1; day <= 8; day++)
                await settings.BackupAsync(Path.Combine(settings.BackupDirectory, $"nexus-{DateTime.Today.AddDays(-day):yyyy-MM-dd}.db"));
            await new SettingsService(path).LoadAsync();
            Assert.True(File.Exists(preUpdate));
            Assert.Equal(8, Directory.GetFiles(settings.BackupDirectory, "*.db").Length);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
